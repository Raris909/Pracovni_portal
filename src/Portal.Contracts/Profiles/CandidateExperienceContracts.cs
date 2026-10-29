using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Profiles;

public sealed class CandidateExperienceRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public string Employer { get; set; } = "";

    [Required, MaxLength(200)]
    public string Position { get; set; } = "";

    [MaxLength(10000)]
    public string? Description { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate == default)
            yield return new ValidationResult("A start date is required.", [nameof(StartDate)]);

        if (EndDate.HasValue && EndDate.Value < StartDate)
            yield return new ValidationResult("End date must be on or after start date.", [nameof(EndDate)]);
    }
}

public sealed record CandidateExperienceResponse(
    Guid Id,
    string Employer,
    string Position,
    string? Description,
    DateOnly StartDate,
    DateOnly? EndDate);
