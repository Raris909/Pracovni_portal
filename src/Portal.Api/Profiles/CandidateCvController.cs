using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

[ApiController]
[Authorize(Roles = "candidate")]
[Route("api/profile/cv")]
public sealed class CandidateCvController : ControllerBase
{
    private readonly CandidateCvService _cvService;

    public CandidateCvController(CandidateCvService cvService)
    {
        _cvService = cvService;
    }

    /// <summary>
    /// Uploads a new CV file (.pdf or .docx), saves it in private storage, records its metadata,
    /// and optionally sets it as the user's current CV.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<CandidateDocumentResponse>> UploadCv(
        IFormFile? file,
        [FromQuery] bool setAsCurrent = true,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (file is null)
        {
            return BadRequest(new { message = "File is required." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var response = await _cvService.UploadCvAsync(
                userId,
                file.FileName,
                file.Length,
                stream,
                setAsCurrent,
                cancellationToken);

            return CreatedAtAction(nameof(GetCurrentCvMetadata), response);
        }
        catch (CvValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Lists all documents uploaded by the current candidate.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<CandidateDocumentResponse>>> GetCvHistory(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var documents = await _cvService.GetDocumentsAsync(userId, cancellationToken);
        return Ok(documents);
    }

    /// <summary>
    /// Returns metadata for the candidate's currently selected CV.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CandidateDocumentResponse>> GetCurrentCvMetadata(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var metadata = await _cvService.GetCurrentCvMetadataAsync(userId, cancellationToken);
        if (metadata is null)
        {
            return NotFound(new { message = "No current CV is selected." });
        }

        return Ok(metadata);
    }

    /// <summary>
    /// Downloads the binary file of the candidate's currently selected CV.
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> DownloadCurrentCv(CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _cvService.DownloadCurrentCvAsync(userId, cancellationToken);
        if (result is null)
        {
            return NotFound(new { message = "Current CV file not found." });
        }

        return File(result.Value.Stream, result.Value.Metadata.MimeType, result.Value.Metadata.OriginalFilename);
    }

    /// <summary>
    /// Downloads a specific CV version by its document ID.
    /// </summary>
    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> DownloadCvById(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _cvService.DownloadDocumentByIdAsync(userId, documentId, cancellationToken);
        if (result is null)
        {
            return NotFound(new { message = "CV document not found." });
        }

        return File(result.Value.Stream, result.Value.Metadata.MimeType, result.Value.Metadata.OriginalFilename);
    }

    /// <summary>
    /// Selects an existing uploaded CV document as the candidate's current active CV.
    /// </summary>
    [HttpPut("current")]
    public async Task<IActionResult> SetCurrentCv(
        [FromBody] SetCurrentCvRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var updated = await _cvService.SetCurrentCvAsync(userId, request.DocumentId, cancellationToken);
        if (!updated)
        {
            return NotFound(new { message = "CV document not found or does not belong to the current user." });
        }

        return NoContent();
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}