using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/applications/{applicationId:guid}/stages/{stageId:guid}/evaluations")]
public sealed class CandidateEvaluationsController(ICandidateEvaluationRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CandidateEvaluationResponse>>> List(
        Guid companyId, Guid applicationId, Guid stageId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var evaluations = await repository.ListAsync(companyId, applicationId, stageId, userId, cancellationToken);
        return evaluations is null ? Failure(CandidateEvaluationStatus.StageNotFound) : Ok(evaluations);
    }

    [HttpPost]
    public async Task<ActionResult<CandidateEvaluationResponse>> Create(
        Guid companyId, Guid applicationId, Guid stageId, UpsertCandidateEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (evaluation, status) = await repository.CreateAsync(
            companyId, applicationId, stageId, userId, request, cancellationToken);
        return status == CandidateEvaluationStatus.Success
            ? CreatedAtAction(nameof(List), new { companyId, applicationId, stageId }, evaluation!)
            : Failure(status);
    }

    [HttpPut("{evaluationId:guid}")]
    public async Task<ActionResult<CandidateEvaluationResponse>> Update(
        Guid companyId, Guid applicationId, Guid stageId, Guid evaluationId, UpsertCandidateEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (evaluation, status) = await repository.UpdateAsync(
            companyId, applicationId, stageId, userId, evaluationId, request, cancellationToken);
        return status == CandidateEvaluationStatus.Success ? Ok(evaluation!) : Failure(status);
    }

    private ObjectResult Failure(CandidateEvaluationStatus status) => status switch
    {
        CandidateEvaluationStatus.StageNotFound => Problem(statusCode: 404, title: "Přiřazené náborové kolo nebylo nalezeno."),
        CandidateEvaluationStatus.EvaluationNotFound => Problem(statusCode: 404, title: "Hodnocení nebylo nalezeno."),
        CandidateEvaluationStatus.Forbidden => Problem(statusCode: 403, title: "Upravovat můžete pouze vlastní hodnocení."),
        CandidateEvaluationStatus.ApplicationClosed => Problem(statusCode: 409, title: "Hodnocení ukončené přihlášky nelze měnit."),
        _ => Problem(statusCode: 500, title: "Neočekávaná chyba serveru.")
    };
}
