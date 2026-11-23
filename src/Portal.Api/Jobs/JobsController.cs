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
        CancellationToken cancellationToken,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        [FromQuery] string? q = null,
        [FromQuery] string? workMode = null,
        [FromQuery] string? employmentType = null,
        [FromQuery] string? location = null,
        [FromQuery] decimal? salaryMin = null,
        [FromQuery] string? sort = null)
    {
        if (offset < 0 || limit is < 1 or > 100)
            return Problem(statusCode: 400, title: "Offset musí být nezáporný a limit musí být 1 až 100.");

        string? normalizedWorkMode = null;
        if (!string.IsNullOrWhiteSpace(workMode))
        {
            normalizedWorkMode = workMode.Trim().ToLowerInvariant();
            if (!WorkModes.All.Contains(normalizedWorkMode))
                return Problem(statusCode: 400, title: "Neplatný režim práce.");
        }

        string? normalizedEmploymentType = null;
        if (!string.IsNullOrWhiteSpace(employmentType))
        {
            normalizedEmploymentType = employmentType.Trim().ToLowerInvariant();
            if (!EmploymentTypes.All.Contains(normalizedEmploymentType))
                return Problem(statusCode: 400, title: "Neplatný typ úvazku.");
        }

        if (salaryMin is < 0)
            return Problem(statusCode: 400, title: "Minimální mzda nesmí být záporná.");

        var normalizedSort = JobOfferSortOptions.Newest;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            normalizedSort = sort.Trim().ToLowerInvariant();
            if (!JobOfferSortOptions.All.Contains(normalizedSort))
                return Problem(statusCode: 400, title: "Neplatný způsob řazení.");
        }

        var searchQuery = new JobOfferSearchQuery(
            Limit: limit,
            Offset: offset,
            Query: string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            WorkMode: normalizedWorkMode,
            EmploymentType: normalizedEmploymentType,
            Location: string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            SalaryMin: salaryMin,
            Sort: normalizedSort);

        var jobs = await jobRepository.GetPublishedJobsAsync(searchQuery, cancellationToken);
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
