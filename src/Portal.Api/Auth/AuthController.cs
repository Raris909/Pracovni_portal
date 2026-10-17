using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Portal.Contracts.Auth;

namespace Portal.Api.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    NpgsqlDataSource database,
    IPasswordHasher<UserResponse> passwordHasher,
    TokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new UserResponse(Guid.NewGuid(), request.Email.Trim().ToLowerInvariant(),
            request.FirstName.Trim(), request.LastName.Trim(), request.Role);
        var passwordHash = passwordHasher.HashPassword(user, request.Password);

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO portal.users (id, role_id, email, password_hash, first_name, last_name)
            VALUES ($1, (SELECT id FROM portal.roles WHERE name = $2), $3, $4, $5, $6)
            """, connection, transaction);
        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(user.Role);
        command.Parameters.AddWithValue(user.Email);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(user.FirstName);
        command.Parameters.AddWithValue(user.LastName);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "users_email_unique")
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "E-mail je již registrován.");
        }

        if (user.Role == UserRoles.Candidate)
        {
            await using var profile = new NpgsqlCommand(
                "INSERT INTO portal.candidate_profiles (user_id) VALUES ($1)", connection, transaction);
            profile.Parameters.AddWithValue(user.Id);
            await profile.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, tokens.Create(user));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT u.id, u.email, u.first_name, u.last_name, r.name, u.password_hash
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE lower(u.email) = $1 AND u.is_active
            """);
        command.Parameters.AddWithValue(request.Email.Trim().ToLowerInvariant());
        UserResponse user;
        string passwordHash;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return InvalidCredentials();

            user = ReadUser(reader);
            passwordHash = reader.GetString(5);
        }

        var result = passwordHasher.VerifyHashedPassword(user, passwordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            return InvalidCredentials();

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await using var update = database.CreateCommand("UPDATE portal.users SET password_hash = $1 WHERE id = $2");
            update.Parameters.AddWithValue(passwordHasher.HashPassword(user, request.Password));
            update.Parameters.AddWithValue(user.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return tokens.Create(user);
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        await using var command = database.CreateCommand("""
            SELECT u.id, u.email, u.first_name, u.last_name, r.name
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE u.id = $1 AND u.is_active
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Unauthorized();

        return ReadUser(reader);
    }

    private ObjectResult InvalidCredentials() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Nesprávný e-mail nebo heslo.");

    private static UserResponse ReadUser(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
}
