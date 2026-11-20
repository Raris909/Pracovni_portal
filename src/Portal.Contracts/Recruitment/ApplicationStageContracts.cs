using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Recruitment;

/// <summary>
/// Request to update an applicant's status in a specific recruitment stage (<c>portal.application_stages</c>).
/// Enforces DB constraints:
/// - <c>status = 'in_progress'</c> requires <c>StartedAt</c>.
/// - <c>status IN ('passed', 'failed', 'skipped')</c> requires both <c>StartedAt</c> and <c>CompletedAt >= StartedAt</c>.
/// </summary>
public sealed record UpdateApplicationStageStatusRequest(
    string Status,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!ApplicationStageStatuses.All.Contains(Status))
        {
            yield return new ValidationResult(
                $"Invalid application stage status '{Status}'.",
                [nameof(Status)]);
            yield break;
        }

        if (Status == ApplicationStageStatuses.InProgress && StartedAt is null)
        {
            yield return new ValidationResult(
                "StartedAt is required when status is 'in_progress'.",
                [nameof(StartedAt)]);
        }

        var isCompletedStatus = ApplicationStageStatuses.CompletedStatuses.Contains(Status);
        if (isCompletedStatus && CompletedAt is null)
        {
            yield return new ValidationResult(
                $"CompletedAt is required when status is '{Status}'.",
                [nameof(CompletedAt)]);
        }

        if (!isCompletedStatus && CompletedAt is not null)
        {
            yield return new ValidationResult(
                $"CompletedAt must be null when status is '{Status}'.",
                [nameof(CompletedAt)]);
        }

        if (CompletedAt is not null && StartedAt is null)
        {
            yield return new ValidationResult(
                "StartedAt is required when CompletedAt is provided.",
                [nameof(StartedAt)]);
        }

        if (StartedAt is not null && CompletedAt is not null && CompletedAt < StartedAt)
        {
            yield return new ValidationResult(
                "CompletedAt must be greater than or equal to StartedAt.",
                [nameof(CompletedAt), nameof(StartedAt)]);
        }
    }
}

/// <summary>
/// Response representing an applicant's state within a recruitment round (<c>portal.application_stages</c>).
/// </summary>
public sealed record ApplicationStageResponse(
    Guid ApplicationId,
    Guid StageId,
    Guid JobOfferId,
    Guid CompanyId,
    string StageName,
    string StageKind,
    int StagePosition,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);
