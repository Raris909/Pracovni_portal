using System.ComponentModel.DataAnnotations;
using Portal.Contracts.Recruitment;
using Xunit;

namespace Portal.Api.Tests;

public sealed class RecruitmentContractsTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("První kolo", "interview", 1, true)]
    [InlineData("Technický test", "test", 2, true)]
    [InlineData("   ", "interview", 1, false)]
    [InlineData("Pohovor", "invalid_kind", 1, false)]
    [InlineData("Pohovor", "interview", 0, false)]
    public void UpsertRecruitmentStageRequest_ValidatesCorrectly(
        string name,
        string kind,
        int position,
        bool expectedValid)
    {
        var request = new UpsertRecruitmentStageRequest(name, kind, position);
        var errors = ValidateModel(request);

        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Fact]
    public void UpdateApplicationStageStatusRequest_InProgressWithoutStartedAt_FailsValidation()
    {
        var request = new UpdateApplicationStageStatusRequest(
            Status: ApplicationStageStatuses.InProgress,
            StartedAt: null,
            CompletedAt: null);

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateApplicationStageStatusRequest.StartedAt)));
    }

    [Fact]
    public void UpdateApplicationStageStatusRequest_PassedWithValidTimestamps_PassesValidation()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var completed = DateTimeOffset.UtcNow;

        var request = new UpdateApplicationStageStatusRequest(
            Status: ApplicationStageStatuses.Passed,
            StartedAt: started,
            CompletedAt: completed);

        var errors = ValidateModel(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void UpdateApplicationStageStatusRequest_CompletedBeforeStarted_FailsValidation()
    {
        var started = DateTimeOffset.UtcNow;
        var completed = started.AddHours(-1);

        var request = new UpdateApplicationStageStatusRequest(
            Status: ApplicationStageStatuses.Failed,
            StartedAt: started,
            CompletedAt: completed);

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateApplicationStageStatusRequest.CompletedAt)));
    }

    [Theory]
    [InlineData(105, "Skóre mimo rozsah.", false)]
    [InlineData(-5, "Záporné skóre.", false)]
    [InlineData(50, "   ", false)]
    [InlineData(85, "Výborná komunikace a znalost C#.", true)]
    public void UpsertCandidateEvaluationRequest_ValidatesCorrectly(
        int? score,
        string note,
        bool expectedValid)
    {
        short? shortScore = score.HasValue ? (short)score.Value : null;
        var request = new UpsertCandidateEvaluationRequest(shortScore, note);
        var errors = ValidateModel(request);

        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Fact]
    public void UpsertInterviewSlotRequest_EndsAtBeforeOrEqualStartsAt_FailsValidation()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new UpsertInterviewSlotRequest(
            InterviewerMemberId: Guid.NewGuid(),
            StartsAt: now,
            EndsAt: now);

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpsertInterviewSlotRequest.EndsAt)));
    }

    [Fact]
    public void UpdateInterviewBookingStatusRequest_CancelledRequiresCancelledAt()
    {
        var invalidCancelled = new UpdateInterviewBookingStatusRequest(
            Status: InterviewBookingStatuses.Cancelled,
            CancelledAt: null);

        var validCancelled = new UpdateInterviewBookingStatusRequest(
            Status: InterviewBookingStatuses.Cancelled,
            CancelledAt: DateTimeOffset.UtcNow);

        Assert.NotEmpty(ValidateModel(invalidCancelled));
        Assert.Empty(ValidateModel(validCancelled));
    }
}