namespace Portal.Contracts.Applications;

public static class ApplicationDocumentKinds
{
    public const string Cv = "cv";
    public const string CoverLetter = "cover_letter";
    public const string Other = "other";
}

public sealed record ApplicationDocumentResponse(
    Guid DocumentId,
    string Kind,
    string OriginalFilename,
    string MimeType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset AttachedAt);
