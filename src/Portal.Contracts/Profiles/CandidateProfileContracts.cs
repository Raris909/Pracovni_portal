using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Profiles;

public sealed class UpdateCandidateProfileRequest : IValidatableObject
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = "";

    [Required, MaxLength(100)]
    public string LastName { get; set; } = "";

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Headline { get; set; }

    [MaxLength(10000)]
    public string? Summary { get; set; }

    [MaxLength(200)]
    public string? City { get; set; }

    [RegularExpression("^[A-Z]{2}$")]
    public string? CountryCode { get; set; }

    [Required, MaxLength(50)]
    public List<string> Skills { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Skills is not null)
        {
            if (Skills.Any(skill => string.IsNullOrWhiteSpace(skill) || skill.Length > 100))
                yield return new ValidationResult("Each skill must be non-empty and at most 100 characters long.",
                    [nameof(Skills)]);

            if (Skills.Select(skill => skill?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Skills.Count)
                yield return new ValidationResult("Skills must be unique, ignoring case and surrounding whitespace.",
                    [nameof(Skills)]);
        }
    }
}

public sealed record CandidateProfileResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    string? Headline,
    string? Summary,
    string? City,
    string? CountryCode,
    IReadOnlyList<string> Skills,
    IReadOnlyList<CandidateExperienceResponse> Experiences,
    IReadOnlyList<CandidateEducationResponse> Education,
    Guid? CurrentCvId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
