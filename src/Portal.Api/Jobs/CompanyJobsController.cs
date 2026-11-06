using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Api.Companies;
using Portal.Contracts.Auth;
using Portal.Contracts.Jobs;

namespace Portal.Api.Jobs;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/jobs")]
public sealed class CompanyJobsController(
    IJobOfferRepository jobRepository, CompanyMembershipService memberships) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobOfferManagementResponse>>> List(
        Guid companyId, CancellationToken cancellationToken,
        [FromQuery] int offset = 0, [FromQuery] int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 100)
            return Problem(statusCode: 400, title: "Offset musí být nezáporný a limit musí být 1 až 100.");

        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        if (await memberships.FindAsync(userId, companyId, cancellationToken) is null)
            return Problem(statusCode: 404, title: "Firma nebyla nalezena.");

        var jobs = await jobRepository.GetCompanyJobsAsync(companyId, limit, offset, cancellationToken);
        return Ok(jobs);
    }

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobOfferManagementResponse>> Get(
        Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        if (await memberships.FindAsync(userId, companyId, cancellationToken) is null)
            return Problem(statusCode: 404, title: "Firma nebyla nalezena.");

        var job = await jobRepository.GetCompanyJobByIdAsync(companyId, jobId, cancellationToken);
        if (job is null)
            return Problem(statusCode: 404, title: "Nabídka nebyla nalezena.");

        return Ok(job);
    }

    [HttpPost]
    public async Task<ActionResult<JobOfferManagementResponse>> Create(
        Guid companyId, JobOfferRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);

        var (job, companyNotFound) = await jobRepository.CreateJobAsync(companyId, userId, request, cancellationToken);

        if (companyNotFound)
            return Problem(statusCode: 404, title: "Firma nebyla nalezena.");

        return CreatedAtAction(nameof(Get), new { companyId, jobId = job!.Id }, job);
    }

    [HttpPut("{jobId:guid}")]
    public async Task<ActionResult<JobOfferManagementResponse>> Update(
        Guid companyId, Guid jobId, JobOfferRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);

        var (job, status) = await jobRepository.UpdateJobAsync(companyId, jobId, userId, request, cancellationToken);

        return status switch
        {
            JobUpdateStatus.CompanyNotFound => Problem(statusCode: 404, title: "Firma nebyla nalezena."),
            JobUpdateStatus.OfferNotFound => Problem(statusCode: 404, title: "Nabídka nebyla nalezena."),
            JobUpdateStatus.NotDraft => Problem(statusCode: 409, title: "Upravovat lze pouze koncept nabídky."),
            JobUpdateStatus.Success => Ok(job!),
            _ => Problem(statusCode: 500, title: "Neočekávaná chyba serveru.")
        };
    }

    [HttpPost("{jobId:guid}/publish")]
    public async Task<ActionResult<JobOfferManagementResponse>> Publish(
        Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var result = await jobRepository.PublishJobAsync(companyId, jobId, userId, cancellationToken);
        return TransitionResult(result);
    }

    [HttpPost("{jobId:guid}/close")]
    public async Task<ActionResult<JobOfferManagementResponse>> Close(
        Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var result = await jobRepository.CloseJobAsync(companyId, jobId, userId, cancellationToken);
        return TransitionResult(result);
    }

    private ActionResult<JobOfferManagementResponse> TransitionResult(
        (JobOfferManagementResponse? Job, JobTransitionStatus Status) result) => result.Status switch
    {
        JobTransitionStatus.CompanyNotFound => Problem(statusCode: 404, title: "Firma nebyla nalezena."),
        JobTransitionStatus.OfferNotFound => Problem(statusCode: 404, title: "Nabídka nebyla nalezena."),
        JobTransitionStatus.InvalidStatus => Problem(statusCode: 409, title: "Stav nabídky neumožňuje požadovanou změnu."),
        JobTransitionStatus.DeadlineExpired => Problem(statusCode: 409, title: "Nabídku s uplynulým termínem přihlášek nelze publikovat."),
        JobTransitionStatus.Success => Ok(result.Job!),
        _ => Problem(statusCode: 500, title: "Neočekávaná chyba serveru.")
    };
}
