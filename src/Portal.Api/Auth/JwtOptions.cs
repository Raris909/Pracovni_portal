using System.ComponentModel.DataAnnotations;

namespace Portal.Api.Auth;

public sealed class JwtOptions
{
    [Required]
    public string Issuer { get; set; } = "";

    [Required]
    public string Audience { get; set; } = "";

    [Required, MinLength(64)]
    public string SigningKey { get; set; } = "";

    [Range(1, 60)]
    public int LifetimeMinutes { get; set; } = 30;
}
