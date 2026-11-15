using Npgsql;
using Portal.Contracts.Chat;

namespace Portal.Api.Chat;

public sealed class ChatRepository : IChatRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public ChatRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<ChatConversationResponse>> GetUserConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                c.id,
                c.application_id,
                a.job_offer_id,
                j.title AS job_title,
                c.company_id,
                comp.name AS company_name,
                c.candidate_id,
                (u.first_name || ' ' || u.last_name) AS candidate_name,
                lm.body AS last_message,
                lm.sent_at AS last_message_at,
                c.created_at
            FROM portal.conversations c
            JOIN portal.applications a ON a.id = c.application_id
            JOIN portal.job_offers j ON j.id = a.job_offer_id AND j.company_id = c.company_id
            JOIN portal.companies comp ON comp.id = c.company_id
            JOIN portal.users u ON u.id = c.candidate_id
            LEFT JOIN LATERAL (
                SELECT m.body, m.sent_at
                FROM portal.chat_messages m
                WHERE m.conversation_id = c.id
                ORDER BY m.sent_at DESC, m.id DESC
                LIMIT 1
            ) lm ON true
            WHERE c.candidate_id = @UserId
               OR EXISTS (
                   SELECT 1 FROM portal.company_members cm
                   WHERE cm.company_id = c.company_id
                     AND cm.user_id = @UserId
                     AND cm.is_active = true
               )
            ORDER BY COALESCE(lm.sent_at, c.created_at) DESC;
            """;

        var list = new List<ChatConversationResponse>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("UserId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadConversation(reader));
        }

        return list;
    }

    public async Task<ChatConversationResponse?> GetConversationByApplicationIdAsync(
        Guid applicationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                c.id,
                c.application_id,
                a.job_offer_id,
                j.title AS job_title,
                c.company_id,
                comp.name AS company_name,
                c.candidate_id,
                (u.first_name || ' ' || u.last_name) AS candidate_name,
                lm.body AS last_message,
                lm.sent_at AS last_message_at,
                c.created_at
            FROM portal.conversations c
            JOIN portal.applications a ON a.id = c.application_id
            JOIN portal.job_offers j ON j.id = a.job_offer_id AND j.company_id = c.company_id
            JOIN portal.companies comp ON comp.id = c.company_id
            JOIN portal.users u ON u.id = c.candidate_id
            LEFT JOIN LATERAL (
                SELECT m.body, m.sent_at
                FROM portal.chat_messages m
                WHERE m.conversation_id = c.id
                ORDER BY m.sent_at DESC, m.id DESC
                LIMIT 1
            ) lm ON true
            WHERE c.application_id = @ApplicationId
              AND (
                  c.candidate_id = @UserId
                  OR EXISTS (
                      SELECT 1 FROM portal.company_members cm
                      WHERE cm.company_id = c.company_id
                        AND cm.user_id = @UserId
                        AND cm.is_active = true
                  )
              );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ApplicationId", applicationId);
        cmd.Parameters.AddWithValue("UserId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadConversation(reader);
    }

    public async Task<bool> CanUserAccessConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM portal.conversations c
                JOIN portal.users u ON u.id = @UserId AND u.is_active = true
                WHERE c.id = @ConversationId
                  AND (
                      c.candidate_id = u.id
                      OR EXISTS (
                          SELECT 1 FROM portal.company_members m
                          WHERE m.company_id = c.company_id
                            AND m.user_id = u.id
                            AND m.is_active = true
                      )
                  )
            );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ConversationId", conversationId);
        cmd.Parameters.AddWithValue("UserId", userId);

        return (bool)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<ChatMessageResponse>?> GetConversationMessagesAsync(
        Guid conversationId,
        Guid userId,
        int limit = 100,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanUserAccessConversationAsync(conversationId, userId, cancellationToken))
        {
            return null;
        }

        var clampedLimit = Math.Clamp(limit, 1, 500);

        const string sql = """
            SELECT
                m.id,
                m.conversation_id,
                m.sender_user_id,
                (u.first_name || ' ' || u.last_name) AS sender_name,
                m.body,
                m.sent_at
            FROM portal.chat_messages m
            JOIN portal.users u ON u.id = m.sender_user_id
            WHERE m.conversation_id = @ConversationId
              AND (@Before IS NULL OR m.sent_at < @Before)
            ORDER BY m.sent_at ASC, m.id ASC
            LIMIT @Limit;
            """;

        var messages = new List<ChatMessageResponse>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ConversationId", conversationId);
        cmd.Parameters.AddWithValue("Before", (object?)before ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Limit", clampedLimit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ChatMessageResponse(
                Id: reader.GetGuid(0),
                ConversationId: reader.GetGuid(1),
                SenderId: reader.GetGuid(2),
                SenderName: reader.GetString(3),
                Content: reader.GetString(4),
                SentAt: reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return messages;
    }

    public async Task<ChatMessageResponse?> AddMessageAsync(
        Guid conversationId,
        Guid senderUserId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var trimmed = content?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (!await CanUserAccessConversationAsync(conversationId, senderUserId, cancellationToken))
        {
            return null;
        }

        const string sql = """
            WITH inserted AS (
                INSERT INTO portal.chat_messages (conversation_id, sender_user_id, body)
                VALUES (@ConversationId, @SenderUserId, @Body)
                RETURNING id, conversation_id, sender_user_id, body, sent_at
            )
            SELECT
                i.id,
                i.conversation_id,
                i.sender_user_id,
                (u.first_name || ' ' || u.last_name) AS sender_name,
                i.body,
                i.sent_at
            FROM inserted i
            JOIN portal.users u ON u.id = i.sender_user_id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ConversationId", conversationId);
        cmd.Parameters.AddWithValue("SenderUserId", senderUserId);
        cmd.Parameters.AddWithValue("Body", trimmed);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ChatMessageResponse(
            Id: reader.GetGuid(0),
            ConversationId: reader.GetGuid(1),
            SenderId: reader.GetGuid(2),
            SenderName: reader.GetString(3),
            Content: reader.GetString(4),
            SentAt: reader.GetFieldValue<DateTimeOffset>(5));
    }

    private static ChatConversationResponse ReadConversation(NpgsqlDataReader reader) =>
        new(
            Id: reader.GetGuid(0),
            ApplicationId: reader.GetGuid(1),
            JobOfferId: reader.GetGuid(2),
            JobTitle: reader.GetString(3),
            CompanyId: reader.GetGuid(4),
            CompanyName: reader.GetString(5),
            CandidateId: reader.GetGuid(6),
            CandidateName: reader.GetString(7),
            LastMessage: reader.IsDBNull(8) ? null : reader.GetString(8),
            LastMessageAt: reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(10));
}