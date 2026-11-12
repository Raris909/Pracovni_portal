using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Applications;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public sealed class ApplicationStageRepository(NpgsqlDataSource database) : IApplicationStageRepository
{
    private const string Columns = """
        a.application_id, a.stage_id, a.job_offer_id, a.company_id, s.name, s.kind, s.position,
        a.status, a.started_at, a.completed_at, a.updated_at
        """;

    public async Task<IReadOnlyList<ApplicationStageResponse>?> ListAsync(
        Guid companyId, Guid applicationId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"""
            SELECT {Columns}
            FROM portal.applications app
            JOIN portal.company_members m ON m.company_id = app.company_id
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            LEFT JOIN portal.application_stages a ON a.application_id = app.id
            LEFT JOIN portal.recruitment_stages s ON s.id = a.stage_id
            WHERE app.company_id = $1 AND app.id = $2 AND m.user_id = $3
              AND m.is_active AND u.is_active AND r.name = 'company'
            ORDER BY s.position
            """);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var stages = new List<ApplicationStageResponse>();
        if (!reader.IsDBNull(0))
        {
            do
            {
                stages.Add(ReadStage(reader));
            } while (await reader.ReadAsync(cancellationToken));
        }
        return stages;
    }

    public async Task<(ApplicationStageResponse? Stage, ApplicationStageStatus Status)> AssignAsync(
        Guid companyId, Guid applicationId, Guid userId, Guid stageId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var application = await LockApplicationAsync(connection, transaction, companyId, applicationId, userId, cancellationToken);
        if (application is null)
            return (null, ApplicationStageStatus.ApplicationNotFound);
        if (IsClosed(application.Value.Status))
            return (null, ApplicationStageStatus.ApplicationClosed);

        var existing = await FindStageAsync(connection, transaction, applicationId, stageId, cancellationToken);
        if (existing is not null)
            return (existing, ApplicationStageStatus.Success);

        await using var command = new NpgsqlCommand("""
            INSERT INTO portal.application_stages (application_id, stage_id, job_offer_id, company_id)
            SELECT $1, id, job_offer_id, $3 FROM portal.recruitment_stages
            WHERE job_offer_id = $2 AND id = $4
            """, connection, transaction);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(application.Value.JobId);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(stageId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            return (null, ApplicationStageStatus.StageNotFound);

        var stage = await FindStageAsync(connection, transaction, applicationId, stageId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (stage, ApplicationStageStatus.Created);
    }

    public async Task<(ApplicationStageResponse? Stage, ApplicationStageStatus Status)> UpdateStatusAsync(
        Guid companyId, Guid applicationId, Guid userId, Guid stageId, UpdateApplicationStageStatusRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var application = await LockApplicationAsync(connection, transaction, companyId, applicationId, userId, cancellationToken);
        if (application is null)
            return (null, ApplicationStageStatus.ApplicationNotFound);
        if (IsClosed(application.Value.Status))
            return (null, ApplicationStageStatus.ApplicationClosed);

        var stage = await FindStageAsync(connection, transaction, applicationId, stageId, cancellationToken);
        if (stage is null)
            return (null, ApplicationStageStatus.StageNotFound);

        var allowed = stage.Status == request.Status
            || stage.Status == ApplicationStageStatuses.Pending
            || (stage.Status == ApplicationStageStatuses.InProgress && ApplicationStageStatuses.CompletedStatuses.Contains(request.Status));
        if (!allowed)
            return (null, ApplicationStageStatus.InvalidTransition);

        await using var command = new NpgsqlCommand("""
            UPDATE portal.application_stages
            SET status = $3, started_at = $4, completed_at = $5
            WHERE application_id = $1 AND stage_id = $2
              AND (status, started_at, completed_at) IS DISTINCT FROM ($3, $4, $5)
            """, connection, transaction);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(request.Status);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)request.StartedAt?.ToUniversalTime() ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)request.CompletedAt?.ToUniversalTime() ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        stage = await FindStageAsync(connection, transaction, applicationId, stageId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (stage, ApplicationStageStatus.Success);
    }

    private static async Task<(Guid JobId, string Status)?> LockApplicationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid applicationId, Guid userId,
        CancellationToken cancellationToken)
    {
        await using var memberCommand = new NpgsqlCommand("""
            SELECT m.id FROM portal.company_members m
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            WHERE m.company_id = $1 AND m.user_id = $2 AND m.is_active
              AND u.is_active AND r.name = 'company'
            FOR SHARE OF m, u
            """, connection, transaction);
        memberCommand.Parameters.AddWithValue(companyId);
        memberCommand.Parameters.AddWithValue(userId);
        if (await memberCommand.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        await using var command = new NpgsqlCommand("""
            SELECT app.job_offer_id, app.status FROM portal.applications app
            JOIN portal.job_offers j ON j.id = app.job_offer_id AND j.company_id = app.company_id
            WHERE app.company_id = $1 AND app.id = $2
            FOR SHARE OF j FOR UPDATE OF app
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(applicationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetGuid(0), reader.GetString(1)) : null;
    }

    private static bool IsClosed(string status) => status is
        ApplicationStatuses.Accepted or ApplicationStatuses.Rejected or ApplicationStatuses.Withdrawn;

    private static async Task<ApplicationStageResponse?> FindStageAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid applicationId, Guid stageId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT {Columns} FROM portal.application_stages a
            JOIN portal.recruitment_stages s ON s.id = a.stage_id
            WHERE a.application_id = $1 AND a.stage_id = $2
            """, connection, transaction);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadStage(reader) : null;
    }

    private static ApplicationStageResponse ReadStage(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4),
        reader.GetString(5), reader.GetInt32(6), reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), reader.GetFieldValue<DateTimeOffset>(10));
}
