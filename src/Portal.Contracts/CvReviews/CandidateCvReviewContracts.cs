using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.CvReviews;

/// <summary>
/// Request to trigger an AI analysis on a candidate's uploaded CV document.
/// If <see cref="DocumentId"/> is null, the backend can default to the candidate's <c>current_cv_id</c>.
/// </summary>
public sealed record CreateCandidateCvReviewRequest(
    Guid? DocumentId = null);

/// <summary>
/// Full response representing a record from <c>portal.candidate_cv_reviews</c>.
/// </summary>
public sealed record CandidateCvReviewResponse(
    Guid Id,
    Guid CandidateId,
    Guid DocumentId,
    string DocumentKind,
    string Status,
    string Provider,
    string Model,
    string PromptVersion,
    [property: Range(0, 100)] short? Score,
    CandidateCvReviewResultPayload? Result,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Lightweight summary item for listing a candidate's historical CV reviews.
/// </summary>
public sealed record CandidateCvReviewSummaryResponse(
    Guid Id,
    Guid DocumentId,
    string Status,
    [property: Range(0, 100)] short? Score,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);