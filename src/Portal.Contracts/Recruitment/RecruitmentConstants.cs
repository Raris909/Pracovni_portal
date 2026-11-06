namespace Portal.Contracts.Recruitment;

/// <summary>
/// Allowed values for <c>portal.recruitment_stages.kind</c>.
/// </summary>
public static class RecruitmentStageKinds
{
    public const string Screening = "screening";
    public const string Interview = "interview";
    public const string Test = "test";
    public const string Offer = "offer";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Screening,
        Interview,
        Test,
        Offer,
        Other
    };
}

/// <summary>
/// Allowed values for <c>portal.application_stages.status</c>.
/// </summary>
public static class ApplicationStageStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Pending,
        InProgress,
        Passed,
        Failed,
        Skipped
    };

    public static readonly IReadOnlySet<string> CompletedStatuses = new HashSet<string>
    {
        Passed,
        Failed,
        Skipped
    };
}

/// <summary>
/// Allowed values for <c>portal.interview_bookings.status</c>.
/// </summary>
public static class InterviewBookingStatuses
{
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
    public const string NoShow = "no_show";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Confirmed,
        Cancelled,
        Completed,
        NoShow
    };
}