using Portal.Contracts.Recruitment;

namespace Portal.Contracts.Applications;

/// <summary>
/// Allowed values for <c>portal.applications.status</c>.
/// </summary>
public static class ApplicationStatuses
{
    public const string Submitted = "submitted";
    public const string InProgress = "in_progress";
    public const string Rejected = "rejected";
    public const string Accepted = "accepted";
    public const string Withdrawn = "withdrawn";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Submitted,
        InProgress,
        Rejected,
        Accepted,
        Withdrawn
    };
}

/// <summary>
/// Summary item for listing the current candidate's job applications (<c>GET /api/applications/my</c> or <c>GET /api/profile/applications</c>).
/// </summary>
public sealed record CandidateApplicationSummaryResponse(
    Guid Id,
    Guid JobOfferId,
    string JobTitle,
    Guid CompanyId,
    string CompanyName,
    string? Location,
    string WorkMode,
    string EmploymentType,
    string Status,
    string? CurrentStageName,
    string? CurrentStageKind,
    string? CurrentStageStatus,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Detailed view of a single candidate application (<c>GET /api/applications/{id}</c>),
/// including the profile snapshot, attached documents, and stage-by-stage progress.
/// </summary>
public sealed record CandidateApplicationDetailResponse(
    Guid Id,
    Guid JobOfferId,
    string JobTitle,
    Guid CompanyId,
    string CompanyName,
    string? Location,
    string WorkMode,
    string EmploymentType,
    string Status,
    string? CoverMessage,
    ApplicationProfileSnapshot ProfileSnapshot,
    IReadOnlyList<ApplicationAttachedDocumentResponse> AttachedDocuments,
    IReadOnlyList<CandidateApplicationStageProgressResponse> Stages,
    Guid? ConversationId,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a document attached to the application (<c>portal.application_documents</c> + <c>portal.candidate_documents</c>).
/// </summary>
public sealed record ApplicationAttachedDocumentResponse(
    Guid DocumentId,
    string Kind,
    string OriginalFilename,
    string MimeType,
    long SizeBytes,
    DateTimeOffset AttachedAt);

/// <summary>
/// Represents the candidate's progress in a specific recruitment round (<c>portal.recruitment_stages</c> + <c>portal.application_stages</c>).
/// </summary>
public sealed record CandidateApplicationStageProgressResponse(
    Guid StageId,
    string Name,
    string Kind,
    int Position,
    string? Description,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt);

    /// <summary>
/// Summary item for a company member listing applications received for the company's job offers.
/// </summary>
public sealed record CompanyApplicationSummaryResponse(
    Guid Id,
    Guid JobOfferId,
    string JobTitle,
    Guid CompanyId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    Guid? AssignedMemberId,
    string Status,
    int AttachedDocumentsCount,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Full detail of a received application for a company HR member, including the candidate's
/// immutable profile snapshot, cover message, and attached document metadata.
/// </summary>
public sealed record CompanyApplicationDetailResponse(
    Guid Id,
    Guid JobOfferId,
    string JobTitle,
    Guid CompanyId,
    Guid CandidateId,
    Guid? AssignedMemberId,
    string Status,
    string? CoverMessage,
    ApplicationProfileSnapshot ProfileSnapshot,
    IReadOnlyList<ApplicationAttachedDocumentResponse> AttachedDocuments,
    IReadOnlyList<CandidateApplicationStageProgressResponse> Stages,
    Guid? ConversationId,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request by a company member to update the status of a received application.
/// Company members can transition applications to <c>in_progress</c>, <c>accepted</c>, or <c>rejected</c>.
/// </summary>
public sealed record UpdateCompanyApplicationStatusRequest(
    [property: System.ComponentModel.DataAnnotations.Required] string Status) : System.ComponentModel.DataAnnotations.IValidatableObject
{
    private static readonly IReadOnlySet<string> AllowedCompanyStatuses = new HashSet<string>
    {
        ApplicationStatuses.Submitted,
        ApplicationStatuses.InProgress,
        ApplicationStatuses.Rejected,
        ApplicationStatuses.Accepted
    };

    public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(
        System.ComponentModel.DataAnnotations.ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Status) || !AllowedCompanyStatuses.Contains(Status.Trim().ToLowerInvariant()))
        {
            yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                $"Invalid status '{Status}'. Allowed company statuses: {string.Join(", ", AllowedCompanyStatuses)}.",
                [nameof(Status)]);
        }
    }
}

/// <summary>
/// Request by a company member to assign or unassign a responsible HR member (<c>assigned_member_id</c>).
/// Pass <c>null</c> to unassign.
/// </summary>
public sealed record AssignApplicationMemberRequest(
    Guid? AssignedMemberId);