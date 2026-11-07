using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public sealed class InterviewSlotRepository(NpgsqlDataSource database) : IInterviewSlotRepository
{
    private const string Columns = """
        s.id, s.job_offer_id, s.company_id, s.stage_id, s.stage_kind, s.interviewer_member_id,
        s.starts_at, s.ends_at, s.location, s.meeting_url, s.is_active,
        EXISTS (SELECT 1 FROM portal.interview_bookings b WHERE b.slot_id = s.id AND b.status <> 'cancelled'),
        s.created_at, s.updated_at
        """;

    public async Task<IReadOnlyList<InterviewSlotResponse>?> ListAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"""
            SELECT {Columns} FROM portal.recruitment_stages stage
            JOIN portal.job_offers j ON j.id = stage.job_offer_id
            JOIN portal.company_members m ON m.company_id = j.company_id
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            LEFT JOIN portal.interview_slots s ON s.stage_id = stage.id
            WHERE j.company_id = $1 AND j.id = $2 AND stage.id = $3 AND stage.kind = 'interview'
              AND m.user_id = $4 AND m.is_active AND u.is_active AND r.name = 'company'
            ORDER BY s.starts_at, s.id
            """);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var slots = new List<InterviewSlotResponse>();
        if (!reader.IsDBNull(0))
        {
            do
            {
                slots.Add(ReadSlot(reader));
            } while (await reader.ReadAsync(cancellationToken));
        }
        return slots;
    }

    public async Task<(InterviewSlotResponse? Slot, InterviewSlotStatus Status)> CreateAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, UpsertInterviewSlotRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await AuthorizeAsync(connection, transaction, companyId, jobId, stageId, userId, cancellationToken))
            return (null, InterviewSlotStatus.StageNotFound);
        var interviewerId = await LockInterviewerAsync(connection, transaction, companyId, request.InterviewerMemberId, cancellationToken);
        if (interviewerId is null)
            return (null, InterviewSlotStatus.InterviewerNotFound);
        if (await HasOverlapAsync(connection, transaction, interviewerId.Value, Guid.Empty, request, cancellationToken))
            return (null, InterviewSlotStatus.Overlap);

        await using var command = new NpgsqlCommand("""
            INSERT INTO portal.interview_slots
                (company_id, job_offer_id, stage_id, interviewer_member_id, starts_at, ends_at, location, meeting_url, is_active)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9) RETURNING id
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(stageId);
        AddRequestParameters(command, request);
        try
        {
            var slotId = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
            var slot = await FindSlotAsync(connection, transaction, slotId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (slot, InterviewSlotStatus.Success);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            return (null, InterviewSlotStatus.Overlap);
        }
    }

    public async Task<(InterviewSlotResponse? Slot, InterviewSlotStatus Status)> UpdateAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, Guid slotId, UpsertInterviewSlotRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await AuthorizeAsync(connection, transaction, companyId, jobId, stageId, userId, cancellationToken))
            return (null, InterviewSlotStatus.StageNotFound);
        var interviewerId = await LockInterviewerAsync(connection, transaction, companyId, request.InterviewerMemberId, cancellationToken);
        if (interviewerId is null)
            return (null, InterviewSlotStatus.InterviewerNotFound);
        if (!await LockSlotAsync(connection, transaction, companyId, jobId, stageId, slotId, cancellationToken))
            return (null, InterviewSlotStatus.SlotNotFound);

        var existing = await FindSlotAsync(connection, transaction, slotId, cancellationToken);
        if (existing.IsBooked)
            return (null, InterviewSlotStatus.Booked);
        if (await HasOverlapAsync(connection, transaction, interviewerId.Value, slotId, request, cancellationToken))
            return (null, InterviewSlotStatus.Overlap);

        await using var command = new NpgsqlCommand("""
            UPDATE portal.interview_slots SET
                interviewer_member_id = $2, starts_at = $3, ends_at = $4, location = $5, meeting_url = $6, is_active = $7
            WHERE id = $1 AND (interviewer_member_id, starts_at, ends_at, location, meeting_url, is_active)
                IS DISTINCT FROM ($2, $3, $4, $5, $6, $7)
            """, connection, transaction);
        command.Parameters.AddWithValue(slotId);
        AddRequestParameters(command, request);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            var slot = await FindSlotAsync(connection, transaction, slotId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (slot, InterviewSlotStatus.Success);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            return (null, InterviewSlotStatus.Overlap);
        }
    }

    public async Task<InterviewSlotStatus> DeactivateAsync(
        Guid companyId, Guid jobId, Guid stageId, Guid userId, Guid slotId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await AuthorizeAsync(connection, transaction, companyId, jobId, stageId, userId, cancellationToken))
            return InterviewSlotStatus.StageNotFound;
        if (!await LockSlotAsync(connection, transaction, companyId, jobId, stageId, slotId, cancellationToken))
            return InterviewSlotStatus.SlotNotFound;
        var slot = await FindSlotAsync(connection, transaction, slotId, cancellationToken);
        if (slot.IsBooked)
            return InterviewSlotStatus.Booked;

        await using var command = new NpgsqlCommand("""
            UPDATE portal.interview_slots SET is_active = false WHERE id = $1 AND is_active
            """, connection, transaction);
        command.Parameters.AddWithValue(slotId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InterviewSlotStatus.Success;
    }

    private static async Task<bool> AuthorizeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid jobId, Guid stageId, Guid userId,
        CancellationToken cancellationToken)
    {
        await using var memberCommand = new NpgsqlCommand("""
            SELECT m.id FROM portal.company_members m
            JOIN portal.users u ON u.id = m.user_id JOIN portal.roles r ON r.id = u.role_id
            WHERE m.company_id = $1 AND m.user_id = $2 AND m.is_active AND u.is_active AND r.name = 'company'
            FOR SHARE OF m, u
            """, connection, transaction);
        memberCommand.Parameters.AddWithValue(companyId);
        memberCommand.Parameters.AddWithValue(userId);
        if (await memberCommand.ExecuteScalarAsync(cancellationToken) is null)
            return false;

        await using var jobCommand = new NpgsqlCommand("""
            SELECT id FROM portal.job_offers WHERE company_id = $1 AND id = $2 FOR SHARE
            """, connection, transaction);
        jobCommand.Parameters.AddWithValue(companyId);
        jobCommand.Parameters.AddWithValue(jobId);
        if (await jobCommand.ExecuteScalarAsync(cancellationToken) is null)
            return false;
        await using var stageCommand = new NpgsqlCommand("""
            SELECT id FROM portal.recruitment_stages WHERE job_offer_id = $1 AND id = $2 AND kind = 'interview'
            """, connection, transaction);
        stageCommand.Parameters.AddWithValue(jobId);
        stageCommand.Parameters.AddWithValue(stageId);
        return await stageCommand.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<Guid?> LockInterviewerAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid memberId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT u.id FROM portal.company_members m
            JOIN portal.users u ON u.id = m.user_id JOIN portal.roles r ON r.id = u.role_id
            WHERE m.company_id = $1 AND m.id = $2 AND m.is_active AND u.is_active AND r.name = 'company'
            FOR SHARE OF m, u
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId);
        var userId = (Guid?)await command.ExecuteScalarAsync(cancellationToken);
        if (userId is null)
            return null;

        // The SQL exclusion constraint covers one membership; this lock also covers the user's other companies.
        await using var lockCommand = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(hashtextextended('interview-slots:' || $1::text, 0))
            """, connection, transaction);
        lockCommand.Parameters.AddWithValue(userId.Value);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        return userId;
    }

    private static async Task<bool> HasOverlapAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid interviewerId, Guid slotId,
        UpsertInterviewSlotRequest request, CancellationToken cancellationToken)
    {
        if (!request.IsActive)
            return false;
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM portal.interview_slots s
                JOIN portal.company_members m ON m.id = s.interviewer_member_id
                WHERE m.user_id = $1 AND s.id <> $2 AND s.is_active AND s.starts_at < $4 AND s.ends_at > $3)
            """, connection, transaction);
        command.Parameters.AddWithValue(interviewerId);
        command.Parameters.AddWithValue(slotId);
        command.Parameters.AddWithValue(request.StartsAt.ToUniversalTime());
        command.Parameters.AddWithValue(request.EndsAt.ToUniversalTime());
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> LockSlotAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid jobId, Guid stageId, Guid slotId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id FROM portal.interview_slots
            WHERE company_id = $1 AND job_offer_id = $2 AND stage_id = $3 AND id = $4 FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(slotId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<InterviewSlotResponse> FindSlotAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid slotId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM portal.interview_slots s WHERE s.id = $1", connection, transaction);
        command.Parameters.AddWithValue(slotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSlot(reader);
    }

    private static void AddRequestParameters(NpgsqlCommand command, UpsertInterviewSlotRequest request)
    {
        command.Parameters.AddWithValue(request.InterviewerMemberId);
        command.Parameters.AddWithValue(request.StartsAt.ToUniversalTime());
        command.Parameters.AddWithValue(request.EndsAt.ToUniversalTime());
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Location ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.MeetingUrl ?? DBNull.Value);
        command.Parameters.AddWithValue(request.IsActive);
    }

    private static InterviewSlotResponse ReadSlot(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5),
        reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7),
        reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.GetBoolean(10), reader.GetBoolean(11), reader.GetFieldValue<DateTimeOffset>(12), reader.GetFieldValue<DateTimeOffset>(13));
}
