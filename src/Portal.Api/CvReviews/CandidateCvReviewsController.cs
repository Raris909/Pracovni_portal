using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Portal.Api.Ai;
using Portal.Api.Profiles;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.CvReviews;
using Portal.Contracts.Profiles;

namespace Portal.Api.CvReviews;

[ApiController]
[Authorize(Roles = "candidate")]
[Route("api/profiles/me/cv-reviews")]
[Route("api/profile/cv/reviews")]
public sealed class CandidateCvReviewsController : ControllerBase
{
    private readonly ICvReviewRepository _reviewRepo;
    private readonly ICandidateDocumentRepository _docRepo;
    private readonly IDocumentStorage _storage;
    private readonly ICvAnalysisClient _aiClient;
    private readonly GeminiOptions _geminiOptions;

    public CandidateCvReviewsController(
        ICvReviewRepository reviewRepo,
        ICandidateDocumentRepository docRepo,
        IDocumentStorage storage,
        ICvAnalysisClient aiClient,
        IOptions<GeminiOptions>? geminiOptions = null)
    {
        _reviewRepo = reviewRepo;
        _docRepo = docRepo;
        _storage = storage;
        _aiClient = aiClient;
        _geminiOptions = geminiOptions?.Value ?? new GeminiOptions();
    }

    [HttpPost]
    public async Task<ActionResult<CandidateCvReviewResponse>> CreateReview(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateCandidateCvReviewRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var docRecord = request?.DocumentId.HasValue == true
            ? await _docRepo.GetDocumentByIdAsync(userId, request.DocumentId.Value, cancellationToken)
            : await _docRepo.GetCurrentCvAsync(userId, cancellationToken);

        if (docRecord is null)
        {
            return NotFound(new { message = "Document not found." });
        }

        if (!string.Equals(docRecord.Kind, CandidateDocumentKinds.Cv, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Selected document is not a CV." });
        }

        var review = await _reviewRepo.CreateCandidateReviewAsync(
            userId,
            docRecord.Id,
            _geminiOptions.Provider,
            _geminiOptions.Model,
            _geminiOptions.PromptVersion,
            cancellationToken);

        try
        {
            await using var stream = await _storage.OpenReadAsync(docRecord.StorageKey, cancellationToken);
            if (stream is null)
            {
                throw new InvalidOperationException("File missing in storage.");
            }

            var aiResult = await _aiClient.AnalyzeCandidateCvAsync(stream, docRecord.MimeType, cancellationToken);
            var clampedScore = Math.Clamp(aiResult.Score, (short)0, (short)100);

            await _reviewRepo.UpdateCandidateReviewAsync(
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
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message) ? "CV analysis failed." : ex.Message;
            await _reviewRepo.UpdateCandidateReviewAsync(
                review.Id,
                CvReviewStatuses.Failed,
                null,
                null,
                errorMessage,
                null,
                cancellationToken);
        }

        var updated = await _reviewRepo.GetCandidateReviewByIdAsync(review.Id, userId, cancellationToken);
        return Ok(updated ?? review);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CandidateCvReviewSummaryResponse>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var reviews = await _reviewRepo.GetCandidateReviewsAsync(userId, cancellationToken);
        return Ok(reviews);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CandidateCvReviewResponse>> Get(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var review = await _reviewRepo.GetCandidateReviewByIdAsync(id, userId, cancellationToken);
        return review is not null ? Ok(review) : NotFound(new { message = "CV review not found." });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}
