using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Portal.Api.Ai;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.CvReviews;

namespace Portal.Api.CvReviews;

[ApiController]
[Authorize(Roles = "company")]
[Route("api/companies/{companyId:guid}/applications/{applicationId:guid}/cv-reviews")]
public sealed class ApplicationCvReviewsController : ControllerBase
{
    private readonly ICvReviewRepository _reviewRepo;
    private readonly IDocumentStorage _storage;
    private readonly ICvAnalysisClient _aiClient;
    private readonly GeminiOptions _geminiOptions;

    public ApplicationCvReviewsController(
        ICvReviewRepository reviewRepo,
        IDocumentStorage storage,
        ICvAnalysisClient aiClient,
        IOptions<GeminiOptions>? geminiOptions = null)
    {
        _reviewRepo = reviewRepo;
        _storage = storage;
        _aiClient = aiClient;
        _geminiOptions = geminiOptions?.Value ?? new GeminiOptions();
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationCvReviewResponse>> CreateReview(
        Guid companyId,
        Guid applicationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateApplicationCvReviewRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var memberId = await _reviewRepo.GetActiveCompanyMemberIdAsync(companyId, userId, cancellationToken);
        if (memberId is null)
        {
            return Forbid();
        }

        var context = await _reviewRepo.GetApplicationCvContextAsync(
            companyId,
            applicationId,
            request?.DocumentId,
            cancellationToken);

        if (context is null)
        {
            return NotFound(new { message = "Application or attached CV document not found." });
        }

        var review = await _reviewRepo.CreateApplicationReviewAsync(
            companyId,
            applicationId,
            context.CandidateId,
            context.DocumentId,
            memberId.Value,
            _geminiOptions.Provider,
            _geminiOptions.Model,
            _geminiOptions.PromptVersion,
            context.JobRequirements,
            cancellationToken);

        try
        {
            await using var stream = await _storage.OpenReadAsync(context.StorageKey, cancellationToken);
            if (stream is null)
            {
                throw new InvalidOperationException("File missing in storage.");
            }

            var aiResult = await _aiClient.AnalyzeApplicationCvAsync(
                stream,
                context.MimeType,
                context.JobRequirements,
                cancellationToken);

            var clampedScore = Math.Clamp(aiResult.Score, (short)0, (short)100);

            await _reviewRepo.UpdateApplicationReviewAsync(
                review.Id,
                CvReviewStatuses.Succeeded,
                clampedScore,
                aiResult.Result,
                null,
                aiResult.Metadata,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message) ? "Application CV analysis failed." : ex.Message;
            await _reviewRepo.UpdateApplicationReviewAsync(
                review.Id,
                CvReviewStatuses.Failed,
                null,
                null,
                errorMessage,
                null,
                cancellationToken);
        }

        var updated = await _reviewRepo.GetApplicationReviewByIdAsync(companyId, applicationId, review.Id, cancellationToken);
        return Ok(updated ?? review);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApplicationCvReviewSummaryResponse>>> GetAll(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var memberId = await _reviewRepo.GetActiveCompanyMemberIdAsync(companyId, userId, cancellationToken);
        if (memberId is null)
        {
            return Forbid();
        }

        var reviews = await _reviewRepo.GetApplicationReviewsAsync(companyId, applicationId, cancellationToken);
        return Ok(reviews);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationCvReviewResponse>> Get(
        Guid companyId,
        Guid applicationId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var memberId = await _reviewRepo.GetActiveCompanyMemberIdAsync(companyId, userId, cancellationToken);
        if (memberId is null)
        {
            return Forbid();
        }

        var review = await _reviewRepo.GetApplicationReviewByIdAsync(companyId, applicationId, id, cancellationToken);
        return review is not null ? Ok(review) : NotFound(new { message = "Application CV review not found." });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}
