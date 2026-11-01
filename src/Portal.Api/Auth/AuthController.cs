using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;

namespace Portal.Api.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var (auth, status) = await authService.RegisterAsync(request, cancellationToken);

        return status switch
        {
            RegisterUserStatus.DuplicateEmail => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "E-mail je již registrován."),
            RegisterUserStatus.Success => StatusCode(StatusCodes.Status201Created, auth),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var (auth, status) = await authService.LoginAsync(request, cancellationToken);

        return status switch
        {
            LoginStatus.InvalidCredentials => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Nesprávný e-mail nebo heslo."),
            LoginStatus.Success => Ok(auth!),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var user = await authService.GetCurrentUserAsync(userId, cancellationToken);
        if (user is null)
            return Unauthorized();

        return user;
    }
}
