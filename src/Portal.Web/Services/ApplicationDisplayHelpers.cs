using Portal.Contracts.Applications;
using Portal.Contracts.Profiles;
using Portal.Contracts.Recruitment;

namespace Portal.Web.Services;

public static class ApplicationDisplayHelpers
{
    public static string GetApplicationStatusLabel(string status) => status switch
    {
        ApplicationStatuses.Submitted => "Odesláno",
        ApplicationStatuses.InProgress => "V řízení",
        ApplicationStatuses.Accepted => "Přijato",
        ApplicationStatuses.Rejected => "Zamítnuto",
        ApplicationStatuses.Withdrawn => "Staženo",
        _ => status
    };

    public static string GetApplicationStatusBadgeClass(string status) => status switch
    {
        ApplicationStatuses.Submitted => "badge badge-info",
        ApplicationStatuses.InProgress => "badge badge-warning",
        ApplicationStatuses.Accepted => "badge badge-success",
        ApplicationStatuses.Rejected => "badge badge-danger",
        ApplicationStatuses.Withdrawn => "badge badge-muted",
        _ => "badge"
    };

    public static string GetStageKindLabel(string kind) => kind switch
    {
        RecruitmentStageKinds.Screening => "Screening",
        RecruitmentStageKinds.Interview => "Pohovor",
        RecruitmentStageKinds.Test => "Test / úkol",
        RecruitmentStageKinds.Offer => "Nabídka",
        RecruitmentStageKinds.Other => "Ostatní",
        _ => kind
    };

    public static string GetStageStatusLabel(string status) => status switch
    {
        ApplicationStageStatuses.Pending => "Čeká",
        ApplicationStageStatuses.InProgress => "Probíhá",
        ApplicationStageStatuses.Passed => "Splněno",
        ApplicationStageStatuses.Failed => "Nesplněno",
        ApplicationStageStatuses.Skipped => "Přeskočeno",
        _ => status
    };

    public static string GetStageStatusBadgeClass(string status) => status switch
    {
        ApplicationStageStatuses.Pending => "badge badge-muted",
        ApplicationStageStatuses.InProgress => "badge badge-warning",
        ApplicationStageStatuses.Passed => "badge badge-success",
        ApplicationStageStatuses.Failed => "badge badge-danger",
        ApplicationStageStatuses.Skipped => "badge badge-muted",
        _ => "badge"
    };

    public static string GetBookingStatusLabel(string status) => status switch
    {
        InterviewBookingStatuses.Confirmed => "Potvrzeno",
        InterviewBookingStatuses.Cancelled => "Zrušeno",
        InterviewBookingStatuses.Completed => "Proběhlo",
        InterviewBookingStatuses.NoShow => "Bez účasti",
        _ => status
    };

    public static string GetBookingStatusBadgeClass(string status) => status switch
    {
        InterviewBookingStatuses.Confirmed => "badge badge-success",
        InterviewBookingStatuses.Completed => "badge badge-info",
        InterviewBookingStatuses.Cancelled => "badge badge-muted",
        InterviewBookingStatuses.NoShow => "badge badge-danger",
        _ => "badge"
    };

    public static string GetDocumentKindLabel(string kind) => kind switch
    {
        CandidateDocumentKinds.Cv => "Životopis (CV)",
        CandidateDocumentKinds.CoverLetter => "Motivační dopis",
        CandidateDocumentKinds.Other => "Ostatní dokument",
        _ => kind
    };

    public static string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes < 1024)
            return $"{sizeBytes} B";
        if (sizeBytes < 1024 * 1024)
            return $"{sizeBytes / 1024.0:F1} kB";
        return $"{sizeBytes / (1024.0 * 1024.0):F2} MB";
    }
}
