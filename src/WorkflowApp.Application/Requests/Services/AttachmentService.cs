using Microsoft.EntityFrameworkCore;
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
    Task<Result<AttachmentDto>> UploadAsync(
        long? requestId, long? taskId, long uploaderId,
        Stream content, string fileName, string contentType, CancellationToken ct = default);

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

    public AttachmentService(IWorkflowDbContext db, IFileStorage storage, IAuditService audit)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
    }

    public async Task<Result<AttachmentDto>> UploadAsync(
        long? requestId, long? taskId, long uploaderId,
        Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        // Exactly one owner: an attachment belongs to a request or a task, never both or neither.
        if (requestId.HasValue == taskId.HasValue)
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.owner_required", "Attach to exactly one of a request or a task."));

        if (requestId is { } rid && !await _db.Requests.AnyAsync(r => r.Id == rid, ct))
            return Result<AttachmentDto>.Failure(Error.NotFound("request.not_found", "Request not found."));

        if (taskId is { } tid && !await _db.Tasks.AnyAsync(t => t.Id == tid, ct))
            return Result<AttachmentDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        // Allow-list by extension. The client-declared content type is stored for the response but
        // never trusted as the security check.
        if (!_storage.IsAllowedFileName(fileName))
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.type_not_allowed", $"Files of type '{Path.GetExtension(fileName)}' are not accepted."));

        if (content.CanSeek && content.Length > _storage.MaxFileSizeBytes)
            return Result<AttachmentDto>.Failure(Error.Validation(
                "attachment.too_large",
                $"Maximum file size is {_storage.MaxFileSizeBytes / (1024 * 1024)} MB."));

        var stored = await _storage.SaveAsync(content, fileName, requestId.HasValue ? "requests" : "tasks", ct);

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
            TaskId = taskId
        };

        _db.Attachments.Add(attachment);

        _audit.Record(
            AuditActions.AttachmentUploaded,
            actorUserId: uploaderId,
            entityType: nameof(Attachment),
            entityId: attachment.Id,
            newValues: new { attachment.OriginalFileName, attachment.SizeBytes, requestId, taskId });

        await _db.SaveChangesAsync(ct);

        return Result<AttachmentDto>.Success(new AttachmentDto(
            attachment.Id, attachment.OriginalFileName, attachment.ContentType,
            attachment.SizeBytes, uploaderId, attachment.CreatedAt));
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
