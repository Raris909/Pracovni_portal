using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portal.Api.Ai;
using Portal.Api.CvReviews;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.CvReviews;
using Xunit;

namespace Portal.Api.Tests;

public sealed class ApplicationCvReviewsControllerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _memberId = Guid.NewGuid();
    private readonly Guid _candidateId = Guid.NewGuid();
    private readonly Guid _documentId = Guid.NewGuid();

    [Fact]
    public async Task CreateReview_WhenAuthorizedAndContextFound_ReturnsSucceededApplicationReview()
    {
        var snapshot = new JobRequirementsSnapshot(
            "Senior .NET Developer",
            "Develop backend services",
            "C#, ASP.NET Core, PostgreSQL",
            "Prague",
            "hybrid",
            "full_time");

        var context = new ApplicationCvDocumentContext(
            _candidateId,
            _documentId,
            "candidates/1/cv.pdf",
            "application/pdf",
            snapshot);

        var repo = new InMemoryApplicationReviewRepo(_memberId, context);
        var storage = new StubStorage(() => new MemoryStream("%PDF-1.7"u8.ToArray()));
        var aiClient = new StubApplicationAiClient();

        var controller = CreateController(repo, storage, aiClient, _userId);

        var result = await controller.CreateReview(_companyId, _applicationId, new CreateApplicationCvReviewRequest(null));

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApplicationCvReviewResponse>(okResult.Value);
        Assert.Equal(CvReviewStatuses.Succeeded, response.Status);
        Assert.Equal((short)91, response.Score);
        Assert.Equal("Senior .NET Developer", response.JobRequirementsSnapshot.Title);
        Assert.NotNull(response.Result);
        Assert.Contains("C#", response.Result!.MatchingSkills);
    }

    [Fact]
    public async Task CreateReview_WhenUserNotCompanyMember_ReturnsForbid()
    {
        var repo = new InMemoryApplicationReviewRepo(null, null);
        var storage = new StubStorage(() => null);
        var aiClient = new StubApplicationAiClient();

        var controller = CreateController(repo, storage, aiClient, _userId);

        var result = await controller.CreateReview(_companyId, _applicationId, null);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task CreateReview_WhenApplicationOrCvNotFound_ReturnsNotFound()
    {
        var repo = new InMemoryApplicationReviewRepo(_memberId, null);
        var storage = new StubStorage(() => null);
        var aiClient = new StubApplicationAiClient();

        var controller = CreateController(repo, storage, aiClient, _userId);

        var result = await controller.CreateReview(_companyId, _applicationId, null);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    private static ApplicationCvReviewsController CreateController(
        ICvReviewRepository repo,
        IDocumentStorage storage,
        ICvAnalysisClient aiClient,
        Guid userId)
    {
        var controller = new ApplicationCvReviewsController(repo, storage, aiClient);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("sub", userId.ToString()),
                    new Claim(ClaimTypes.Role, "company")
                ], "TestAuth"))
            }
        };
        return controller;
    }

    private sealed class StubStorage(Func<Stream?> factory) : IDocumentStorage
    {
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(factory());

        public Task<StoredFileResult> SaveAsync(Guid candidateId, Stream content, string extension, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubApplicationAiClient : ICvAnalysisClient
    {
        public Task<AiCandidateCvAnalysisOutput> AnalyzeCandidateCvAsync(Stream documentStream, string mimeType, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AiApplicationCvAnalysisOutput> AnalyzeApplicationCvAsync(Stream documentStream, string mimeType, JobRequirementsSnapshot jobRequirements, CancellationToken cancellationToken = default)
        {
            var payload = new ApplicationCvReviewResultPayload(
                "Strong candidate for role",
                ["C#", "PostgreSQL"],
                [],
                ["Solid experience"],
                [],
                ["How do you optimize SQL queries?"],
                "Proceed to technical interview");

            return Task.FromResult(new AiApplicationCvAnalysisOutput(91, payload, new AiMetadata("Gemini", "gemini-3.8-flash", "v1")));
        }
    }

    private sealed class InMemoryApplicationReviewRepo(
        Guid? memberId,
        ApplicationCvDocumentContext? context) : ICvReviewRepository
    {
        private ApplicationCvReviewResponse? _stored;

        public Task<Guid?> GetActiveCompanyMemberIdAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(memberId);

        public Task<ApplicationCvDocumentContext?> GetApplicationCvContextAsync(Guid companyId, Guid applicationId, Guid? documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(context);

        public Task<ApplicationCvReviewResponse> CreateApplicationReviewAsync(Guid companyId, Guid applicationId, Guid candidateId, Guid documentId, Guid requestedByMemberId, string provider, string model, string promptVersion, JobRequirementsSnapshot jobRequirementsSnapshot, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            _stored = new ApplicationCvReviewResponse(
                Guid.NewGuid(),
                applicationId,
                companyId,
                candidateId,
                documentId,
                "cv",
                requestedByMemberId,
                CvReviewStatuses.Queued,
                provider,
                model,
                promptVersion,
                jobRequirementsSnapshot,
                null,
                null,
                null,
                now,
                null,
                now);
            return Task.FromResult(_stored);
        }

        public Task UpdateApplicationReviewAsync(Guid reviewId, string status, short? score, ApplicationCvReviewResultPayload? result, string? errorMessage, AiMetadata? metadata = null, CancellationToken cancellationToken = default)
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

        public Task<ApplicationCvReviewResponse?> GetApplicationReviewByIdAsync(Guid companyId, Guid applicationId, Guid reviewId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored is not null && _stored.Id == reviewId ? _stored : null);

        public Task<IReadOnlyList<ApplicationCvReviewSummaryResponse>> GetApplicationReviewsAsync(Guid companyId, Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationCvReviewSummaryResponse>>(_stored is not null
                ? [new ApplicationCvReviewSummaryResponse(_stored.Id, _stored.ApplicationId, _stored.DocumentId, _stored.RequestedByMemberId, _stored.Status, _stored.Score, _stored.CreatedAt, _stored.CompletedAt)]
                : []);

        public Task<CandidateCvReviewResponse> CreateCandidateReviewAsync(Guid candidateId, Guid documentId, string provider, string model, string promptVersion, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task UpdateCandidateReviewAsync(Guid reviewId, string status, short? score, CandidateCvReviewResultPayload? result, string? errorMessage, AiMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<CandidateCvReviewResponse?> GetCandidateReviewByIdAsync(Guid reviewId, Guid candidateId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<CandidateCvReviewSummaryResponse>> GetCandidateReviewsAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
