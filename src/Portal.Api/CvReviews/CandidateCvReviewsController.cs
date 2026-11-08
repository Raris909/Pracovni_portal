using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Api.Ai;
using Portal.Api.Profiles;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.CvReviews;

namespace Portal.Api.CvReviews;

[ApiController]
[Authorize(Roles = "candidate")]
[Route("api/profiles/me/cv-reviews")]
public class CandidateCvReviewsController : ControllerBase
{
    private readonly ICvReviewRepository _reviewRepo;
    private readonly ICandidateDocumentRepository _docRepo;
    private readonly IDocumentStorage _storage;
    private readonly ICvAnalysisClient _aiClient;

    public CandidateCvReviewsController(ICvReviewRepository reviewRepo, ICandidateDocumentRepository docRepo, IDocumentStorage storage, ICvAnalysisClient aiClient)
    {
        _reviewRepo = reviewRepo;
        _docRepo = docRepo;
        _storage = storage;
        _aiClient = aiClient;
    }

    [HttpPost]
    public async Task<ActionResult<CandidateCvReviewResponse>> CreateReview([FromBody] CreateCandidateCvReviewRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var docRecord = request.DocumentId.HasValue
            ? await _docRepo.GetDocumentByIdAsync(userId, request.DocumentId.Value, cancellationToken)
            : await _docRepo.GetCurrentCvAsync(userId, cancellationToken);

        if (docRecord is null)
            return NotFound(new { message = "Document not found." });

        var review = await _reviewRepo.CreateCandidateReviewAsync(userId, docRecord.Id, "Gemini", "gemini-1.5-flash", "v1", cancellationToken);

        try
        {
            await using var stream = await _storage.OpenReadAsync(docRecord.StorageKey, cancellationToken);
            if (stream is null)
                throw new InvalidOperationException("File missing in storage.");

            var aiResult = await _aiClient.AnalyzeCandidateCvAsync(stream, docRecord.MimeType, cancellationToken);
            await _reviewRepo.UpdateCandidateReviewAsync(review.Id, CvReviewStatuses.Succeeded, aiResult.Score, aiResult.Result, null, cancellationToken);
        }
        catch (Exception ex)
        {
            await _reviewRepo.UpdateCandidateReviewAsync(review.Id, CvReviewStatuses.Failed, null, null, ex.Message, cancellationToken);
        }

        return await _reviewRepo.GetCandidateReviewByIdAsync(review.Id, userId, cancellationToken) ?? review;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CandidateCvReviewSummaryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        return Ok(await _reviewRepo.GetCandidateReviewsAsync(userId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CandidateCvReviewResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var review = await _reviewRepo.GetCandidateReviewByIdAsync(id, userId, cancellationToken);
        return review is not null ? Ok(review) : NotFound();
    }
}
