using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Recruitment;

/// <summary>
/// Request to create or update a recruitment round (<c>portal.recruitment_stages</c>) for a job offer.
/// </summary>
public sealed record UpsertRecruitmentStageRequest(
    string Name,
    string Kind,
    int Position,
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

        if (Position < 1)
            yield return new ValidationResult("Stage position must be positive.", [nameof(Position)]);
    }
}

/// <summary>
/// Request to reorder recruitment rounds for a job offer.
/// </summary>
public sealed record ReorderRecruitmentStagesRequest(
    IReadOnlyList<Guid> OrderedStageIds) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OrderedStageIds is null || OrderedStageIds.Count == 0)
        {
            yield return new ValidationResult("Provide all stage IDs in the requested order.", [nameof(OrderedStageIds)]);
            yield break;
        }

        if (OrderedStageIds.Contains(Guid.Empty) || OrderedStageIds.Distinct().Count() != OrderedStageIds.Count)
            yield return new ValidationResult("Stage IDs must be non-empty and unique.", [nameof(OrderedStageIds)]);
    }
}

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
