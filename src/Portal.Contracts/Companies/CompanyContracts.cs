using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Companies;

public sealed class CreateCompanyRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string? RegistrationNumber { get; set; }

    [Required, RegularExpression("^[A-Z]{2}$")]
    public string CountryCode { get; set; } = "";

    [MaxLength(10000)]
    public string? Description { get; set; }

    [Url, MaxLength(2048), RegularExpression("^https?://.+")]
    public string? WebsiteUrl { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }
}

public sealed record CompanyResponse(
    Guid Id,
    string Name,
    string? RegistrationNumber,
    string CountryCode,
    string? Description,
    string? WebsiteUrl,
    string? Address,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CompanyMembershipResponse(
    CompanyResponse Company,
    CompanyMemberResponse Membership);
