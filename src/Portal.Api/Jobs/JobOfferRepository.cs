using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Companies;
using Portal.Contracts.Jobs;

namespace Portal.Api.Jobs;

public sealed class JobOfferRepository(NpgsqlDataSource database) : IJobOfferRepository
{
    private const string Columns = """
        id, company_id, created_by_member_id, title, description, requirements, location,
        work_mode, employment_type, salary_min, salary_max, salary_currency, salary_period,
        status, published_at, application_deadline, created_at, updated_at
        """;

    public async Task<IReadOnlyList<JobOfferResponse>> GetPublishedJobsAsync(int limit, int offset, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT j.id, j.title, c.name, j.location, j.work_mode
            FROM portal.job_offers j JOIN portal.companies c ON c.id = j.company_id
            WHERE j.status = 'published'
              AND (j.application_deadline IS NULL OR j.application_deadline > now())
            ORDER BY j.published_at DESC, j.id
            LIMIT $1 OFFSET $2
            """);
        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(offset);

        var jobs = new List<JobOfferResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
        }
        return jobs;
    }

    public async Task<JobOfferDetailResponse?> GetPublishedJobByIdAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT j.id, j.title, j.description, j.requirements, j.location, j.work_mode,
                j.employment_type, j.salary_min, j.salary_max, j.salary_currency, j.salary_period,
                j.published_at, j.application_deadline,
                c.id, c.name, c.registration_number, c.country_code, c.description,
                c.website_url, c.address, c.created_at, c.updated_at
            FROM portal.job_offers j JOIN portal.companies c ON c.id = j.company_id
            WHERE j.id = $1 AND j.status = 'published'
              AND (j.application_deadline IS NULL OR j.application_deadline > now())
            """);
        command.Parameters.AddWithValue(jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var company = new CompanyResponse(
            reader.GetGuid(13), reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18), reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.GetFieldValue<DateTimeOffset>(20), reader.GetFieldValue<DateTimeOffset>(21));

        return new JobOfferDetailResponse(
            reader.GetGuid(0), company, reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7), reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
    }

    public async Task<IReadOnlyList<JobOfferManagementResponse>> GetCompanyJobsAsync(Guid companyId, int limit, int offset, CancellationToken cancellationToken)
    {
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

    public async Task<JobOfferManagementResponse?> GetCompanyJobByIdAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"""
            SELECT {Columns} FROM portal.job_offers WHERE company_id = $1 AND id = $2
            """);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public async Task<(JobOfferManagementResponse? Job, bool CompanyNotFound)> CreateJobAsync(
        Guid companyId, Guid userId, JobOfferRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var memberId = await LockMemberAsync(connection, transaction, userId, companyId, cancellationToken);
        if (memberId is null)
            return (null, true);

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
        return (job, false);
    }

    public async Task<(JobOfferManagementResponse? Job, JobUpdateStatus Status)> UpdateJobAsync(
        Guid companyId, Guid jobId, Guid userId, JobOfferRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await LockMemberAsync(connection, transaction, userId, companyId, cancellationToken) is null)
            return (null, JobUpdateStatus.CompanyNotFound);

        await using var statusCommand = new NpgsqlCommand("""
            SELECT status FROM portal.job_offers WHERE company_id = $1 AND id = $2 FOR UPDATE
            """, connection, transaction);
        statusCommand.Parameters.AddWithValue(companyId);
        statusCommand.Parameters.AddWithValue(jobId);

        var status = (string?)await statusCommand.ExecuteScalarAsync(cancellationToken);
        if (status is null)
            return (null, JobUpdateStatus.OfferNotFound);
        if (status != JobOfferStatuses.Draft)
            return (null, JobUpdateStatus.NotDraft);

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
        return (job, JobUpdateStatus.Success);
    }

    public Task<(JobOfferManagementResponse? Job, JobTransitionStatus Status)> PublishJobAsync(
        Guid companyId, Guid jobId, Guid userId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(companyId, jobId, userId, JobOfferStatuses.Published, cancellationToken);

    public Task<(JobOfferManagementResponse? Job, JobTransitionStatus Status)> CloseJobAsync(
        Guid companyId, Guid jobId, Guid userId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(companyId, jobId, userId, JobOfferStatuses.Closed, cancellationToken);

    private async Task<(JobOfferManagementResponse? Job, JobTransitionStatus Status)> ChangeStatusAsync(
        Guid companyId, Guid jobId, Guid userId, string targetStatus, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await LockMemberAsync(connection, transaction, userId, companyId, cancellationToken) is null)
            return (null, JobTransitionStatus.CompanyNotFound);

        await using var statusCommand = new NpgsqlCommand("""
            SELECT status FROM portal.job_offers WHERE company_id = $1 AND id = $2 FOR UPDATE
            """, connection, transaction);
        statusCommand.Parameters.AddWithValue(companyId);
        statusCommand.Parameters.AddWithValue(jobId);

        var status = (string?)await statusCommand.ExecuteScalarAsync(cancellationToken);
        if (status is null)
            return (null, JobTransitionStatus.OfferNotFound);

        var requiredStatus = targetStatus == JobOfferStatuses.Published
            ? JobOfferStatuses.Draft : JobOfferStatuses.Published;
        if (status != requiredStatus)
            return (null, JobTransitionStatus.InvalidStatus);

        // Check the deadline after acquiring the row lock, using the publication timestamp.
        await using var command = new NpgsqlCommand($"""
            UPDATE portal.job_offers SET
                status = $3,
                published_at = CASE WHEN $3 = 'published' THEN statement_timestamp() ELSE published_at END
            WHERE company_id = $1 AND id = $2
              AND ($3 <> 'published' OR application_deadline IS NULL OR application_deadline > statement_timestamp())
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(targetStatus);

        JobOfferManagementResponse job;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return (null, JobTransitionStatus.DeadlineExpired);

            job = ReadJob(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return (job, JobTransitionStatus.Success);
    }

    private static async Task<Guid?> LockMemberAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, Guid companyId,
        CancellationToken cancellationToken)
    {
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
