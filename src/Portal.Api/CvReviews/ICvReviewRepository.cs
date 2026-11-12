using Portal.Api.Ai;
using Portal.Contracts.CvReviews;

namespace Portal.Api.CvReviews;

public sealed record ApplicationCvDocumentContext(
    Guid CandidateId,
    Guid DocumentId,
    string StorageKey,
    string MimeType,
    JobRequirementsSnapshot JobRequirements);

public interface ICvReviewRepository
{
    Task<CandidateCvReviewResponse> CreateCandidateReviewAsync(
        Guid candidateId,
        Guid documentId,
        string provider,
        string model,
        string promptVersion,
        CancellationToken cancellationToken = default);

    Task UpdateCandidateReviewAsync(
        Guid reviewId,
        string status,
        short? score,
        CandidateCvReviewResultPayload? result,
        string? errorMessage,
        AiMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    Task<CandidateCvReviewResponse?> GetCandidateReviewByIdAsync(
        Guid reviewId,
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CandidateCvReviewSummaryResponse>> GetCandidateReviewsAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetActiveCompanyMemberIdAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ApplicationCvDocumentContext?> GetApplicationCvContextAsync(
        Guid companyId,
        Guid applicationId,
        Guid? documentId,
        CancellationToken cancellationToken = default);

    Task<ApplicationCvReviewResponse> CreateApplicationReviewAsync(
        Guid companyId,
        Guid applicationId,
        Guid candidateId,
        Guid documentId,
        Guid requestedByMemberId,
        string provider,
        string model,
        string promptVersion,
        JobRequirementsSnapshot jobRequirementsSnapshot,
        CancellationToken cancellationToken = default);

    Task UpdateApplicationReviewAsync(
        Guid reviewId,
        string status,
        short? score,
        ApplicationCvReviewResultPayload? result,
        string? errorMessage,
        AiMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    Task<ApplicationCvReviewResponse?> GetApplicationReviewByIdAsync(
        Guid companyId,
        Guid applicationId,
        Guid reviewId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationCvReviewSummaryResponse>> GetApplicationReviewsAsync(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default);
}
