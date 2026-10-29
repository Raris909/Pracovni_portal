using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/members")]
public sealed class CompanyMembersController(
    NpgsqlDataSource database, CompanyMembershipService memberships) : ControllerBase
{
    private const string MemberColumns = """
        m.id, m.company_id, m.user_id, u.email, u.first_name, u.last_name,
        m.role, m.is_active, m.created_at, m.updated_at
        """;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyMemberResponse>>> List(
        Guid companyId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        if (await memberships.FindAsync(userId, companyId, cancellationToken) is null)
            return CompanyNotFound();

        await using var command = database.CreateCommand($"""
            SELECT {MemberColumns}
            FROM portal.company_members m JOIN portal.users u ON u.id = m.user_id
            WHERE m.company_id = $1 AND m.is_active
            ORDER BY u.last_name, u.first_name, m.id
            """);
        command.Parameters.AddWithValue(companyId);
        var members = new List<CompanyMemberResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            members.Add(ReadMember(reader));

        return Ok(members);
    }

    [HttpPost]
    public async Task<ActionResult<CompanyMemberResponse>> Add(
        Guid companyId, AddCompanyMemberRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var error = await AuthorizeManagementAsync(connection, transaction, companyId, cancellationToken);
        if (error is not null)
            return error;

        await using var userCommand = new NpgsqlCommand("""
            SELECT u.id
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE lower(u.email) = $1 AND u.is_active AND r.name = 'company'
            FOR SHARE OF u
            """, connection, transaction);
        userCommand.Parameters.AddWithValue(request.Email.Trim().ToLowerInvariant());
        var userId = await userCommand.ExecuteScalarAsync(cancellationToken);
        if (userId is null)
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Aktivní firemní účet s tímto e-mailem nebyl nalezen.");

        await using var command = new NpgsqlCommand($"""
            WITH m AS (
                INSERT INTO portal.company_members (company_id, user_id, role)
                VALUES ($1, $2, $3)
                ON CONFLICT (company_id, user_id) DO UPDATE
                SET role = EXCLUDED.role, is_active = true
                WHERE NOT company_members.is_active AND company_members.role <> 'owner'
                RETURNING *
            )
            SELECT {MemberColumns} FROM m JOIN portal.users u ON u.id = m.user_id
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(request.Role);
        CompanyMemberResponse member;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Uživatel je již členem firmy nebo má chráněné členství vlastníka.");

            member = ReadMember(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return CreatedAtAction(nameof(List), new { companyId }, member);
    }

    [HttpPatch("{memberId:guid}")]
    public async Task<ActionResult<CompanyMemberResponse>> Update(
        Guid companyId, Guid memberId, UpdateCompanyMemberRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var error = await AuthorizeManagementAsync(connection, transaction, companyId, cancellationToken);
        if (error is not null)
            return error;

        var targetError = await CheckTargetAsync(connection, transaction, companyId, memberId, cancellationToken);
        if (targetError is not null)
            return targetError;

        await using var command = new NpgsqlCommand($"""
            UPDATE portal.company_members m SET role = $3
            FROM portal.users u
            WHERE m.company_id = $1 AND m.id = $2 AND u.id = m.user_id
            RETURNING {MemberColumns}
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId);
        command.Parameters.AddWithValue(request.Role);
        CompanyMemberResponse member;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            member = ReadMember(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return member;
    }

    [HttpDelete("{memberId:guid}")]
    public async Task<IActionResult> Remove(Guid companyId, Guid memberId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var error = await AuthorizeManagementAsync(connection, transaction, companyId, cancellationToken);
        if (error is not null)
            return error;

        var targetError = await CheckTargetAsync(connection, transaction, companyId, memberId, cancellationToken);
        if (targetError is not null)
            return targetError;

        await using var command = new NpgsqlCommand("""
            UPDATE portal.company_members SET is_active = false WHERE company_id = $1 AND id = $2
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ObjectResult?> AuthorizeManagementAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, CancellationToken cancellationToken)
    {
        // Serialize team changes before reading permissions, including after a concurrent revocation.
        await using var companyCommand = new NpgsqlCommand(
            "SELECT id FROM portal.companies WHERE id = $1 FOR UPDATE", connection, transaction);
        companyCommand.Parameters.AddWithValue(companyId);
        if (await companyCommand.ExecuteScalarAsync(cancellationToken) is null)
            return CompanyNotFound();

        await using var command = new NpgsqlCommand("""
            SELECT m.role
            FROM portal.company_members m
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            WHERE m.company_id = $1 AND m.user_id = $2
              AND m.is_active AND u.is_active AND r.name = 'company'
            FOR SHARE OF m, u
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(Guid.Parse(User.FindFirstValue("sub")!));
        var role = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (role is null)
            return CompanyNotFound();

        return role is CompanyMemberRoles.Owner or CompanyMemberRoles.Admin
            ? null
            : Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nemáte oprávnění spravovat HR tým.");
    }

    private async Task<ObjectResult?> CheckTargetAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT role FROM portal.company_members
            WHERE company_id = $1 AND id = $2 AND is_active
            FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId);
        var role = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (role is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Člen firmy nebyl nalezen.");

        return role == CompanyMemberRoles.Owner
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: "Členství vlastníka firmy nelze změnit ani odebrat.")
            : null;
    }

    private ObjectResult CompanyNotFound() =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Firma nebyla nalezena.");

    private static CompanyMemberResponse ReadMember(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetBoolean(7),
            reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9));
}
