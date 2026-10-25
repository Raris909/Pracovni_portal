using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Auth;

public static class UserRoles
{
    public const string Candidate = "candidate";
    public const string Company = "company";
}

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Potvrzení hesla je povinné.")]
    [Compare(nameof(Password), ErrorMessage = "Zadaná hesla se neshodují.")]
    public string ConfirmPassword { get; set; } = "";

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = "";

    [Required, MaxLength(100)]
    public string LastName { get; set; } = "";

    [Required, RegularExpression("^(candidate|company)$")]
    public string Role { get; set; } = UserRoles.Candidate;
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
