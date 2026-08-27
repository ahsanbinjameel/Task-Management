using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Domain.Entities.Requests;

namespace WorkflowApp.Application.Requests.Services;

/// <summary>An attachment ready to stream back, with the metadata needed for the response headers.</summary>
public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType);

public interface IAttachmentService
{
    /// <summary>
    /// Stores a file against exactly one owner. A batch is the third because the screenshot showing
    /// all eight problems belongs to the submission, not to whichever item happened to be first;
    /// a verification is the fourth because a checker's evidence has to survive whether or not the
    /// investigation ever produces a task to hang it on.
    /// </summary>
    Task<Result<AttachmentDto>> UploadAsync(
        long? requestId, long? taskId, long uploaderId,
        Stream content, string fileName, string contentType, CancellationToken ct = default,
        long? batchId = null, AttachmentKind kind = AttachmentKind.General,
        long? verificationId = null);

    /// <summary>
    /// Ties the evidence a checker staged to the attempt they have just recorded.
    ///
    /// Evidence is uploaded before the verdict, because the attempt does not exist until the
    /// verdict is submitted. This claims whatever that checker left unclaimed on the task, so a
    /// rejected submission leaves the files staged for the retry rather than stranding them.
    /// Stages the change; the caller commits it with the verdict.
    /// </summary>
    Task ClaimQCEvidenceAsync(long taskId, long reviewerId, long qcReviewId, CancellationToken ct = default);

    Task<Result<AttachmentDownload>> DownloadAsync(long attachmentId, long actingUserId, CancellationToken ct = default);

    Task<Result> DeleteAsync(long attachmentId, long actingUserId, CancellationToken ct = default);
}

/// <summary>
/// Attachment metadata and access control. The binary itself never goes in a transactional table —
/// it lives on disk behind <see cref="IFileStorage"/>, and every read passes through here so it can
/// be authorized and audited rather than served from a guessable URL.
/// </summary>
public sealed class AttachmentService : IAttachmentService
{
    private readonly IWorkflowDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;

