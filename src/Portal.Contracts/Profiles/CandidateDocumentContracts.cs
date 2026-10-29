using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Profiles;

public static class CandidateDocumentKinds
{
    public const string Cv = "cv";
    public const string CoverLetter = "cover_letter";
    public const string Other = "other";
}

public static class CvFileRules
{
    public const string Extension = ".pdf";
    public const string MimeType = "application/pdf";
    public const long MaxSizeBytes = 10 * 1024 * 1024;
    public const int MaxFilenameLength = 255;
}

public sealed class SetCurrentCvRequest : IValidatableObject
{
    public Guid? DocumentId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DocumentId == Guid.Empty)
            yield return new ValidationResult("Use null to clear the current CV.", [nameof(DocumentId)]);
    }
}

public sealed record CandidateDocumentResponse(
    Guid Id,
    string Kind,
    string OriginalFilename,
    string MimeType,
    long SizeBytes,
    DateTimeOffset CreatedAt);
