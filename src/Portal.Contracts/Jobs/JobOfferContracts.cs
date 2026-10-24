using System.ComponentModel.DataAnnotations;
using Portal.Contracts.Companies;

namespace Portal.Contracts.Jobs;

public static class JobOfferStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Closed = "closed";
    public const string Archived = "archived";
}

public static class WorkModes
{
    public const string Onsite = "onsite";
    public const string Hybrid = "hybrid";
    public const string Remote = "remote";
}

public static class EmploymentTypes
{
    public const string FullTime = "full_time";
    public const string PartTime = "part_time";
    public const string Contract = "contract";
    public const string Internship = "internship";
}

public static class SalaryPeriods
{
    public const string Hour = "hour";
    public const string Month = "month";
    public const string Year = "year";
}

public sealed class JobOfferRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [Required, MaxLength(20000)]
    public string Description { get; set; } = "";

    [Required(AllowEmptyStrings = true), MaxLength(20000)]
    public string Requirements { get; set; } = "";

    [MaxLength(200)]
    public string? Location { get; set; }

    [Required, RegularExpression("^(onsite|hybrid|remote)$")]
    public string WorkMode { get; set; } = "";

    [Required, RegularExpression("^(full_time|part_time|contract|internship)$")]
    public string EmploymentType { get; set; } = "";

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal? SalaryMin { get; set; }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal? SalaryMax { get; set; }

    [RegularExpression("^[A-Z]{3}$")]
    public string? SalaryCurrency { get; set; }

    [RegularExpression("^(hour|month|year)$")]
    public string? SalaryPeriod { get; set; }

    public DateTimeOffset? ApplicationDeadline { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasSalary = SalaryMin.HasValue || SalaryMax.HasValue;
        if (hasSalary && (string.IsNullOrEmpty(SalaryCurrency) || string.IsNullOrEmpty(SalaryPeriod)))
            yield return new ValidationResult("Salary currency and period are required when a salary is specified.",
                [nameof(SalaryCurrency), nameof(SalaryPeriod)]);

        if (!hasSalary && (SalaryCurrency is not null || SalaryPeriod is not null))
            yield return new ValidationResult("A salary amount is required when currency or period is specified.",
                [nameof(SalaryMin), nameof(SalaryMax)]);

        if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMax < SalaryMin)
            yield return new ValidationResult("Maximum salary must be greater than or equal to minimum salary.",
                [nameof(SalaryMax)]);

        if (SalaryMin.HasValue && decimal.Round(SalaryMin.Value, 2) != SalaryMin.Value)
            yield return new ValidationResult("Salary must have at most two decimal places.", [nameof(SalaryMin)]);

        if (SalaryMax.HasValue && decimal.Round(SalaryMax.Value, 2) != SalaryMax.Value)
            yield return new ValidationResult("Salary must have at most two decimal places.", [nameof(SalaryMax)]);
    }
}

public sealed record JobOfferManagementResponse(
    Guid Id,
    Guid CompanyId,
    Guid CreatedByMemberId,
    string Title,
    string Description,
    string Requirements,
    string? Location,
    string WorkMode,
    string EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    string? SalaryPeriod,
    string Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ApplicationDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobOfferDetailResponse(
    Guid Id,
    CompanyResponse Company,
    string Title,
    string Description,
    string Requirements,
    string? Location,
    string WorkMode,
    string EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    string? SalaryPeriod,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ApplicationDeadline);
