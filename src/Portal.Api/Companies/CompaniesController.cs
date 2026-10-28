using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies")]
public sealed class CompaniesController(
    NpgsqlDataSource database, CompanyMembershipService memberships) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CompanyMembershipResponse>> Create(
        CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Keep the founder's account state unchanged until both inserts commit.
        await using var userCommand = new NpgsqlCommand("""
            SELECT u.email, u.first_name, u.last_name
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE u.id = $1 AND u.is_active AND r.name = 'company'
            FOR SHARE OF u
            """, connection, transaction);
        userCommand.Parameters.AddWithValue(userId);
        string email, firstName, lastName;
        await using (var reader = await userCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return Forbid();

            email = reader.GetString(0);
            firstName = reader.GetString(1);
            lastName = reader.GetString(2);
        }

        await using var companyCommand = new NpgsqlCommand("""
            INSERT INTO portal.companies (name, registration_number, country_code, description, website_url, address)
            VALUES ($1, $2, $3, $4, $5, $6)
            RETURNING id, name, registration_number, country_code, description, website_url, address, created_at, updated_at
            """, connection, transaction);
        companyCommand.Parameters.AddWithValue(request.Name.Trim());
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(request.RegistrationNumber) ? DBNull.Value : request.RegistrationNumber.Trim());
        companyCommand.Parameters.AddWithValue(request.CountryCode);
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Description ?? DBNull.Value);
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.WebsiteUrl ?? DBNull.Value);
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Address ?? DBNull.Value);
        CompanyResponse company;
        try
        {
            await using var reader = await companyCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            company = new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "companies_country_code_registration_number_key")
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Firma s tímto registračním číslem již v dané zemi existuje.");
        }

        await using var memberCommand = new NpgsqlCommand("""
            INSERT INTO portal.company_members (company_id, user_id, role)
            VALUES ($1, $2, $3)
            RETURNING id, is_active, created_at, updated_at
            """, connection, transaction);
        memberCommand.Parameters.AddWithValue(company.Id);
        memberCommand.Parameters.AddWithValue(userId);
        memberCommand.Parameters.AddWithValue(CompanyMemberRoles.Owner);
        CompanyMemberResponse membership;
        await using (var reader = await memberCommand.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            membership = new(reader.GetGuid(0), company.Id, userId, email, firstName, lastName,
                CompanyMemberRoles.Owner, reader.GetBoolean(1),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3));
        }

        await transaction.CommitAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { companyId = company.Id }, new CompanyMembershipResponse(company, membership));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyMembershipResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        return Ok(await memberships.ListAsync(userId, cancellationToken));
    }

    [HttpGet("{companyId:guid}")]
    public async Task<ActionResult<CompanyMembershipResponse>> Get(Guid companyId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var membership = await memberships.FindAsync(userId, companyId, cancellationToken);
        if (membership is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Firma nebyla nalezena.");

        return membership;
    }
}
