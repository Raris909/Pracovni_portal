using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public enum ApplicationStageStatus
{
    Success,
    Created,
    ApplicationNotFound,
    StageNotFound,
    ApplicationClosed,
    InvalidTransition
}

public interface IApplicationStageRepository
{
    Task<IReadOnlyList<ApplicationStageResponse>?> ListAsync(
        Guid companyId, Guid applicationId, Guid userId, CancellationToken cancellationToken);

    Task<(ApplicationStageResponse? Stage, ApplicationStageStatus Status)> AssignAsync(
        Guid companyId, Guid applicationId, Guid userId, Guid stageId, CancellationToken cancellationToken);

    Task<(ApplicationStageResponse? Stage, ApplicationStageStatus Status)> UpdateStatusAsync(
        Guid companyId, Guid applicationId, Guid userId, Guid stageId, UpdateApplicationStageStatusRequest request,
        CancellationToken cancellationToken);
}
