using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;

namespace Portal.Api.Auth;

[ApiController]
[Authorize]
[Route("api/account")]
public sealed class AccountController(AuthService authService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var user = await authService.GetCurrentUserAsync(userId, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPut]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Update(
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var (user, status) = await authService.UpdateAccountAsync(userId, request, cancellationToken);
        return status switch
        {
            UpdateAccountStatus.Success => Ok(user!),
            UpdateAccountStatus.UserNotFound => Unauthorized(),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    [HttpPut("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var status = await authService.ChangePasswordAsync(userId, request, cancellationToken);
        return status switch
        {
            ChangePasswordStatus.Success => NoContent(),
            ChangePasswordStatus.InvalidCurrentPassword => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Současné heslo není správné."),
            ChangePasswordStatus.UserNotFound => Unauthorized(),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    [HttpPost("deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Deactivate(
        DeactivateAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var status = await authService.DeactivateAccountAsync(userId, request, cancellationToken);
        return status switch
        {
            DeactivateAccountStatus.Success => NoContent(),
            DeactivateAccountStatus.InvalidCurrentPassword => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Současné heslo není správné."),
            DeactivateAccountStatus.UserNotFound => Unauthorized(),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}
