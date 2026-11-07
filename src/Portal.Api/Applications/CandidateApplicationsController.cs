using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Applications;

namespace Portal.Api.Applications;

[ApiController]
[Authorize(Roles = "candidate")]
[Route("api/applications")]
public sealed class CandidateApplicationsController : ControllerBase
{
    private readonly ICandidateApplicationRepository _repository;

    public CandidateApplicationsController(ICandidateApplicationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Lists all job applications submitted by the currently authenticated candidate,
    /// including their current status and active recruitment round.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CandidateApplicationSummaryResponse>>> GetMyApplications(
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var candidateId))
        {
            return Unauthorized();
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = status.Trim().ToLowerInvariant();
            if (!ApplicationStatuses.All.Contains(normalizedStatus))
            {
                return BadRequest(new
                {
                    message = $"Invalid status filter '{status}'. Allowed values: {string.Join(", ", ApplicationStatuses.All)}."
                });
            }
        }

        var applications = await _repository.GetCandidateApplicationsAsync(
            candidateId,
            normalizedStatus,
            cancellationToken);

        return Ok(applications);
    }

    /// <summary>
    /// Returns full details of a specific application belonging to the current candidate,
    /// including its status, submitted profile snapshot, attached documents, and recruitment stage progress.
    /// </summary>
    [HttpGet("{applicationId:guid}")]
    public async Task<ActionResult<CandidateApplicationDetailResponse>> GetMyApplicationById(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var candidateId))
        {
            return Unauthorized();
        }

        var application = await _repository.GetCandidateApplicationByIdAsync(
            candidateId,
            applicationId,
            cancellationToken);

        if (application is null)
        {
            return NotFound(new { message = "Application not found." });
        }

        return Ok(application);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}