using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Applications;

public static class ApplicationStatuses
{
    public const string Submitted = "submitted";
    public const string InProgress = "in_progress";
    public const string Rejected = "rejected";
    public const string Accepted = "accepted";
    public const string Withdrawn = "withdrawn";
}

public sealed class CreateApplicationRequest : IValidatableObject
{
    public Guid JobOfferId { get; set; }

    [MaxLength(10000)]
    public string? CoverMessage { get; set; }

    [Required]
    public List<Guid> DocumentIds { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (JobOfferId == Guid.Empty)
            yield return new ValidationResult("A job offer ID is required.", [nameof(JobOfferId)]);

        if (DocumentIds is not null)
        {
            if (DocumentIds.Contains(Guid.Empty))
                yield return new ValidationResult("Document IDs must not be empty.", [nameof(DocumentIds)]);

            if (DocumentIds.Distinct().Count() != DocumentIds.Count)
                yield return new ValidationResult("Document IDs must be unique.", [nameof(DocumentIds)]);
        }
    }
}

public sealed class UpdateApplicationStatusRequest
{
    [Required, RegularExpression("^(in_progress|rejected|accepted)$")]
    public string Status { get; set; } = "";
}

public sealed class AssignApplicationRequest : IValidatableObject
{
    public Guid? AssignedMemberId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AssignedMemberId == Guid.Empty)
            yield return new ValidationResult("Use null to remove the assignment.", [nameof(AssignedMemberId)]);
    }
}

public sealed record ApplicationResponse(
    Guid Id,
    Guid JobOfferId,
    Guid CompanyId,
    Guid CandidateId,
    string JobTitle,
    string CompanyName,
    string CandidateFirstName,
    string CandidateLastName,
    Guid? AssignedMemberId,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

public sealed record ApplicationDetailResponse(
    ApplicationResponse Application,
    string? CoverMessage,
    ApplicationProfileSnapshot ProfileSnapshot,
    IReadOnlyList<ApplicationDocumentResponse> Documents);
