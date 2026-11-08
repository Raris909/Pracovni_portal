using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Portal.Api.Ai;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.CvReviews;

namespace Portal.Api.CvReviews;

[ApiController]
[Authorize(Roles = "company")]
[Route("api/companies/{companyId:guid}/applications/{applicationId:guid}/cv-reviews")]
public class ApplicationCvReviewsController : ControllerBase
{
    private readonly ICvReviewRepository _reviewRepo;
    private readonly IDocumentStorage _storage;
    private readonly ICvAnalysisClient _aiClient;
    private readonly NpgsqlDataSource _dataSource;

    public ApplicationCvReviewsController(ICvReviewRepository reviewRepo, IDocumentStorage storage, ICvAnalysisClient aiClient, NpgsqlDataSource dataSource)
    {
        _reviewRepo = reviewRepo;
        _storage = storage;
        _aiClient = aiClient;
        _dataSource = dataSource;
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationCvReviewResponse>> CreateReview(Guid companyId, Guid applicationId, [FromBody] CreateApplicationCvReviewRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string authSql = "SELECT id FROM portal.company_members WHERE company_id = @CId AND user_id = @UId AND is_active = true";
        await using var authCmd = new NpgsqlCommand(authSql, connection);
        authCmd.Parameters.AddWithValue("CId", companyId);
        authCmd.Parameters.AddWithValue("UId", userId);
        var memberIdObj = await authCmd.ExecuteScalarAsync(cancellationToken);
        if (memberIdObj is null) return Forbid();
        var memberId = (Guid)memberIdObj;

        const string appSql = """
            SELECT a.candidate_id, d.id, d.storage_key, d.mime_type, j.title, j.description, j.requirements, j.location, j.work_mode, j.employment_type
            FROM portal.applications a
            JOIN portal.job_offers j ON j.id = a.job_offer_id
            JOIN portal.application_documents ad ON ad.application_id = a.id
            JOIN portal.candidate_documents d ON d.id = ad.document_id
            WHERE a.id = @AppId AND a.company_id = @CId AND d.kind = 'cv'
            """;
        await using var appCmd = new NpgsqlCommand(appSql, connection);
        appCmd.Parameters.AddWithValue("AppId", applicationId);
        appCmd.Parameters.AddWithValue("CId", companyId);

        Guid candidateId = Guid.Empty;
        Guid documentId = Guid.Empty;
        string storageKey = string.Empty, mimeType = string.Empty;
        JobRequirementsSnapshot reqs = null!;
        bool found = false;

        await using (var reader = await appCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (request.DocumentId.HasValue && request.DocumentId.Value != reader.GetGuid(1)) continue;

                candidateId = reader.GetGuid(0);
                documentId = reader.GetGuid(1);
                storageKey = reader.GetString(2);
                mimeType = reader.GetString(3);
                reqs = new JobRequirementsSnapshot(
                    reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8), reader.GetString(9));
                found = true;
                break;
            }
        }

        if (!found) return NotFound(new { message = "Application or CV document not found." });

        var review = await _reviewRepo.CreateApplicationReviewAsync(companyId, applicationId, candidateId, documentId, memberId, "Gemini", "gemini-1.5-flash", "v1", reqs, cancellationToken);

        try
        {
            await using var stream = await _storage.OpenReadAsync(storageKey, cancellationToken);
            if (stream is null) throw new InvalidOperationException("File missing in storage.");

            var aiResult = await _aiClient.AnalyzeApplicationCvAsync(stream, mimeType, reqs, cancellationToken);
            await _reviewRepo.UpdateApplicationReviewAsync(review.Id, CvReviewStatuses.Succeeded, aiResult.Score, aiResult.Result, null, cancellationToken);
        }
        catch (Exception ex)
        {
            await _reviewRepo.UpdateApplicationReviewAsync(review.Id, CvReviewStatuses.Failed, null, null, ex.Message, cancellationToken);
        }

        return await _reviewRepo.GetApplicationReviewByIdAsync(companyId, applicationId, review.Id, cancellationToken) ?? review;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApplicationCvReviewSummaryResponse>>> GetAll(Guid companyId, Guid applicationId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string authSql = "SELECT 1 FROM portal.company_members WHERE company_id = @CId AND user_id = @UId AND is_active = true";
        await using var authCmd = new NpgsqlCommand(authSql, connection);
        authCmd.Parameters.AddWithValue("CId", companyId);
        authCmd.Parameters.AddWithValue("UId", userId);
        if (await authCmd.ExecuteScalarAsync(cancellationToken) is null) return Forbid();

        return Ok(await _reviewRepo.GetApplicationReviewsAsync(companyId, applicationId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationCvReviewResponse>> Get(Guid companyId, Guid applicationId, Guid id, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string authSql = "SELECT 1 FROM portal.company_members WHERE company_id = @CId AND user_id = @UId AND is_active = true";
        await using var authCmd = new NpgsqlCommand(authSql, connection);
        authCmd.Parameters.AddWithValue("CId", companyId);
        authCmd.Parameters.AddWithValue("UId", userId);
        if (await authCmd.ExecuteScalarAsync(cancellationToken) is null) return Forbid();

        var review = await _reviewRepo.GetApplicationReviewByIdAsync(companyId, applicationId, id, cancellationToken);
        return review is not null ? Ok(review) : NotFound();
    }
}
