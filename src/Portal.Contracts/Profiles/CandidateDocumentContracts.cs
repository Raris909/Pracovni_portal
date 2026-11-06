using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Profiles;

public static class CandidateDocumentKinds
{
    public const string Cv = "cv";
    public const string CoverLetter = "cover_letter";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Cv,
        CoverLetter,
        Other
    };
}

public sealed record CandidateDocumentResponse(
    Guid Id,
    Guid CandidateId,
    string Kind,
    string OriginalFilename,
    string MimeType,
    long SizeBytes,
    string Sha256,
    bool IsCurrentCv,
    DateTimeOffset CreatedAt);

public sealed record SetCurrentCvRequest(
    [property: Required] Guid DocumentId) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DocumentId == Guid.Empty)
        {
            yield return new ValidationResult(
                "DocumentId must be a non-empty GUID.",
                [nameof(DocumentId)]);
        }
    }
}