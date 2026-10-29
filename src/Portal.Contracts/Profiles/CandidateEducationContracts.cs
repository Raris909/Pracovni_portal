using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Profiles;

public sealed class CandidateEducationRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public string School { get; set; } = "";

    [MaxLength(200)]
    public string? FieldOfStudy { get; set; }

    [MaxLength(100)]
    public string? Degree { get; set; }

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

public sealed record CandidateEducationResponse(
    Guid Id,
    string School,
    string? FieldOfStudy,
    string? Degree,
    DateOnly StartDate,
    DateOnly? EndDate);
