using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Companies;

public static class CompanyMemberRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Recruiter = "recruiter";
}

public sealed class AddCompanyMemberRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = "";

    [Required, RegularExpression("^(admin|recruiter)$")]
    public string Role { get; set; } = CompanyMemberRoles.Recruiter;
}

public sealed class UpdateCompanyMemberRequest
{
    [Required, RegularExpression("^(admin|recruiter)$")]
    public string Role { get; set; } = "";
}

public sealed record CompanyMemberResponse(
    Guid Id,
    Guid CompanyId,
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
