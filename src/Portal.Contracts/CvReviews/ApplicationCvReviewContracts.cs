using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.CvReviews;

/// <summary>
/// Request by a company member (HR) to trigger an AI analysis of a candidate's CV
/// for a specific job application.
/// If <see cref="DocumentId"/> is null, the backend selects the primary attached CV from <c>application_documents</c>.
/// </summary>
public sealed record CreateApplicationCvReviewRequest(
    Guid? DocumentId = null);

/// <summary>
/// Full response representing a record from <c>portal.application_cv_reviews</c>.
/// </summary>
public sealed record ApplicationCvReviewResponse(
    Guid Id,
    Guid ApplicationId,
    Guid CompanyId,
    Guid CandidateId,
    Guid DocumentId,
    string DocumentKind,
    Guid RequestedByMemberId,
    string Status,
    string Provider,
    string Model,
    string PromptVersion,
    JobRequirementsSnapshot JobRequirementsSnapshot,
    [property: Range(0, 100)] short? Score,
    ApplicationCvReviewResultPayload? Result,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Lightweight summary item for displaying AI match scores in an application list or kanban board.
/// </summary>
public sealed record ApplicationCvReviewSummaryResponse(
    Guid Id,
    Guid ApplicationId,
    Guid DocumentId,
    Guid RequestedByMemberId,
    string Status,
    [property: Range(0, 100)] short? Score,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);