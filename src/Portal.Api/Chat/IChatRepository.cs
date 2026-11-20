using Portal.Contracts.Chat;

namespace Portal.Api.Chat;

public interface IChatRepository
{
    Task<IReadOnlyList<ChatConversationResponse>> GetUserConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ChatConversationResponse?> GetConversationByApplicationIdAsync(
        Guid applicationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> CanUserAccessConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageResponse>?> GetConversationMessagesAsync(
        Guid conversationId,
        Guid userId,
        int limit = 100,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    Task<ChatMessageResponse?> AddMessageAsync(
        Guid conversationId,
        Guid senderUserId,
        string content,
        CancellationToken cancellationToken = default);
}