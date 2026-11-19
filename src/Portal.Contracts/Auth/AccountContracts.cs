using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Auth;

public sealed class UpdateAccountRequest : IValidatableObject
{
    [Required(ErrorMessage = "Jméno je povinné.")]
    [MaxLength(100)]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Příjmení je povinné.")]
    [MaxLength(100)]
    public string LastName { get; set; } = "";

    [MaxLength(32)]
    public string? Phone { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(FirstName))
        {
            yield return new ValidationResult("Jméno nesmí být prázdné.", [nameof(FirstName)]);
        }

        if (string.IsNullOrWhiteSpace(LastName))
        {
            yield return new ValidationResult("Příjmení nesmí být prázdné.", [nameof(LastName)]);
        }
    }
}

public sealed class ChangePasswordRequest : IValidatableObject
{
    [Required(ErrorMessage = "Současné heslo je povinné.")]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Nové heslo je povinné.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Heslo musí mít 12 až 128 znaků.")]
    public string NewPassword { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(NewPassword))
            yield break;

        if (string.Equals(CurrentPassword, NewPassword, StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                "Nové heslo se musí lišit od současného.",
                [nameof(NewPassword)]);
        }

        if (!NewPassword.Any(char.IsUpper))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jedno velké písmeno.",
                [nameof(NewPassword)]);
        }

        if (!NewPassword.Any(char.IsLower))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jedno malé písmeno.",
                [nameof(NewPassword)]);
        }

        if (!NewPassword.Any(char.IsDigit))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jednu číslici.",
                [nameof(NewPassword)]);
        }

        if (!NewPassword.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jeden speciální znak.",
                [nameof(NewPassword)]);
        }
    }
}

public sealed class DeactivateAccountRequest
{
    [Required(ErrorMessage = "Současné heslo je povinné.")]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = "";
}
