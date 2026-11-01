using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Portal.Contracts.Auth;

public static class UserRoles
{
    public const string Candidate = "candidate";
    public const string Company = "company";
}

public sealed class RegisterRequest : IValidatableObject
{
    private const string StrictEmailPattern = @"^[^@\s]+@[^@\s]+\.[a-zA-Z]{2,}$";

    [Required(ErrorMessage = "E-mail je povinný.")]
    [MaxLength(254)]
    [RegularExpression(StrictEmailPattern, ErrorMessage = "E-mail musí být v platném formátu (např. jmeno@domena.cz).")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Heslo je povinné.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Heslo musí mít 12 až 128 znaků.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Potvrzení hesla je povinné.")]
    [Compare(nameof(Password), ErrorMessage = "Zadaná hesla se neshodují.")]
    public string ConfirmPassword { get; set; } = "";

    [Required(ErrorMessage = "Jméno je povinné.")]
    [MaxLength(100)]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Příjmení je povinné.")]
    [MaxLength(100)]
    public string LastName { get; set; } = "";

    [Required]
    [RegularExpression("^(candidate|company)$", ErrorMessage = "Neplatná role uživatele.")]
    public string Role { get; set; } = UserRoles.Candidate;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(Password))
            yield break;

        if (!Password.Any(char.IsUpper))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jedno velké písmeno.",
                [nameof(Password)]);
        }

        if (!Password.Any(char.IsLower))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jedno malé písmeno.",
                [nameof(Password)]);
        }

        if (!Password.Any(char.IsDigit))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jednu číslici.",
                [nameof(Password)]);
        }

        if (!Password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            yield return new ValidationResult(
                "Heslo musí obsahovat alespoň jeden speciální znak.",
                [nameof(Password)]);
        }
    }
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = "";

    [Required, MaxLength(128)]
    public string Password { get; set; } = "";
}

public sealed record UserResponse(Guid Id, string Email, string FirstName, string LastName, string Role);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);
