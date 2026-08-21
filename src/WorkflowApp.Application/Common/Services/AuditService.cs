using System.Text.Json;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Entities.Tasks;

namespace WorkflowApp.Application.Common.Services;

/// <summary>
/// Writes to the technical/security audit stream (<see cref="AuditLog"/>) — distinct from the
/// business timeline (<c>TaskActivity</c>). Append-only: this interface deliberately offers no
/// update or delete.
/// </summary>
public interface IAuditService
{
    /// <summary>Stages an audit row. The caller's <c>SaveChangesAsync</c> commits it in the same transaction.</summary>
    void Record(
        string action,
        long? actorUserId = null,
        string? entityType = null,
        long? entityId = null,
        object? previousValues = null,
        object? newValues = null,
        string? ipAddress = null,
        string? deviceInfo = null);
}

/// <summary>Well-known audit action names, so queries and alerts can rely on stable strings.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string Logout = "Auth.Logout";
    public const string TokenRefreshed = "Auth.TokenRefreshed";
    public const string TokenReuseDetected = "Auth.TokenReuseDetected";
    public const string AccountLockedOut = "Auth.AccountLockedOut";
    public const string PasswordChanged = "Auth.PasswordChanged";
    public const string PasswordResetByAdmin = "Auth.PasswordResetByAdmin";
    public const string UserCreated = "Admin.UserCreated";
    public const string UserActivated = "Admin.UserActivated";
    public const string UserDeactivated = "Admin.UserDeactivated";
    public const string UserRolesChanged = "Admin.UserRolesChanged";

    /// <summary>A supervisor closed someone else's shift. Always carries a reason.</summary>
    public const string ShiftForceEnded = "Workforce.ShiftForceEnded";

    /// <summary>The background sweep closed a shift left open past the configured maximum.</summary>
    public const string ShiftAutoClosed = "Workforce.ShiftAutoClosed";

    /// <summary>Triage approved a request — the only path that creates executable work.</summary>
    public const string RequestApproved = "Request.Approved";
    public const string RequestRejected = "Request.Rejected";
    public const string RequestDuplicated = "Request.MarkedDuplicate";
    public const string RequestTriaged = "Request.Triaged";

    /// <summary>A status transition forced outside the workflow map. Always carries a reason.</summary>
    public const string WorkflowOverride = "Task.WorkflowOverride";

    public const string AttachmentUploaded = "Attachment.Uploaded";
    public const string AttachmentDownloaded = "Attachment.Downloaded";
}

public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IWorkflowDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AuditService(IWorkflowDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public void Record(
        string action,
        long? actorUserId = null,
        string? entityType = null,
        long? entityId = null,
        object? previousValues = null,
        object? newValues = null,
        string? ipAddress = null,
        string? deviceInfo = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            ActorUserId = actorUserId ?? _currentUser.UserId,
            EntityType = entityType,
            EntityId = entityId,
            PreviousValues = Serialize(previousValues),
            NewValues = Serialize(newValues),
            IpAddress = ipAddress ?? _currentUser.IpAddress,
            DeviceInfo = deviceInfo ?? _currentUser.UserAgent
        });
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