    public AttachmentService(
        IWorkflowDbContext db, IFileStorage storage, IAuditService audit, ICurrentUser currentUser)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<Result<AttachmentDto>> UploadAsync(
        long? requestId, long? taskId, long uploaderId,
        Stream content, string fileName, string contentType, CancellationToken ct = default,
        long? batchId = null, AttachmentKind kind = AttachmentKind.General,
        long? verificationId = null)
    {
        // Completion proof and QC evidence belong to a task and to nothing else. A "completion
        // proof" hanging off a request would be a claim about work that has not been created yet.
        if (kind is AttachmentKind.CompletionProof or AttachmentKind.QCEvidence && taskId is null)
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.kind_needs_task", "Proof and quality-check evidence attach to a task."));

        // And the mirror of it: a verification's evidence belongs to the verification. Filing it
        // against a task would be filing it against work that in most cases does not exist.
        if (kind == AttachmentKind.VerificationEvidence && verificationId is null)
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.kind_needs_verification", "Verification evidence attaches to a verification."));

        // Exactly one owner: a request, a task, a batch or a verification — never two, never none.
        // Counted rather than compared, which is what made adding this fourth owner a one-line
        // change instead of an unpicking of nested conditions.
        var owners = (requestId.HasValue ? 1 : 0) + (taskId.HasValue ? 1 : 0)
            + (batchId.HasValue ? 1 : 0) + (verificationId.HasValue ? 1 : 0);
        if (owners != 1)
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.owner_required",
                "Attach to exactly one of a request, a task, a batch or a verification."));

        if (requestId is { } rid && !await _db.Requests.AnyAsync(r => r.Id == rid, ct))
            return Result<AttachmentDto>.Failure(Error.NotFound("request.not_found", "Request not found."));

        if (taskId is { } tid && !await _db.Tasks.AnyAsync(t => t.Id == tid, ct))
            return Result<AttachmentDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (batchId is { } bid && !await _db.RequestBatches.AnyAsync(b => b.Id == bid, ct))
            return Result<AttachmentDto>.Failure(Error.NotFound("batch.not_found", "Batch not found."));

        if (verificationId is { } vid && !await _db.Verifications.AnyAsync(v => v.Id == vid, ct))
            return Result<AttachmentDto>.Failure(
                Error.NotFound("verification.not_found", "Verification not found."));

        // Who may claim to have proved what. Checked here rather than on the controller because
        // the answer depends on the task, not only on a permission: the proof that work was done
        // is the responsible person's to give, and nobody else's.
        if (kind == AttachmentKind.CompletionProof)
        {
            var isAssignee = await _db.Tasks.AsNoTracking()
                .AnyAsync(t => t.Id == taskId && t.PrimaryAssigneeUserId == uploaderId, ct);

            if (!isAssignee)
                return Result<AttachmentDto>.Failure(Error.Forbidden(
                    "attachment.not_assignee",
                    "Only the person responsible for this work can attach proof that it is done."));
        }

        if (kind == AttachmentKind.QCEvidence && !_currentUser.Permissions.Contains(Permissions.TaskQCReview))
        {
            return Result<AttachmentDto>.Failure(Error.Forbidden(
                "attachment.not_checker", "Only a quality checker can attach evidence to a check."));
        }

        // Same shape of rule as CompletionProof, and here for the same reason: the answer depends
        // on the record rather than only on the caller. Evidence for an investigation is the
        // investigator's to supply, so holding Verification.Create — or every permission there is —
        // still does not let somebody else file material under the checker's name.
        if (kind == AttachmentKind.VerificationEvidence)
        {
            var isChecker = await _db.Verifications.AsNoTracking()
                .AnyAsync(v => v.Id == verificationId && v.AssignedToUserId == uploaderId, ct);

            if (!isChecker)
                return Result<AttachmentDto>.Failure(Error.Forbidden(
                    "attachment.not_verification_checker",
                    "Only the assigned checker can attach evidence to this verification."));
        }

        // Allow-list by extension. The client-declared content type is stored for the response but
        // never trusted as the security check.
        if (!_storage.IsAllowedFileName(fileName))
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.type_not_allowed", $"Files of type '{Path.GetExtension(fileName)}' are not accepted."));

        if (content.CanSeek && content.Length > _storage.MaxFileSizeBytes)
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.too_large",
                $"Maximum file size is {_storage.MaxFileSizeBytes / (1024 * 1024)} MB."));

        var folder = requestId.HasValue ? "requests"
            : batchId.HasValue ? "batches"
            : verificationId.HasValue ? "verifications"
            : "tasks";
        var stored = await _storage.SaveAsync(content, fileName, folder, ct);

        if (stored.SizeBytes > _storage.MaxFileSizeBytes)
        {
            // A non-seekable stream only reveals its size once written. Undo rather than keep it.
            await _storage.DeleteAsync(stored.RelativePath, ct);
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.too_large",
                $"Maximum file size is {_storage.MaxFileSizeBytes / (1024 * 1024)} MB."));
        }

        var attachment = new Attachment
        {
            OriginalFileName = Path.GetFileName(fileName),
            StoredPath = stored.RelativePath,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            UploadedByUserId = uploaderId,
            RequestId = requestId,
            BatchId = batchId,
            VerificationId = verificationId,
            Kind = kind,
            TaskId = taskId
        };

        _db.Attachments.Add(attachment);

        _audit.Record(
            AuditActions.AttachmentUploaded,
            actorUserId: uploaderId,
            entityType: nameof(Attachment),
            entityId: attachment.Id,
            newValues: new
            {
                attachment.OriginalFileName, attachment.SizeBytes,
                requestId, taskId, batchId, verificationId, Kind = kind.ToString()
            });

        await _db.SaveChangesAsync(ct);

        return Result<AttachmentDto>.Success(new AttachmentDto(
            attachment.Id, attachment.OriginalFileName, attachment.ContentType,
            attachment.SizeBytes, uploaderId, attachment.CreatedAt));
    }

    public async Task ClaimQCEvidenceAsync(
        long taskId, long reviewerId, long qcReviewId, CancellationToken ct = default)
    {
        var staged = await _db.Attachments
            .Where(a => a.TaskId == taskId
                        && a.Kind == AttachmentKind.QCEvidence
                        && a.QCReviewId == null
                        && a.UploadedByUserId == reviewerId)
            .ToListAsync(ct);

        // Scoped to this reviewer on purpose: two checkers can be looking at the same task, and
        // one of them must not have their pictures swept onto the other's verdict.
        foreach (var attachment in staged)
            attachment.QCReviewId = qcReviewId;
    }

    public async Task<Result<AttachmentDownload>> DownloadAsync(
        long attachmentId, long actingUserId, CancellationToken ct = default)
    {
        var attachment = await _db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);

        if (attachment is null)
            return Result<AttachmentDownload>.Failure(
                Error.NotFound("attachment.not_found", "Attachment not found."));

        var stream = await _storage.OpenReadAsync(attachment.StoredPath, ct);
        if (stream is null)
        {
            // The row outlived the file. Report it as missing rather than throwing.
            return Result<AttachmentDownload>.Failure(
                Error.NotFound("attachment.file_missing", "The stored file is no longer available."));
        }

        _audit.Record(
            AuditActions.AttachmentDownloaded,
            actorUserId: actingUserId,
            entityType: nameof(Attachment),
            entityId: attachmentId);

        await _db.SaveChangesAsync(ct);

        return Result<AttachmentDownload>.Success(
            new AttachmentDownload(stream, attachment.OriginalFileName, attachment.ContentType));
    }

    public async Task<Result> DeleteAsync(long attachmentId, long actingUserId, CancellationToken ct = default)
    {
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is null)
            return Result.Failure(Error.NotFound("attachment.not_found", "Attachment not found."));

        if (attachment.UploadedByUserId != actingUserId)
            return Result.Failure(Error.Forbidden(
                "attachment.not_owner", "Only the uploader can remove this attachment."));

        // The metadata row goes; the audit trail of the removal stays.
        _db.Attachments.Remove(attachment);

        _audit.Record(
            "Attachment.Removed",
            actorUserId: actingUserId,
            entityType: nameof(Attachment),
            entityId: attachmentId,
            previousValues: new { attachment.OriginalFileName, attachment.StoredPath, attachment.Sha256 });

        await _db.SaveChangesAsync(ct);
        await _storage.DeleteAsync(attachment.StoredPath, ct);

        return Result.Success();
    }
}
