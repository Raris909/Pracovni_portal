using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Recruitment;

/// <summary>
/// Request by a company member to create or update an interview time slot (<c>portal.interview_slots</c>).
/// Enforces DB constraint <c>ends_at > starts_at</c>.
/// </summary>
public sealed record UpsertInterviewSlotRequest(
    Guid InterviewerMemberId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location = null,
    string? MeetingUrl = null,
    bool IsActive = true) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAt == default)
            yield return new ValidationResult("StartsAt is required.", [nameof(StartsAt)]);
        if (EndsAt == default)
            yield return new ValidationResult("EndsAt is required.", [nameof(EndsAt)]);
        if (MeetingUrl is not null && (!Uri.TryCreate(MeetingUrl, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)))
            yield return new ValidationResult("MeetingUrl must be an absolute HTTP or HTTPS URL.", [nameof(MeetingUrl)]);

        if (InterviewerMemberId == Guid.Empty)
        {
            yield return new ValidationResult(
                "InterviewerMemberId must be a non-empty GUID.",
                [nameof(InterviewerMemberId)]);
        }

        // PostgreSQL stores timestamps with microsecond precision.
        if (EndsAt.UtcTicks / TimeSpan.TicksPerMicrosecond <= StartsAt.UtcTicks / TimeSpan.TicksPerMicrosecond)
        {
            yield return new ValidationResult(
                "EndsAt must be strictly greater than StartsAt.",
                [nameof(EndsAt), nameof(StartsAt)]);
        }
    }
}

/// <summary>
/// Response representing an interview time slot (<c>portal.interview_slots</c>).
/// </summary>
public sealed record InterviewSlotResponse(
    Guid Id,
    Guid JobOfferId,
    Guid CompanyId,
    Guid StageId,
    string StageKind,
    Guid InterviewerMemberId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location,
    string? MeetingUrl,
    bool IsActive,
    bool IsBooked,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request by a candidate to book an available interview slot (<c>portal.interview_bookings</c>).
/// </summary>
public sealed record CreateInterviewBookingRequest(
    Guid SlotId) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SlotId == Guid.Empty)
        {
            yield return new ValidationResult(
                "SlotId must be a non-empty GUID.",
                [nameof(SlotId)]);
        }
    }
}

/// <summary>
/// Request to update the status of an interview booking (<c>portal.interview_bookings</c>).
/// Enforces DB constraint: <c>(status = 'cancelled') = (cancelled_at IS NOT NULL)</c>.
/// </summary>
public sealed record UpdateInterviewBookingStatusRequest(
    [property: Required] string Status,
    DateTimeOffset? CancelledAt = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!InterviewBookingStatuses.All.Contains(Status))
        {
            yield return new ValidationResult(
                $"Invalid booking status '{Status}'. Allowed values: {string.Join(", ", InterviewBookingStatuses.All)}.",
                [nameof(Status)]);
            yield break;
        }

        if (Status == InterviewBookingStatuses.Cancelled && CancelledAt is null)
        {
            yield return new ValidationResult(
                "CancelledAt is required when status is 'cancelled'.",
                [nameof(CancelledAt)]);
        }

        if (Status != InterviewBookingStatuses.Cancelled && CancelledAt is not null)
        {
            yield return new ValidationResult(
                $"CancelledAt must be null when status is '{Status}'.",
                [nameof(CancelledAt)]);
        }
    }
}

/// <summary>
/// Response representing an interview booking (<c>portal.interview_bookings</c>).
/// </summary>
public sealed record InterviewBookingResponse(
    Guid Id,
    Guid SlotId,
    Guid ApplicationId,
    Guid StageId,
    Guid JobOfferId,
    Guid CompanyId,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location,
    string? MeetingUrl,
    DateTimeOffset BookedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset UpdatedAt);
