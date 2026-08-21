namespace WorkflowApp.Application.Common.Options;

/// <summary>Bound from the <c>FileStorage</c> configuration section.</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Directory the binaries live under. Must be outside the web root.</summary>
    public string Root { get; set; } = "./storage";

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Extension allow-list, not a block-list: enumerating what is safe is the only version of this
    /// that stays safe as new dangerous extensions appear.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
        ".txt", ".csv", ".log", ".json", ".xml",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".7z", ".msg", ".eml"
    };
}
