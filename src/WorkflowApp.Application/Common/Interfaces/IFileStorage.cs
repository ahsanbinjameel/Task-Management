namespace WorkflowApp.Application.Common.Interfaces;

/// <summary>What was written, and enough about it to record the metadata row.</summary>
public sealed record StoredFile(string RelativePath, long SizeBytes, string Sha256);

/// <summary>
/// Binary storage for attachments. Files live on disk under a configured root; only metadata goes
/// in the database, and every read goes through an authorized endpoint rather than a public URL.
/// </summary>
public interface IFileStorage
{
    /// <summary>Writes a stream and returns its storage-root-relative path plus content hash.</summary>
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, string category, CancellationToken ct = default);

    /// <summary>Opens a stored file for reading, or null when it is missing from disk.</summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>True when the extension is on the allow-list.</summary>
    bool IsAllowedFileName(string fileName);

    long MaxFileSizeBytes { get; }
}
