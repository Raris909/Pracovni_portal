using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portal.Api.Ai;
using Portal.Api.CvReviews;
using Portal.Api.Profiles;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.CvReviews;
using Portal.Contracts.Profiles;
using Xunit;

namespace Portal.Api.Tests;

public sealed class CandidateCvReviewsControllerTests
{
    private readonly Guid _candidateId = Guid.NewGuid();
    private readonly Guid _documentId = Guid.NewGuid();

    [Fact]
    public async Task CreateReview_WhenCurrentCvExists_ReturnsSucceededReview()
    {
        var repo = new InMemoryCvReviewRepo();
        var docRepo = new StubCandidateDocRepo(new CandidateDocumentRecord(
            _documentId,
            _candidateId,
            CandidateDocumentKinds.Cv,
            "cv.pdf",
            "candidates/test/cv.pdf",
            "application/pdf",
            1024,
            new string('a', 64),
            true,
            DateTimeOffset.UtcNow));

        var storage = new StubDocumentStorage(() => new MemoryStream("%PDF-1.7"u8.ToArray()));
        var aiClient = new StubAiClient();

        var controller = CreateController(repo, docRepo, storage, aiClient, _candidateId);

        var result = await controller.CreateReview(new CreateCandidateCvReviewRequest(null));

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CandidateCvReviewResponse>(okResult.Value);
        Assert.Equal(CvReviewStatuses.Succeeded, response.Status);
        Assert.Equal((short)85, response.Score);
        Assert.NotNull(response.Result);
        Assert.Equal("Good CV", response.Result!.Summary);
    }

