using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public sealed class RecruitmentStageRepository(NpgsqlDataSource database) : IRecruitmentStageRepository
{
    private const string Columns = "id, job_offer_id, name, kind, position, description";
    private const string PositionConstraint = "recruitment_stages_job_offer_id_position_key";

    public async Task<IReadOnlyList<RecruitmentStageResponse>?> ListAsync(
        Guid companyId, Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT s.id, s.job_offer_id, s.name, s.kind, s.position, s.description
            FROM portal.job_offers j
            JOIN portal.company_members m ON m.company_id = j.company_id
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            LEFT JOIN portal.recruitment_stages s ON s.job_offer_id = j.id
            WHERE j.company_id = $1 AND j.id = $2 AND m.user_id = $3
              AND m.is_active AND u.is_active AND r.name = 'company'
            ORDER BY s.position
            """);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var stages = new List<RecruitmentStageResponse>();
        if (!reader.IsDBNull(0))
        {
            do
            {
                stages.Add(ReadStage(reader));
            } while (await reader.ReadAsync(cancellationToken));
        }
        return stages;
    }

    public async Task<(RecruitmentStageResponse? Stage, RecruitmentStageStatus Status)> CreateAsync(
        Guid companyId, Guid jobId, Guid userId, UpsertRecruitmentStageRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockJobAsync(connection, transaction, companyId, jobId, userId, cancellationToken))
            return (null, RecruitmentStageStatus.JobNotFound);

        await using var command = new NpgsqlCommand($"""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position, description)
            VALUES ($1, $2, $3, $4, $5) RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(jobId);
        AddRequestParameters(command, request);

        try
        {
            RecruitmentStageResponse stage;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                stage = ReadStage(reader);
            }
            await transaction.CommitAsync(cancellationToken);
            return (stage, RecruitmentStageStatus.Success);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == PositionConstraint)
        {
            return (null, RecruitmentStageStatus.PositionConflict);
        }
    }

    public async Task<(RecruitmentStageResponse? Stage, RecruitmentStageStatus Status)> UpdateAsync(
        Guid companyId, Guid jobId, Guid userId, Guid stageId, UpsertRecruitmentStageRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockJobAsync(connection, transaction, companyId, jobId, userId, cancellationToken))
            return (null, RecruitmentStageStatus.JobNotFound);

        await using var stageCommand = new NpgsqlCommand("""
            SELECT kind FROM portal.recruitment_stages WHERE job_offer_id = $1 AND id = $2 FOR UPDATE
            """, connection, transaction);
        stageCommand.Parameters.AddWithValue(jobId);
        stageCommand.Parameters.AddWithValue(stageId);
        var kind = (string?)await stageCommand.ExecuteScalarAsync(cancellationToken);
        if (kind is null)
            return (null, RecruitmentStageStatus.StageNotFound);

        if (kind != request.Kind)
        {
            await using var usageCommand = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM portal.application_stages WHERE stage_id = $1)
                    OR EXISTS (SELECT 1 FROM portal.interview_slots WHERE stage_id = $1)
                """, connection, transaction);
            usageCommand.Parameters.AddWithValue(stageId);
            if ((bool)(await usageCommand.ExecuteScalarAsync(cancellationToken))!)
                return (null, RecruitmentStageStatus.StageInUse);
        }

        await using var command = new NpgsqlCommand($"""
            UPDATE portal.recruitment_stages SET name = $3, kind = $4, position = $5, description = $6
            WHERE job_offer_id = $1 AND id = $2 RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(stageId);
        AddRequestParameters(command, request);

        try
        {
            RecruitmentStageResponse stage;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                stage = ReadStage(reader);
            }
            await transaction.CommitAsync(cancellationToken);
            return (stage, RecruitmentStageStatus.Success);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == PositionConstraint)
        {
            return (null, RecruitmentStageStatus.PositionConflict);
        }
    }

    public async Task<RecruitmentStageStatus> DeleteAsync(
        Guid companyId, Guid jobId, Guid userId, Guid stageId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockJobAsync(connection, transaction, companyId, jobId, userId, cancellationToken))
            return RecruitmentStageStatus.JobNotFound;

        await using var command = new NpgsqlCommand("""
            DELETE FROM portal.recruitment_stages WHERE job_offer_id = $1 AND id = $2
            """, connection, transaction);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(stageId);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                return RecruitmentStageStatus.StageNotFound;

            await transaction.CommitAsync(cancellationToken);
            return RecruitmentStageStatus.Success;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return RecruitmentStageStatus.StageInUse;
        }
    }

    public async Task<(IReadOnlyList<RecruitmentStageResponse>? Stages, RecruitmentStageStatus Status)> ReorderAsync(
        Guid companyId, Guid jobId, Guid userId, ReorderRecruitmentStagesRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockJobAsync(connection, transaction, companyId, jobId, userId, cancellationToken))
            return (null, RecruitmentStageStatus.JobNotFound);

        await using var listCommand = new NpgsqlCommand("""
            SELECT id FROM portal.recruitment_stages WHERE job_offer_id = $1
            """, connection, transaction);
        listCommand.Parameters.AddWithValue(jobId);
        var stageIds = new HashSet<Guid>();
        await using (var reader = await listCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                stageIds.Add(reader.GetGuid(0));
        }

        if (!stageIds.SetEquals(request.OrderedStageIds))
            return (null, RecruitmentStageStatus.InvalidOrder);

        // Defer the unique position constraint so swaps never collide with the old positions.
        await using var deferCommand = new NpgsqlCommand(
            $"SET CONSTRAINTS portal.{PositionConstraint} DEFERRED", connection, transaction);
        await deferCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var command = new NpgsqlCommand("""
            UPDATE portal.recruitment_stages s SET position = ordered.position::integer
            FROM unnest($2::uuid[]) WITH ORDINALITY AS ordered(id, position)
            WHERE s.job_offer_id = $1 AND s.id = ordered.id
            RETURNING s.id, s.job_offer_id, s.name, s.kind, s.position, s.description
            """, connection, transaction);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(request.OrderedStageIds.ToArray());
        var stages = new List<RecruitmentStageResponse>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                stages.Add(ReadStage(reader));
        }

        await transaction.CommitAsync(cancellationToken);
        return (stages.OrderBy(stage => stage.Position).ToArray(), RecruitmentStageStatus.Success);
    }

    private static async Task<bool> LockJobAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid jobId, Guid userId,
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
            return false;

        // Serialize changes to the stage definitions and ordering of this offer.
        await using var jobCommand = new NpgsqlCommand("""
            SELECT id FROM portal.job_offers WHERE company_id = $1 AND id = $2 FOR UPDATE
            """, connection, transaction);
        jobCommand.Parameters.AddWithValue(companyId);
        jobCommand.Parameters.AddWithValue(jobId);
        return await jobCommand.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static void AddRequestParameters(NpgsqlCommand command, UpsertRecruitmentStageRequest request)
    {
        command.Parameters.AddWithValue(request.Name.Trim());
        command.Parameters.AddWithValue(request.Kind);
        command.Parameters.AddWithValue(request.Position);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Description ?? DBNull.Value);
    }

    private static RecruitmentStageResponse ReadStage(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetString(5));
}
