using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Recruitment;

/// <summary>
/// Request by a company member to create or update a candidate evaluation (<c>portal.candidate_evaluations</c>).
/// </summary>
public sealed record UpsertCandidateEvaluationRequest(
    [property: Range(0, 100)] short? Score,
    [property: Required, MinLength(1)] string Note) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Note))
        {
            yield return new ValidationResult(
                "Evaluation note cannot be empty or whitespace.",
                [nameof(Note)]);
        }
    }
}

/// <summary>
/// Response representing a candidate evaluation (<c>portal.candidate_evaluations</c>).
/// </summary>
public sealed record CandidateEvaluationResponse(
    Guid Id,
    Guid ApplicationId,
    Guid StageId,
    Guid CompanyId,
    Guid AuthorMemberId,
    [property: Range(0, 100)] short? Score,
    string Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);