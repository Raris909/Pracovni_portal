using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using Portal.Api.Companies;
using Portal.Contracts.Auth;
using Portal.Contracts.Jobs;

namespace Portal.Api.Jobs;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/jobs")]
public sealed class CompanyJobsController(
    NpgsqlDataSource database, CompanyMembershipService memberships) : ControllerBase
{
    private const string Columns = """
        id, company_id, created_by_member_id, title, description, requirements, location,
        work_mode, employment_type, salary_min, salary_max, salary_currency, salary_period,
        status, published_at, application_deadline, created_at, updated_at
        """;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobOfferManagementResponse>>> List(
        Guid companyId, CancellationToken cancellationToken,
        [FromQuery] int offset = 0, [FromQuery] int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 100)
            return Problem(statusCode: 400, title: "Offset musí být nezáporný a limit musí být 1 až 100.");

        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        if (await memberships.FindAsync(userId, companyId, cancellationToken) is null)
            return Problem(statusCode: 404, title: "Firma nebyla nalezena.");

        await using var command = database.CreateCommand($"""
            SELECT {Columns} FROM portal.job_offers
            WHERE company_id = $1
            ORDER BY created_at DESC, id
            LIMIT $2 OFFSET $3
            """);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(offset);
        var jobs = new List<JobOfferManagementResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            jobs.Add(ReadJob(reader));

        return jobs;
    }

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobOfferManagementResponse>> Get(
        Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        if (await memberships.FindAsync(userId, companyId, cancellationToken) is null)
            return Problem(statusCode: 404, title: "Firma nebyla nalezena.");

        await using var command = database.CreateCommand($"""
            SELECT {Columns} FROM portal.job_offers WHERE company_id = $1 AND id = $2
            """);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Problem(statusCode: 404, title: "Nabídka nebyla nalezena.");

        return ReadJob(reader);
    }

    [HttpPost]
    public async Task<ActionResult<JobOfferManagementResponse>> Create(
        Guid companyId, JobOfferRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var memberId = await LockMemberAsync(connection, transaction, userId, companyId, cancellationToken);
        if (memberId is null)
            return Problem(statusCode: 404, title: "Firma nebyla nalezena.");

        await using var command = new NpgsqlCommand($"""
            INSERT INTO portal.job_offers (
                company_id, created_by_member_id, title, description, requirements, location,
                work_mode, employment_type, salary_min, salary_max, salary_currency, salary_period,
                application_deadline)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId.Value);
        AddRequestParameters(command, request);
        JobOfferManagementResponse job;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            job = ReadJob(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { companyId, jobId = job.Id }, job);
    }

    [HttpPut("{jobId:guid}")]
    public async Task<ActionResult<JobOfferManagementResponse>> Update(
        Guid companyId, Guid jobId, JobOfferRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await LockMemberAsync(connection, transaction, userId, companyId, cancellationToken) is null)
            return Problem(statusCode: 404, title: "Firma nebyla nalezena.");

        await using var statusCommand = new NpgsqlCommand("""
            SELECT status FROM portal.job_offers WHERE company_id = $1 AND id = $2 FOR UPDATE
            """, connection, transaction);
        statusCommand.Parameters.AddWithValue(companyId);
        statusCommand.Parameters.AddWithValue(jobId);
        var status = (string?)await statusCommand.ExecuteScalarAsync(cancellationToken);
        if (status is null)
            return Problem(statusCode: 404, title: "Nabídka nebyla nalezena.");
        if (status != JobOfferStatuses.Draft)
            return Problem(statusCode: 409, title: "Upravovat lze pouze koncept nabídky.");

        await using var command = new NpgsqlCommand($"""
            UPDATE portal.job_offers SET
                title = $3, description = $4, requirements = $5, location = $6,
                work_mode = $7, employment_type = $8, salary_min = $9, salary_max = $10,
                salary_currency = $11, salary_period = $12, application_deadline = $13
            WHERE company_id = $1 AND id = $2
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);
        AddRequestParameters(command, request);
        JobOfferManagementResponse job;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            job = ReadJob(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    private static async Task<Guid?> LockMemberAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, Guid companyId,
        CancellationToken cancellationToken)
    {
        // Keep access valid until the write commits, including concurrent account or membership revocation.
        await using var command = new NpgsqlCommand("""
            SELECT m.id FROM portal.company_members m
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            WHERE m.company_id = $1 AND m.user_id = $2 AND m.is_active
              AND u.is_active AND r.name = 'company'
            FOR SHARE OF m, u
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(userId);
        return (Guid?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void AddRequestParameters(NpgsqlCommand command, JobOfferRequest request)
    {
        command.Parameters.AddWithValue(request.Title.Trim());
        command.Parameters.AddWithValue(request.Description);
        command.Parameters.AddWithValue(request.Requirements);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Location ?? DBNull.Value);
        command.Parameters.AddWithValue(request.WorkMode);
        command.Parameters.AddWithValue(request.EmploymentType);
        command.Parameters.AddWithValue(NpgsqlDbType.Numeric, (object?)request.SalaryMin ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Numeric, (object?)request.SalaryMax ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.SalaryCurrency ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.SalaryPeriod ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz,
            (object?)request.ApplicationDeadline?.ToUniversalTime() ?? DBNull.Value);
    }

    private static JobOfferManagementResponse ReadJob(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10),
        reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
        reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
        reader.GetFieldValue<DateTimeOffset>(16), reader.GetFieldValue<DateTimeOffset>(17));
}
