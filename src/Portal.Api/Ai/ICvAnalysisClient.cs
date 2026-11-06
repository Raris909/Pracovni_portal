using Portal.Contracts.CvReviews;

namespace Portal.Api.Ai;

public sealed record AiMetadata(
    string Provider,
    string Model,
    string PromptVersion);

public sealed record AiCandidateCvAnalysisOutput(
    short Score,
    CandidateCvReviewResultPayload Result,
    AiMetadata Metadata);

public sealed record AiApplicationCvAnalysisOutput(
    short Score,
    ApplicationCvReviewResultPayload Result,
    AiMetadata Metadata);

public interface ICvAnalysisClient
{
    /// <summary>
    /// Evaluates a candidate's CV and returns general feedback and a 0-100 score.
    /// </summary>
    Task<AiCandidateCvAnalysisOutput> AnalyzeCandidateCvAsync(
        Stream documentStream,
        string mimeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a candidate's CV against a specific job offer's requirements snapshot.
    /// </summary>
    Task<AiApplicationCvAnalysisOutput> AnalyzeApplicationCvAsync(
        Stream documentStream,
        string mimeType,
        JobRequirementsSnapshot jobRequirements,
        CancellationToken cancellationToken = default);
}