using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Applications;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public sealed class CandidateEvaluationRepository(NpgsqlDataSource database) : ICandidateEvaluationRepository
{
    private const string Columns = """
        id, application_id, stage_id, company_id, author_member_id, score, note, created_at, updated_at
        """;

    public async Task<IReadOnlyList<CandidateEvaluationResponse>?> ListAsync(
        Guid companyId, Guid applicationId, Guid stageId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT e.id, e.application_id, e.stage_id, e.company_id, e.author_member_id,
                   e.score, e.note, e.created_at, e.updated_at
            FROM portal.application_stages a
            JOIN portal.company_members m ON m.company_id = a.company_id
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            LEFT JOIN portal.candidate_evaluations e
                ON e.application_id = a.application_id AND e.stage_id = a.stage_id AND e.company_id = a.company_id
            WHERE a.company_id = $1 AND a.application_id = $2 AND a.stage_id = $3
              AND m.user_id = $4 AND m.is_active AND u.is_active AND r.name = 'company'
            ORDER BY e.created_at, e.id
            """);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var evaluations = new List<CandidateEvaluationResponse>();
        if (!reader.IsDBNull(0))
        {
            do
            {
                evaluations.Add(ReadEvaluation(reader));
            } while (await reader.ReadAsync(cancellationToken));
        }
        return evaluations;
    }

    public async Task<(CandidateEvaluationResponse? Evaluation, CandidateEvaluationStatus Status)> CreateAsync(
        Guid companyId, Guid applicationId, Guid stageId, Guid userId, UpsertCandidateEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (memberId, status) = await AuthorizeWriteAsync(
            connection, transaction, companyId, applicationId, stageId, userId, cancellationToken);
        if (status != CandidateEvaluationStatus.Success)
            return (null, status);

        await using var command = new NpgsqlCommand($"""
            INSERT INTO portal.candidate_evaluations
                (application_id, stage_id, company_id, author_member_id, score, note)
            VALUES ($1, $2, $3, $4, $5, $6) RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId!.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Smallint, (object?)request.Score ?? DBNull.Value);
        command.Parameters.AddWithValue(request.Note.Trim());

        CandidateEvaluationResponse evaluation;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            evaluation = ReadEvaluation(reader);
        }
        await transaction.CommitAsync(cancellationToken);
        return (evaluation, CandidateEvaluationStatus.Success);
    }

    public async Task<(CandidateEvaluationResponse? Evaluation, CandidateEvaluationStatus Status)> UpdateAsync(
        Guid companyId, Guid applicationId, Guid stageId, Guid userId, Guid evaluationId,
        UpsertCandidateEvaluationRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (memberId, status) = await AuthorizeWriteAsync(
            connection, transaction, companyId, applicationId, stageId, userId, cancellationToken);
        if (status != CandidateEvaluationStatus.Success)
            return (null, status);

        await using var findCommand = new NpgsqlCommand($"""
            SELECT {Columns} FROM portal.candidate_evaluations
            WHERE company_id = $1 AND application_id = $2 AND stage_id = $3 AND id = $4
            """, connection, transaction);
        findCommand.Parameters.AddWithValue(companyId);
        findCommand.Parameters.AddWithValue(applicationId);
        findCommand.Parameters.AddWithValue(stageId);
        findCommand.Parameters.AddWithValue(evaluationId);
        CandidateEvaluationResponse existing;
        await using (var reader = await findCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return (null, CandidateEvaluationStatus.EvaluationNotFound);
            existing = ReadEvaluation(reader);
        }
        if (existing.AuthorMemberId != memberId)
            return (null, CandidateEvaluationStatus.Forbidden);

        var note = request.Note.Trim();
        if (existing.Score == request.Score && existing.Note == note)
            return (existing, CandidateEvaluationStatus.Success);

        await using var command = new NpgsqlCommand($"""
            UPDATE portal.candidate_evaluations SET score = $5, note = $6
            WHERE company_id = $1 AND application_id = $2 AND stage_id = $3 AND id = $4
            RETURNING {Columns}
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(evaluationId);
        command.Parameters.AddWithValue(NpgsqlDbType.Smallint, (object?)request.Score ?? DBNull.Value);
        command.Parameters.AddWithValue(note);

        CandidateEvaluationResponse evaluation;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            evaluation = ReadEvaluation(reader);
        }
        await transaction.CommitAsync(cancellationToken);
        return (evaluation, CandidateEvaluationStatus.Success);
    }

    private static async Task<(Guid? MemberId, CandidateEvaluationStatus Status)> AuthorizeWriteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid applicationId, Guid stageId,
        Guid userId, CancellationToken cancellationToken)
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
        var memberId = (Guid?)await memberCommand.ExecuteScalarAsync(cancellationToken);
        if (memberId is null)
            return (null, CandidateEvaluationStatus.StageNotFound);

        await using var applicationCommand = new NpgsqlCommand("""
            SELECT status FROM portal.applications WHERE company_id = $1 AND id = $2 FOR UPDATE
            """, connection, transaction);
        applicationCommand.Parameters.AddWithValue(companyId);
        applicationCommand.Parameters.AddWithValue(applicationId);
        var applicationStatus = (string?)await applicationCommand.ExecuteScalarAsync(cancellationToken);
        if (applicationStatus is null)
            return (null, CandidateEvaluationStatus.StageNotFound);
        if (applicationStatus is ApplicationStatuses.Accepted or ApplicationStatuses.Rejected or ApplicationStatuses.Withdrawn)
            return (null, CandidateEvaluationStatus.ApplicationClosed);

        await using var stageCommand = new NpgsqlCommand("""
            SELECT stage_id FROM portal.application_stages
            WHERE company_id = $1 AND application_id = $2 AND stage_id = $3 FOR SHARE
            """, connection, transaction);
        stageCommand.Parameters.AddWithValue(companyId);
        stageCommand.Parameters.AddWithValue(applicationId);
        stageCommand.Parameters.AddWithValue(stageId);
        if (await stageCommand.ExecuteScalarAsync(cancellationToken) is null)
            return (null, CandidateEvaluationStatus.StageNotFound);

        return (memberId, CandidateEvaluationStatus.Success);
    }

    private static CandidateEvaluationResponse ReadEvaluation(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4),
        reader.IsDBNull(5) ? null : reader.GetInt16(5), reader.GetString(6),
        reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8));
}
