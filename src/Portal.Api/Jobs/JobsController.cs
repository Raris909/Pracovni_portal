using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Jobs;

namespace Portal.Api.Jobs;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(IJobOfferRepository jobRepository) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobOfferResponse>>> List(
        CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 100)
            return Problem(statusCode: 400, title: "Offset musí být nezáporný a limit musí být 1 až 100.");

        var jobs = await jobRepository.GetPublishedJobsAsync(limit, offset, cancellationToken);
        return Ok(jobs);
    }

    [AllowAnonymous]
    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobOfferDetailResponse>> Get(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetPublishedJobByIdAsync(jobId, cancellationToken);
        if (job is null)
            return Problem(statusCode: 404, title: "Nabídka nebyla nalezena.");

        return Ok(job);
    }
}
