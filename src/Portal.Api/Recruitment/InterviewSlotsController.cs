using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/jobs/{jobId:guid}/stages/{stageId:guid}/slots")]
public sealed class InterviewSlotsController(IInterviewSlotRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InterviewSlotResponse>>> List(
        Guid companyId, Guid jobId, Guid stageId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var slots = await repository.ListAsync(companyId, jobId, stageId, userId, cancellationToken);
        return slots is null ? Failure(InterviewSlotStatus.StageNotFound) : Ok(slots);
    }

    [HttpPost]
    public async Task<ActionResult<InterviewSlotResponse>> Create(
        Guid companyId, Guid jobId, Guid stageId, UpsertInterviewSlotRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (slot, status) = await repository.CreateAsync(companyId, jobId, stageId, userId, request, cancellationToken);
        return status == InterviewSlotStatus.Success
            ? CreatedAtAction(nameof(List), new { companyId, jobId, stageId }, slot!) : Failure(status);
    }

    [HttpPut("{slotId:guid}")]
    public async Task<ActionResult<InterviewSlotResponse>> Update(
        Guid companyId, Guid jobId, Guid stageId, Guid slotId, UpsertInterviewSlotRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (slot, status) = await repository.UpdateAsync(companyId, jobId, stageId, userId, slotId, request, cancellationToken);
        return status == InterviewSlotStatus.Success ? Ok(slot!) : Failure(status);
    }

    [HttpDelete("{slotId:guid}")]
    public async Task<IActionResult> Deactivate(
        Guid companyId, Guid jobId, Guid stageId, Guid slotId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var status = await repository.DeactivateAsync(companyId, jobId, stageId, userId, slotId, cancellationToken);
        return status == InterviewSlotStatus.Success ? NoContent() : Failure(status);
    }

    private ObjectResult Failure(InterviewSlotStatus status) => status switch
    {
        InterviewSlotStatus.StageNotFound => Problem(statusCode: 404, title: "Kolo pohovoru nebylo nalezeno."),
        InterviewSlotStatus.SlotNotFound => Problem(statusCode: 404, title: "Termín pohovoru nebyl nalezen."),
        InterviewSlotStatus.InterviewerNotFound => Problem(statusCode: 404, title: "Aktivní HR člen firmy nebyl nalezen."),
        InterviewSlotStatus.Overlap => Problem(statusCode: 409, title: "HR má v tomto čase jiný aktivní termín."),
        InterviewSlotStatus.Booked => Problem(statusCode: 409, title: "Obsazený termín nelze měnit ani deaktivovat."),
        _ => Problem(statusCode: 500, title: "Neočekávaná chyba serveru.")
    };
}
