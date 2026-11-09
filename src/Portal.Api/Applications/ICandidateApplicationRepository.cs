using Portal.Contracts.Applications;

namespace Portal.Api.Applications;

public interface ICandidateApplicationRepository
{
    Task<IReadOnlyList<CandidateApplicationSummaryResponse>> GetCandidateApplicationsAsync(
        Guid candidateId,
        string? statusFilter = null,
        CancellationToken cancellationToken = default);

    Task<CandidateApplicationDetailResponse?> GetCandidateApplicationByIdAsync(
        Guid candidateId,
        Guid applicationId,
        CancellationToken cancellationToken = default);
}