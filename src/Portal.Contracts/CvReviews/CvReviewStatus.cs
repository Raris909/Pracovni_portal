namespace Portal.Contracts.CvReviews;

/// <summary>
/// Represents the lifecycle state of an AI CV review (<c>status</c> column in DB).
/// </summary>
public static class CvReviewStatuses
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Queued,
        Processing,
        Succeeded,
        Failed
    };
}