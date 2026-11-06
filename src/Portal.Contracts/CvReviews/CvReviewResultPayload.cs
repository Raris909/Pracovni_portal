using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.CvReviews;

/// <summary>
/// Structured JSONB payload (<c>result</c> column in <c>candidate_cv_reviews</c>)
/// returned by the AI for a general candidate self-review.
/// </summary>
public sealed record CandidateCvReviewResultPayload(
    [property: Required] string Summary,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<string>ExtractedSkills,
    IReadOnlyList<CvSectionFeedback>? SectionFeedback = null);

/// <summary>
/// Optional granular feedback per CV section (e.g., Experience, Education, Formatting).
/// </summary>
public sealed record CvSectionFeedback(
    [property: Required] string SectionName,
    [property: Range(0, 100)] int? Score,
    [property: Required] string Feedback);

/// <summary>
/// Structured JSONB payload (<c>result</c> column in <c>application_cv_reviews</c>)
/// returned by the AI when evaluating a candidate's CV against a specific job offer.
/// </summary>
public sealed record ApplicationCvReviewResultPayload(
    [property: Required] string Summary,
    IReadOnlyList<string> MatchingSkills,
    IReadOnlyList<string> MissingRequirements,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> RisksOrGaps,
    IReadOnlyList<string>? SuggestedInterviewQuestions = null,
    string? OverallRecommendation = null);

/// <summary>
/// Structured JSONB payload (<c>job_requirements_snapshot</c> column in <c>application_cv_reviews</c>)
/// capturing the state of the job offer at the moment the AI analysis was requested.
/// </summary>
public sealed record JobRequirementsSnapshot(
    [property: Required] string Title,
    [property: Required] string Description,
    [property: Required] string Requirements,
    string? Location,
    [property: Required] string WorkMode,
    [property: Required] string EmploymentType);