using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Recruitment;

/// <summary>
/// Request by a company member to create or update a candidate evaluation (<c>portal.candidate_evaluations</c>).
/// </summary>
public sealed record UpsertCandidateEvaluationRequest(
    short? Score,
    string Note) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Score is < 0 or > 100)
            yield return new ValidationResult("Evaluation score must be between 0 and 100.", [nameof(Score)]);

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
