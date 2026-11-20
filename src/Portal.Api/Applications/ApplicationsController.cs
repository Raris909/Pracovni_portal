using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Applications;
using Portal.Contracts.Auth;

namespace Portal.Api.Applications;

public sealed record SubmitJobApplicationBodyRequest(
    string? CoverMessage = null,
    IReadOnlyList<Guid>? DocumentIds = null);

public sealed record SubmitApplicationWithJobIdRequest(
    Guid JobOfferId,
    string? CoverMessage = null,
    IReadOnlyList<Guid>? DocumentIds = null);

[ApiController]
[Authorize(Roles = UserRoles.Candidate)]
public sealed class ApplicationsController : ControllerBase
{
    private readonly IApplicationSubmissionService _submissionService;

    public ApplicationsController(IApplicationSubmissionService submissionService)
    {
        _submissionService = submissionService;
    }

    /// <summary>
    /// Submits an application for a specific published job offer (<c>POST /api/jobs/{jobOfferId}/applications</c>).
    /// </summary>
    [HttpPost("api/jobs/{jobOfferId:guid}/applications")]
    public async Task<ActionResult<SubmittedApplicationResult>> SubmitForJobOffer(
        Guid jobOfferId,
        [FromBody] SubmitJobApplicationBodyRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var candidateId))
        {
            return Unauthorized();
        }

        if (jobOfferId == Guid.Empty)
        {
            return BadRequest(new { message = "JobOfferId must be a valid GUID." });
        }

        return await ExecuteSubmissionAsync(
            candidateId,
            jobOfferId,
            request?.CoverMessage,
            request?.DocumentIds,
            cancellationToken);
    }

    /// <summary>
    /// Submits an application (<c>POST /api/applications</c>).
    /// </summary>
    [HttpPost("api/applications")]
    public async Task<ActionResult<SubmittedApplicationResult>> Submit(
        [FromBody] SubmitApplicationWithJobIdRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var candidateId))
        {
            return Unauthorized();
        }

        if (request.JobOfferId == Guid.Empty)
        {
            return BadRequest(new { message = "JobOfferId must be a valid GUID." });
        }

        return await ExecuteSubmissionAsync(
            candidateId,
            request.JobOfferId,
            request.CoverMessage,
            request.DocumentIds,
            cancellationToken);
    }

    private async Task<ActionResult<SubmittedApplicationResult>> ExecuteSubmissionAsync(
        Guid candidateId,
        Guid jobOfferId,
        string? coverMessage,
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _submissionService.SubmitApplicationAsync(
                candidateId,
                jobOfferId,
                coverMessage,
                documentIds,
                cancellationToken);

            return Created($"/api/applications/{result.Id}", result);
        }
        catch (ApplicationSubmissionException ex)
        {
            return ex.Kind switch
            {
                ApplicationSubmissionErrorKind.JobOfferNotFoundOrNotOpen => NotFound(new { message = ex.Message }),
                ApplicationSubmissionErrorKind.AlreadyApplied => Conflict(new { message = ex.Message }),
                ApplicationSubmissionErrorKind.InvalidDocumentSelection => BadRequest(new { message = ex.Message }),
                ApplicationSubmissionErrorKind.CandidateProfileNotFound => NotFound(new { message = ex.Message }),
                _ => BadRequest(new { message = ex.Message })
            };
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}