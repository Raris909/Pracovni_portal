namespace Portal.Api.Profiles.Storage;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    /// <summary>
    /// Root path for private persistent file storage (e.g., "/var/lib/portal/storage" in Docker).
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "private-documents");

    /// <summary>
    /// Maximum allowed file size in bytes (default: 10 MB).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}