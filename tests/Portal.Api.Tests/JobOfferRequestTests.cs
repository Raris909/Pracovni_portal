using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Portal.Contracts.Jobs;

namespace Portal.Api.Tests;

public sealed class JobOfferRequestTests
{
    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    public void SalaryValidationAcceptsContractLimitsRegardlessOfCulture(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            (decimal? Amount, bool IsValid)[] cases =
            [
                (null, true), (0m, true), (0.01m, true), (9999999999.99m, true),
                (-0.01m, false), (10000000000m, false), (0.001m, false)
            ];
            foreach (var (amount, isValid) in cases)
            {
                var request = new JobOfferRequest
                {
                    Title = "Developer", Description = "Web development",
                    WorkMode = WorkModes.Remote, EmploymentType = EmploymentTypes.FullTime,
                    SalaryMin = amount, SalaryMax = amount,
                    SalaryCurrency = amount.HasValue ? "CZK" : null,
                    SalaryPeriod = amount.HasValue ? SalaryPeriods.Month : null
                };
                var results = new List<ValidationResult>();
                Assert.Equal(isValid, Validator.TryValidateObject(request, new ValidationContext(request), results,
                    validateAllProperties: true));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
