using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public enum InterviewBookingStatus
{
    Success,
    StageNotFound,
    SlotNotFound,
    BookingNotFound,
    Unavailable
}

public interface IInterviewBookingRepository
{
    Task<IReadOnlyList<InterviewSlotResponse>?> ListAvailableSlotsAsync(
        Guid applicationId, Guid stageId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InterviewBookingResponse>?> ListBookingsAsync(
        Guid applicationId, Guid stageId, Guid userId, CancellationToken cancellationToken);

    Task<(InterviewBookingResponse? Booking, InterviewBookingStatus Status)> CreateAsync(
        Guid applicationId, Guid stageId, Guid userId, CreateInterviewBookingRequest request,
        CancellationToken cancellationToken);

    Task<InterviewBookingStatus> CancelAsync(
        Guid applicationId, Guid stageId, Guid userId, Guid bookingId, CancellationToken cancellationToken);
}
