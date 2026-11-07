using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/applications/{applicationId:guid}/stages")]
public sealed class ApplicationStagesController(IApplicationStageRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApplicationStageResponse>>> List(
        Guid companyId, Guid applicationId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var stages = await repository.ListAsync(companyId, applicationId, userId, cancellationToken);
        return stages is null ? Failure(ApplicationStageStatus.ApplicationNotFound) : Ok(stages);
    }

    [HttpPut("{stageId:guid}")]
    public async Task<ActionResult<ApplicationStageResponse>> Assign(
        Guid companyId, Guid applicationId, Guid stageId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (stage, status) = await repository.AssignAsync(companyId, applicationId, userId, stageId, cancellationToken);
        return status switch
        {
            ApplicationStageStatus.Created => CreatedAtAction(nameof(List), new { companyId, applicationId }, stage!),
            ApplicationStageStatus.Success => Ok(stage!),
            _ => Failure(status)
        };
    }

    [HttpPatch("{stageId:guid}/status")]
    public async Task<ActionResult<ApplicationStageResponse>> UpdateStatus(
        Guid companyId, Guid applicationId, Guid stageId, UpdateApplicationStageStatusRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (stage, status) = await repository.UpdateStatusAsync(
            companyId, applicationId, userId, stageId, request, cancellationToken);
        return status == ApplicationStageStatus.Success ? Ok(stage!) : Failure(status);
    }

    private ObjectResult Failure(ApplicationStageStatus status) => status switch
    {
        ApplicationStageStatus.ApplicationNotFound => Problem(statusCode: 404, title: "Přihláška nebyla nalezena."),
        ApplicationStageStatus.StageNotFound => Problem(statusCode: 404, title: "Náborové kolo nebylo nalezeno."),
        ApplicationStageStatus.ApplicationClosed => Problem(statusCode: 409, title: "Kola ukončené přihlášky nelze měnit."),
        ApplicationStageStatus.InvalidTransition => Problem(statusCode: 409, title: "Tato změna stavu kola není povolena."),
        _ => Problem(statusCode: 500, title: "Neočekávaná chyba serveru.")
    };
}
