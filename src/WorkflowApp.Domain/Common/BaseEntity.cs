namespace WorkflowApp.Domain.Common;

/// <summary>
/// Common conventions for all persisted entities: surrogate key, audit timestamps,
/// and an optimistic-concurrency token (maps to SQL ROWVERSION).
/// </summary>
public abstract class BaseEntity
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Set by created-by/updated-by interceptor once identity is wired (Phase 1).</summary>
    public long? CreatedByUserId { get; set; }
    public long? UpdatedByUserId { get; set; }

    /// <summary>SQL ROWVERSION — used for optimistic concurrency on mutating operations.</summary>
    public byte[]? RowVersion { get; set; }
}
