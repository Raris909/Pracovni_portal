using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/jobs/{jobId:guid}/stages")]
public sealed class RecruitmentStagesController(IRecruitmentStageRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecruitmentStageResponse>>> List(
        Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var stages = await repository.ListAsync(companyId, jobId, userId, cancellationToken);
        return stages is null ? Failure(RecruitmentStageStatus.JobNotFound) : Ok(stages);
    }

    [HttpPost]
    public async Task<ActionResult<RecruitmentStageResponse>> Create(
        Guid companyId, Guid jobId, UpsertRecruitmentStageRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (stage, status) = await repository.CreateAsync(companyId, jobId, userId, request, cancellationToken);
        return status == RecruitmentStageStatus.Success
            ? CreatedAtAction(nameof(List), new { companyId, jobId }, stage!)
            : Failure(status);
    }

    [HttpPut("{stageId:guid}")]
    public async Task<ActionResult<RecruitmentStageResponse>> Update(
        Guid companyId, Guid jobId, Guid stageId, UpsertRecruitmentStageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (stage, status) = await repository.UpdateAsync(companyId, jobId, userId, stageId, request, cancellationToken);
        return status == RecruitmentStageStatus.Success ? Ok(stage!) : Failure(status);
    }

    [HttpDelete("{stageId:guid}")]
    public async Task<IActionResult> Delete(
        Guid companyId, Guid jobId, Guid stageId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var status = await repository.DeleteAsync(companyId, jobId, userId, stageId, cancellationToken);
        return status == RecruitmentStageStatus.Success ? NoContent() : Failure(status);
    }

    [HttpPut("order")]
    public async Task<ActionResult<IReadOnlyList<RecruitmentStageResponse>>> Reorder(
        Guid companyId, Guid jobId, ReorderRecruitmentStagesRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (stages, status) = await repository.ReorderAsync(companyId, jobId, userId, request, cancellationToken);
        return status == RecruitmentStageStatus.Success ? Ok(stages!) : Failure(status);
    }

    private ObjectResult Failure(RecruitmentStageStatus status) => status switch
    {
        RecruitmentStageStatus.JobNotFound => Problem(statusCode: 404, title: "Nabídka nebyla nalezena."),
        RecruitmentStageStatus.StageNotFound => Problem(statusCode: 404, title: "Náborové kolo nebylo nalezeno."),
        RecruitmentStageStatus.PositionConflict => Problem(statusCode: 409, title: "Na této pozici již existuje náborové kolo."),
        RecruitmentStageStatus.StageInUse => Problem(statusCode: 409, title: "Použité kolo nelze smazat ani změnit jeho typ."),
        RecruitmentStageStatus.InvalidOrder => Problem(statusCode: 400, title: "Pořadí musí obsahovat právě všechna kola nabídky."),
        _ => Problem(statusCode: 500, title: "Neočekávaná chyba serveru.")
    };
}
