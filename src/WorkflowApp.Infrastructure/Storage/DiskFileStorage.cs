using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;

namespace WorkflowApp.Infrastructure.Storage;

/// <summary>
/// Stores attachments on disk under a configured root.
///
/// Two things matter here. The stored name is generated, never taken from the upload — an attacker
/// controls the original file name, and letting it reach the file system invites traversal and
/// collisions. And every resolved path is verified to sit under the root before it is touched, so
/// a crafted stored path in the database still cannot escape.
/// </summary>
public sealed class DiskFileStorage : IFileStorage
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<DiskFileStorage> _logger;
    private readonly string _root;

    public DiskFileStorage(IOptions<FileStorageOptions> options, ILogger<DiskFileStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        _root = Path.GetFullPath(_options.Root);
    }

    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    public bool IsAllowedFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) &&
               _options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, string originalFileName, string category, CancellationToken ct = default)
    {
        // Date-partitioned so a directory never accumulates an unmanageable number of entries.
        var relativeDirectory = Path.Combine(Sanitize(category), DateTime.UtcNow.ToString("yyyy'/'MM"));
        var absoluteDirectory = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        // Generated name; only the extension survives from what the client sent.
        var extension = Path.GetExtension(originalFileName);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = Path.Combine(relativeDirectory, storedName).Replace('\\', '/');
        var absolutePath = ResolveWithinRoot(relativePath);

        long size;
        byte[] hash;

        await using (var destination = File.Create(absolutePath))
        using (var sha = SHA256.Create())
        {
            // Hash while writing so the file is never read back a second time.
            await using var hashing = new CryptoStream(destination, sha, CryptoStreamMode.Write);
            await content.CopyToAsync(hashing, ct);
            await hashing.FlushFinalBlockAsync(ct);

            size = destination.Length;
            hash = sha.Hash!;
        }

        _logger.LogInformation("Stored attachment {RelativePath} ({SizeBytes} bytes)", relativePath, size);
        return new StoredFile(relativePath, size, Convert.ToHexString(hash));
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var absolutePath = ResolveWithinRoot(relativePath);

        if (!File.Exists(absolutePath))
        {
            // The metadata row outlived the file. Report it and let the caller 404 rather than throw.
            _logger.LogWarning("Attachment {RelativePath} is recorded but missing from disk.", relativePath);
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(
            new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true));
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var absolutePath = ResolveWithinRoot(relativePath);
        if (File.Exists(absolutePath)) File.Delete(absolutePath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a relative path against the root and refuses anything that lands outside it.
    /// This is the last line of defence against a traversal sequence reaching the file system.
    /// </summary>
    private string ResolveWithinRoot(string relativePath)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (!absolutePath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Resolved path escapes the storage root: {relativePath}");

        return absolutePath;
    }

    private static string Sanitize(string segment)
    {
        var cleaned = new string(segment.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return cleaned.Length == 0 ? "misc" : cleaned;
    }
}
