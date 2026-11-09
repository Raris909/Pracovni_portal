using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public enum InterviewSlotStatus
{
    Success,
    StageNotFound,
    SlotNotFound,
    InterviewerNotFound,
    Overlap,
    Booked
}

public interface IInterviewSlotRepository
{
    Task<IReadOnlyList<InterviewSlotResponse>?> ListAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, CancellationToken cancellationToken);

    Task<(InterviewSlotResponse? Slot, InterviewSlotStatus Status)> CreateAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, UpsertInterviewSlotRequest request,
        CancellationToken cancellationToken);

    Task<(InterviewSlotResponse? Slot, InterviewSlotStatus Status)> UpdateAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, Guid slotId, UpsertInterviewSlotRequest request,
        CancellationToken cancellationToken);

    Task<InterviewSlotStatus> DeactivateAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, Guid slotId, CancellationToken cancellationToken);
}
