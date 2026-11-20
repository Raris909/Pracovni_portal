using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public enum CandidateEvaluationStatus
{
    Success,
    StageNotFound,
    EvaluationNotFound,
    Forbidden,
    ApplicationClosed
}

public interface ICandidateEvaluationRepository
{
    Task<IReadOnlyList<CandidateEvaluationResponse>?> ListAsync(
        Guid companyId, Guid applicationId, Guid stageId, Guid userId, CancellationToken cancellationToken);

    Task<(CandidateEvaluationResponse? Evaluation, CandidateEvaluationStatus Status)> CreateAsync(
        Guid companyId, Guid applicationId, Guid stageId, Guid userId, UpsertCandidateEvaluationRequest request,
        CancellationToken cancellationToken);

    Task<(CandidateEvaluationResponse? Evaluation, CandidateEvaluationStatus Status)> UpdateAsync(
        Guid companyId, Guid applicationId, Guid stageId, Guid userId, Guid evaluationId,
        UpsertCandidateEvaluationRequest request, CancellationToken cancellationToken);
}
