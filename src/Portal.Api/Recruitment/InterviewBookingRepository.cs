using Npgsql;
using Portal.Contracts.Applications;
using Portal.Contracts.Recruitment;

namespace Portal.Api.Recruitment;

public sealed class InterviewBookingRepository(NpgsqlDataSource database) : IInterviewBookingRepository
{
    private const string BookingColumns = """
        b.id, b.slot_id, b.application_id, b.stage_id, b.job_offer_id, b.company_id, b.status,
        s.starts_at, s.ends_at, s.location, s.meeting_url, b.booked_at, b.cancelled_at, b.updated_at
        """;

    public async Task<IReadOnlyList<InterviewSlotResponse>?> ListAvailableSlotsAsync(
        Guid applicationId, Guid stageId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT s.id, s.job_offer_id, s.company_id, s.stage_id, s.stage_kind, s.interviewer_member_id,
                s.starts_at, s.ends_at, s.location, s.meeting_url, s.is_active, s.created_at, s.updated_at
            FROM portal.applications app
            JOIN portal.application_stages a ON a.application_id = app.id
            JOIN portal.recruitment_stages stage ON stage.id = a.stage_id AND stage.kind = 'interview'
            JOIN portal.users u ON u.id = app.candidate_id JOIN portal.roles r ON r.id = u.role_id
            LEFT JOIN (
                portal.interview_slots s
                JOIN portal.company_members m ON m.id = s.interviewer_member_id AND m.is_active
                JOIN portal.users hr ON hr.id = m.user_id AND hr.is_active
                JOIN portal.roles hr_role ON hr_role.id = hr.role_id AND hr_role.name = 'company'
            ) ON s.job_offer_id = app.job_offer_id AND s.stage_id = a.stage_id
                AND s.is_active AND s.starts_at > statement_timestamp()
                AND app.status IN ('submitted', 'in_progress') AND a.status IN ('pending', 'in_progress')
                AND NOT EXISTS (SELECT 1 FROM portal.interview_bookings b
                    WHERE b.application_id = app.id AND b.stage_id = a.stage_id AND b.status <> 'cancelled')
                AND NOT EXISTS (SELECT 1 FROM portal.interview_bookings b WHERE b.slot_id = s.id AND b.status <> 'cancelled')
                AND NOT EXISTS (
                    SELECT 1 FROM portal.interview_bookings b
                    JOIN portal.applications other_app ON other_app.id = b.application_id
                    JOIN portal.interview_slots other_slot ON other_slot.id = b.slot_id
                    WHERE other_app.candidate_id = app.candidate_id AND b.status <> 'cancelled'
                        AND other_slot.starts_at < s.ends_at AND other_slot.ends_at > s.starts_at)
            WHERE app.id = $1 AND a.stage_id = $2 AND app.candidate_id = $3 AND u.is_active AND r.name = 'candidate'
            ORDER BY s.starts_at, s.id
            """);
        command.Parameters.AddWithValue(applicationId);
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
                slots.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4),
                    reader.GetGuid(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetBoolean(10), false, reader.GetFieldValue<DateTimeOffset>(11), reader.GetFieldValue<DateTimeOffset>(12)));
            } while (await reader.ReadAsync(cancellationToken));
        }
        return slots;
    }

    public async Task<IReadOnlyList<InterviewBookingResponse>?> ListBookingsAsync(
        Guid applicationId, Guid stageId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"""
            SELECT {BookingColumns} FROM portal.applications app
            JOIN portal.application_stages a ON a.application_id = app.id
            JOIN portal.recruitment_stages stage ON stage.id = a.stage_id AND stage.kind = 'interview'
            JOIN portal.users u ON u.id = app.candidate_id JOIN portal.roles r ON r.id = u.role_id
            LEFT JOIN portal.interview_bookings b ON b.application_id = app.id AND b.stage_id = a.stage_id
            LEFT JOIN portal.interview_slots s ON s.id = b.slot_id
            WHERE app.id = $1 AND a.stage_id = $2 AND app.candidate_id = $3 AND u.is_active AND r.name = 'candidate'
            ORDER BY b.booked_at, b.id
            """);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var bookings = new List<InterviewBookingResponse>();
        if (!reader.IsDBNull(0))
        {
            do
            {
                bookings.Add(ReadBooking(reader));
            } while (await reader.ReadAsync(cancellationToken));
        }
        return bookings;
    }

    public async Task<(InterviewBookingResponse? Booking, InterviewBookingStatus Status)> CreateAsync(
        Guid applicationId, Guid stageId, Guid userId, CreateInterviewBookingRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var context = await LockContextAsync(connection, transaction, applicationId, stageId, userId, cancellationToken);
        if (context is null)
            return (null, InterviewBookingStatus.StageNotFound);
        if (context.Value.ApplicationStatus is not (ApplicationStatuses.Submitted or ApplicationStatuses.InProgress)
            || context.Value.StageStatus is not (ApplicationStageStatuses.Pending or ApplicationStageStatuses.InProgress))
            return (null, InterviewBookingStatus.Unavailable);
        if (!await LockSlotAsync(connection, transaction, context.Value.JobId, stageId, request.SlotId, cancellationToken))
            return (null, InterviewBookingStatus.SlotNotFound);

        // Read availability after acquiring the slot lock, including any R05 change committed while waiting.
        await using var slotCommand = new NpgsqlCommand("""
            SELECT s.starts_at, s.ends_at, s.is_active AND s.starts_at > clock_timestamp()
            FROM portal.interview_slots s
            JOIN portal.company_members m ON m.id = s.interviewer_member_id
            JOIN portal.users hr ON hr.id = m.user_id JOIN portal.roles r ON r.id = hr.role_id
            WHERE s.id = $1 AND m.is_active AND hr.is_active AND r.name = 'company'
            FOR SHARE OF m, hr
            """, connection, transaction);
        slotCommand.Parameters.AddWithValue(request.SlotId);
        DateTimeOffset startsAt, endsAt;
        await using (var reader = await slotCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(2))
                return (null, InterviewBookingStatus.Unavailable);
            startsAt = reader.GetFieldValue<DateTimeOffset>(0);
            endsAt = reader.GetFieldValue<DateTimeOffset>(1);
        }

        await using var conflictCommand = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM portal.interview_bookings b
                WHERE b.status <> 'cancelled' AND (b.slot_id = $1 OR (b.application_id = $2 AND b.stage_id = $3)))
            OR EXISTS (
                SELECT 1 FROM portal.interview_bookings b
                JOIN portal.applications app ON app.id = b.application_id
                JOIN portal.interview_slots s ON s.id = b.slot_id
                WHERE app.candidate_id = $4 AND b.status <> 'cancelled' AND s.starts_at < $6 AND s.ends_at > $5)
            """, connection, transaction);
        conflictCommand.Parameters.AddWithValue(request.SlotId);
        conflictCommand.Parameters.AddWithValue(applicationId);
        conflictCommand.Parameters.AddWithValue(stageId);
        conflictCommand.Parameters.AddWithValue(userId);
        conflictCommand.Parameters.AddWithValue(startsAt);
        conflictCommand.Parameters.AddWithValue(endsAt);
        if ((bool)(await conflictCommand.ExecuteScalarAsync(cancellationToken))!)
            return (null, InterviewBookingStatus.Unavailable);