    [Fact]
    public async Task CreateReview_WhenDocumentMissing_ReturnsNotFound()
    {
        var repo = new InMemoryCvReviewRepo();
        var docRepo = new StubCandidateDocRepo(null);
        var storage = new StubDocumentStorage(() => null);
        var aiClient = new StubAiClient();

        var controller = CreateController(repo, docRepo, storage, aiClient, _candidateId);

        var result = await controller.CreateReview(new CreateCandidateCvReviewRequest(Guid.NewGuid()));

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateReview_WhenAiThrows_SavesFailedReview()
    {
        var repo = new InMemoryCvReviewRepo();
        var docRepo = new StubCandidateDocRepo(new CandidateDocumentRecord(
            _documentId,
            _candidateId,
            CandidateDocumentKinds.Cv,
            "cv.pdf",
            "candidates/test/cv.pdf",
            "application/pdf",
            1024,
            new string('a', 64),
            true,
            DateTimeOffset.UtcNow));

        var storage = new StubDocumentStorage(() => new MemoryStream("%PDF-1.7"u8.ToArray()));
        var aiClient = new StubAiClient(shouldThrow: true);

        var controller = CreateController(repo, docRepo, storage, aiClient, _candidateId);

        var result = await controller.CreateReview(null);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CandidateCvReviewResponse>(okResult.Value);
        Assert.Equal(CvReviewStatuses.Failed, response.Status);
        Assert.Null(response.Score);
        Assert.Equal("AI service unavailable", response.ErrorMessage);
    }

    private static CandidateCvReviewsController CreateController(
        ICvReviewRepository repo,
        ICandidateDocumentRepository docRepo,
        IDocumentStorage storage,
        ICvAnalysisClient aiClient,
        Guid userId)
    {
        var controller = new CandidateCvReviewsController(repo, docRepo, storage, aiClient);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("sub", userId.ToString()),
                    new Claim(ClaimTypes.Role, "candidate")
                ], "TestAuth"))
            }
        };
        return controller;
    }

    private sealed class StubCandidateDocRepo(CandidateDocumentRecord? record) : ICandidateDocumentRepository
    {
        public Task<CandidateDocumentRecord?> GetCurrentCvAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(record);

        public Task<CandidateDocumentRecord?> GetDocumentByIdAsync(Guid candidateId, Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(record);

        public Task<IReadOnlyList<CandidateDocumentResponse>> GetCandidateDocumentsAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<CandidateDocumentRecord> InsertDocumentAsync(Guid candidateId, string kind, string originalFilename, string storageKey, string mimeType, long sizeBytes, string sha256, bool setAsCurrentCv, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> SetCurrentCvAsync(Guid candidateId, Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubDocumentStorage(Func<Stream?> streamFactory) : IDocumentStorage
    {
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(streamFactory());

        public Task<StoredFileResult> SaveAsync(Guid candidateId, Stream content, string extension, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubAiClient(bool shouldThrow = false) : ICvAnalysisClient
    {
        public Task<AiCandidateCvAnalysisOutput> AnalyzeCandidateCvAsync(Stream documentStream, string mimeType, CancellationToken cancellationToken = default)
        {
            if (shouldThrow)
            {
                throw new InvalidOperationException("AI service unavailable");
            }

            var payload = new CandidateCvReviewResultPayload("Good CV", ["C#"], ["Add links"], ["C#"]);
            return Task.FromResult(new AiCandidateCvAnalysisOutput(85, payload, new AiMetadata("Gemini", "gemini-3.8-flash", "v1")));
        }

        public Task<AiApplicationCvAnalysisOutput> AnalyzeApplicationCvAsync(Stream documentStream, string mimeType, JobRequirementsSnapshot jobRequirements, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class InMemoryCvReviewRepo : ICvReviewRepository
    {
        private CandidateCvReviewResponse? _stored;

        public Task<CandidateCvReviewResponse> CreateCandidateReviewAsync(Guid candidateId, Guid documentId, string provider, string model, string promptVersion, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            _stored = new CandidateCvReviewResponse(Guid.NewGuid(), candidateId, documentId, "cv", CvReviewStatuses.Queued, provider, model, promptVersion, null, null, null, now, null, now);
            return Task.FromResult(_stored);
        }

        public Task UpdateCandidateReviewAsync(Guid reviewId, string status, short? score, CandidateCvReviewResultPayload? result, string? errorMessage, AiMetadata? metadata = null, CancellationToken cancellationToken = default)
        {
            if (_stored is not null && _stored.Id == reviewId)
            {
                var now = DateTimeOffset.UtcNow;
                _stored = _stored with
                {
                    Status = status,
                    Score = score,
                    Result = result,
                    ErrorMessage = errorMessage,
                    Provider = metadata?.Provider ?? _stored.Provider,
                    Model = metadata?.Model ?? _stored.Model,
                    PromptVersion = metadata?.PromptVersion ?? _stored.PromptVersion,
                    CompletedAt = status is CvReviewStatuses.Succeeded or CvReviewStatuses.Failed ? now : null,
                    UpdatedAt = now
                };
            }

            return Task.CompletedTask;
        }

        public Task<CandidateCvReviewResponse?> GetCandidateReviewByIdAsync(Guid reviewId, Guid candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored is not null && _stored.Id == reviewId && _stored.CandidateId == candidateId ? _stored : null);

        public Task<IReadOnlyList<CandidateCvReviewSummaryResponse>> GetCandidateReviewsAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CandidateCvReviewSummaryResponse>>(_stored is not null
                ? [new CandidateCvReviewSummaryResponse(_stored.Id, _stored.DocumentId, _stored.Status, _stored.Score, _stored.CreatedAt, _stored.CompletedAt)]
                : []);

        public Task<Guid?> GetActiveCompanyMemberIdAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApplicationCvDocumentContext?> GetApplicationCvContextAsync(Guid companyId, Guid applicationId, Guid? documentId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApplicationCvReviewResponse> CreateApplicationReviewAsync(Guid companyId, Guid applicationId, Guid candidateId, Guid documentId, Guid requestedByMemberId, string provider, string model, string promptVersion, JobRequirementsSnapshot jobRequirementsSnapshot, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task UpdateApplicationReviewAsync(Guid reviewId, string status, short? score, ApplicationCvReviewResultPayload? result, string? errorMessage, AiMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApplicationCvReviewResponse?> GetApplicationReviewByIdAsync(Guid companyId, Guid applicationId, Guid reviewId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ApplicationCvReviewSummaryResponse>> GetApplicationReviewsAsync(Guid companyId, Guid applicationId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
