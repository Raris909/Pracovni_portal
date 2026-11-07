using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public enum RecruitmentStageStatus
{
    Success,
    JobNotFound,
    StageNotFound,
    PositionConflict,
    StageInUse,
    InvalidOrder
}

public interface IRecruitmentStageRepository
{
    Task<IReadOnlyList<RecruitmentStageResponse>?> ListAsync(
        Guid companyId, Guid jobId, Guid userId, CancellationToken cancellationToken);

    Task<(RecruitmentStageResponse? Stage, RecruitmentStageStatus Status)> CreateAsync(
        Guid companyId, Guid jobId, Guid userId, UpsertRecruitmentStageRequest request,
        CancellationToken cancellationToken);

    Task<(RecruitmentStageResponse? Stage, RecruitmentStageStatus Status)> UpdateAsync(
        Guid companyId, Guid jobId, Guid userId, Guid stageId, UpsertRecruitmentStageRequest request,
        CancellationToken cancellationToken);

    Task<RecruitmentStageStatus> DeleteAsync(
        Guid companyId, Guid jobId, Guid userId, Guid stageId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<RecruitmentStageResponse>? Stages, RecruitmentStageStatus Status)> ReorderAsync(
        Guid companyId, Guid jobId, Guid userId, ReorderRecruitmentStagesRequest request,
        CancellationToken cancellationToken);
}
