using Portal.Contracts.Jobs;

namespace Portal.Api.Jobs;

public enum JobUpdateStatus
{
    Success,
    CompanyNotFound,
    OfferNotFound,
    NotDraft
}

public enum JobTransitionStatus
{
    Success,
    CompanyNotFound,
    OfferNotFound,
    InvalidStatus,
    DeadlineExpired
}

public interface IJobOfferRepository
{
    Task<IReadOnlyList<JobOfferResponse>> GetPublishedJobsAsync(int limit, int offset, CancellationToken cancellationToken);
    Task<JobOfferDetailResponse?> GetPublishedJobByIdAsync(Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobOfferManagementResponse>> GetCompanyJobsAsync(Guid companyId, int limit, int offset, CancellationToken cancellationToken);
    Task<JobOfferManagementResponse?> GetCompanyJobByIdAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken);
    Task<(JobOfferManagementResponse? Job, bool CompanyNotFound)> CreateJobAsync(Guid companyId, Guid userId, JobOfferRequest request, CancellationToken cancellationToken);
    Task<(JobOfferManagementResponse? Job, JobUpdateStatus Status)> UpdateJobAsync(Guid companyId, Guid jobId, Guid userId, JobOfferRequest request, CancellationToken cancellationToken);
    Task<(JobOfferManagementResponse? Job, JobTransitionStatus Status)> PublishJobAsync(Guid companyId, Guid jobId, Guid userId, CancellationToken cancellationToken);
    Task<(JobOfferManagementResponse? Job, JobTransitionStatus Status)> CloseJobAsync(Guid companyId, Guid jobId, Guid userId, CancellationToken cancellationToken);
}
