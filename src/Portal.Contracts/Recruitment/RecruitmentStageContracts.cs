using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Recruitment;

/// <summary>
/// Request to create or update a recruitment round (<c>portal.recruitment_stages</c>) for a job offer.
/// </summary>
public sealed record UpsertRecruitmentStageRequest(
    [property: Required, MinLength(1)] string Name,
    [property: Required] string Kind,
    [property: Range(1, int.MaxValue)] int Position,
    string? Description = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Stage name cannot be empty or whitespace.",
                [nameof(Name)]);
        }

        if (!RecruitmentStageKinds.All.Contains(Kind))
        {
            yield return new ValidationResult(
                $"Invalid stage kind '{Kind}'. Allowed values: {string.Join(", ", RecruitmentStageKinds.All)}.",
                [nameof(Kind)]);
        }
    }
}

/// <summary>
/// Request to reorder recruitment rounds for a job offer.
/// </summary>
public sealed record ReorderRecruitmentStagesRequest(
    [property: Required, MinLength(1)] IReadOnlyList<Guid> OrderedStageIds);

/// <summary>
/// Response representing a recruitment round (<c>portal.recruitment_stages</c>).
/// </summary>
public sealed record RecruitmentStageResponse(
    Guid Id,
    Guid JobOfferId,
    string Name,
    string Kind,
    int Position,
    string? Description);