using Portal.Contracts.CvReviews;

namespace Portal.Api.CvReviews;

public interface ICvReviewRepository
{
    // Candidate CV self-reviews (portal.candidate_cv_reviews)
    Task<CandidateCvReviewResponse> CreateCandidateReviewAsync(
        Guid candidateId,
        Guid documentId,
        string provider,
        string model,
        string promptVersion,
        CancellationToken cancellationToken = default);

    Task<CandidateCvReviewResponse?> GetCandidateReviewByIdAsync(
        Guid reviewId,
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CandidateCvReviewSummaryResponse>> GetCandidateReviewsAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    // Company Application CV reviews (portal.application_cv_reviews)
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