        await using var command = new NpgsqlCommand("""
            INSERT INTO portal.interview_bookings (slot_id, application_id, stage_id, job_offer_id, company_id, booked_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, statement_timestamp(), statement_timestamp()) RETURNING id
            """, connection, transaction);
        command.Parameters.AddWithValue(request.SlotId);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(context.Value.JobId);
        command.Parameters.AddWithValue(context.Value.CompanyId);
        try
        {
            var id = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
            var booking = await FindBookingAsync(connection, transaction, applicationId, stageId, id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (booking, InterviewBookingStatus.Success);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName is "interview_bookings_slot_unique" or "interview_bookings_application_stage_unique")
        {
            return (null, InterviewBookingStatus.Unavailable);
        }
    }

    public async Task<InterviewBookingStatus> CancelAsync(
        Guid applicationId, Guid stageId, Guid userId, Guid bookingId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var context = await LockContextAsync(connection, transaction, applicationId, stageId, userId, cancellationToken);
        if (context is null)
            return InterviewBookingStatus.StageNotFound;
        var booking = await FindBookingAsync(connection, transaction, applicationId, stageId, bookingId, cancellationToken);
        if (booking is null)
            return InterviewBookingStatus.BookingNotFound;
        if (booking.Status == InterviewBookingStatuses.Cancelled)
            return InterviewBookingStatus.Success;
        await LockSlotAsync(connection, transaction, context.Value.JobId, stageId, booking.SlotId, cancellationToken);

        await using var command = new NpgsqlCommand("""
            UPDATE portal.interview_bookings b SET status = 'cancelled', cancelled_at = clock_timestamp()
            WHERE b.id = $1 AND b.status = 'confirmed'
                AND EXISTS (SELECT 1 FROM portal.interview_slots s WHERE s.id = b.slot_id AND s.starts_at > clock_timestamp())
            """, connection, transaction);
        command.Parameters.AddWithValue(bookingId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            return InterviewBookingStatus.Unavailable;
        await transaction.CommitAsync(cancellationToken);
        return InterviewBookingStatus.Success;
    }

    private static async Task<(Guid JobId, Guid CompanyId, string ApplicationStatus, string StageStatus)?> LockContextAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid applicationId, Guid stageId, Guid userId,
        CancellationToken cancellationToken)
    {
        // Serialize bookings across all applications of this candidate to prevent overlapping interviews.
        await using var userCommand = new NpgsqlCommand("""
            SELECT u.id FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE u.id = $1 AND u.is_active AND r.name = 'candidate' FOR UPDATE OF u
            """, connection, transaction);
        userCommand.Parameters.AddWithValue(userId);
        if (await userCommand.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        await using var command = new NpgsqlCommand("""
            SELECT app.job_offer_id, app.company_id, app.status, a.status
            FROM portal.applications app
            JOIN portal.job_offers j ON j.id = app.job_offer_id
            JOIN portal.application_stages a ON a.application_id = app.id
            JOIN portal.recruitment_stages stage ON stage.id = a.stage_id AND stage.kind = 'interview'
            WHERE app.id = $1 AND a.stage_id = $2 AND app.candidate_id = $3
            FOR SHARE OF j FOR UPDATE OF app, a
            """, connection, transaction);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3)) : null;
    }

    private static async Task<bool> LockSlotAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid jobId, Guid stageId, Guid slotId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id FROM portal.interview_slots WHERE job_offer_id = $1 AND stage_id = $2 AND id = $3 FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue(jobId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(slotId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<InterviewBookingResponse?> FindBookingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid applicationId, Guid stageId, Guid bookingId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT {BookingColumns} FROM portal.interview_bookings b
            JOIN portal.interview_slots s ON s.id = b.slot_id
            WHERE b.application_id = $1 AND b.stage_id = $2 AND b.id = $3
            """, connection, transaction);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(stageId);
        command.Parameters.AddWithValue(bookingId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBooking(reader) : null;
    }

    private static InterviewBookingResponse ReadBooking(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5),
        reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
        reader.GetFieldValue<DateTimeOffset>(13));
}
