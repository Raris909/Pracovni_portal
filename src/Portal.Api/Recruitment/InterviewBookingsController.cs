using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

[ApiController]
[Authorize(Policy = UserRoles.Candidate)]
[Route("api/applications/{applicationId:guid}/stages/{stageId:guid}")]
public sealed class InterviewBookingsController(IInterviewBookingRepository repository) : ControllerBase
{
    [HttpGet("slots")]
    public async Task<ActionResult<IReadOnlyList<InterviewSlotResponse>>> ListAvailableSlots(
        Guid applicationId, Guid stageId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var slots = await repository.ListAvailableSlotsAsync(applicationId, stageId, userId, cancellationToken);
        return slots is null ? Failure(InterviewBookingStatus.StageNotFound) : Ok(slots);
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<IReadOnlyList<InterviewBookingResponse>>> ListBookings(
        Guid applicationId, Guid stageId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var bookings = await repository.ListBookingsAsync(applicationId, stageId, userId, cancellationToken);
        return bookings is null ? Failure(InterviewBookingStatus.StageNotFound) : Ok(bookings);
    }

    [HttpPost("bookings")]
    public async Task<ActionResult<InterviewBookingResponse>> Create(
        Guid applicationId, Guid stageId, CreateInterviewBookingRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (booking, status) = await repository.CreateAsync(applicationId, stageId, userId, request, cancellationToken);
        return status == InterviewBookingStatus.Success
            ? CreatedAtAction(nameof(ListBookings), new { applicationId, stageId }, booking!) : Failure(status);
    }

    [HttpDelete("bookings/{bookingId:guid}")]
    public async Task<IActionResult> Cancel(
        Guid applicationId, Guid stageId, Guid bookingId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var status = await repository.CancelAsync(applicationId, stageId, userId, bookingId, cancellationToken);
        return status == InterviewBookingStatus.Success ? NoContent() : Failure(status);
    }

    private ObjectResult Failure(InterviewBookingStatus status) => status switch
    {
        InterviewBookingStatus.StageNotFound => Problem(statusCode: 404, title: "Přiřazené kolo pohovoru nebylo nalezeno."),
        InterviewBookingStatus.SlotNotFound => Problem(statusCode: 404, title: "Termín pohovoru nebyl nalezen."),
        InterviewBookingStatus.BookingNotFound => Problem(statusCode: 404, title: "Rezervace nebyla nalezena."),
        InterviewBookingStatus.Unavailable => Problem(statusCode: 409, title: "Termín nebo rezervace již nejsou pro tuto změnu dostupné."),
        _ => Problem(statusCode: 500, title: "Neočekávaná chyba serveru.")
    };
}
