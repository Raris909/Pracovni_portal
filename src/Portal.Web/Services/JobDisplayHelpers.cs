using Portal.Contracts.Jobs;

namespace Portal.Web.Services;

public static class JobDisplayHelpers
{
    public static string GetWorkModeLabel(string mode) => mode switch
    {
        WorkModes.Onsite => "Na pracovišti",
        WorkModes.Hybrid => "Hybridní",
        WorkModes.Remote => "Remote",
        _ => mode
    };

    public static string GetEmploymentTypeLabel(string type) => type switch
    {
        EmploymentTypes.FullTime => "Plný úvazek",
        EmploymentTypes.PartTime => "Zkrácený úvazek",
        EmploymentTypes.Contract => "Kontrakt / IČO",
        EmploymentTypes.Internship => "Stáž",
        _ => type
    };

    public static bool TryGetSafeHttpUrl(string? rawUrl, out string safeUrl)
    {
        safeUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
            return false;

        if (Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            safeUrl = uri.AbsoluteUri;
            return true;
        }

        return false;
    }
}