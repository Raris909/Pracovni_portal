using Portal.Contracts.Applications;

namespace Portal.Api.Applications;

public sealed record SubmittedApplicationResult(
    Guid Id,
    Guid JobOfferId,
    Guid CompanyId,
    Guid CandidateId,
    string Status,
    string? CoverMessage,
    ApplicationProfileSnapshot ProfileSnapshot,
    IReadOnlyList<Guid> AttachedDocumentIds,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

public interface IApplicationSubmissionService
{
    Task<SubmittedApplicationResult> SubmitApplicationAsync(
        Guid candidateId,
        Guid jobOfferId,
        string? coverMessage,
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken = default);
}