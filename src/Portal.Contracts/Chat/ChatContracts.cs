using System.ComponentModel.DataAnnotations;

namespace Portal.Contracts.Chat;

/// <summary>
/// Summary of a chat conversation (<c>portal.conversations</c>) for a candidate or company HR member.
/// </summary>
public sealed record ChatConversationResponse(
    Guid Id,
    Guid ApplicationId,
    Guid JobOfferId,
    string JobTitle,
    Guid CompanyId,
    string CompanyName,
    Guid CandidateId,
    string CandidateName,
    string? LastMessage,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents a single chat message (<c>portal.chat_messages</c>).
/// Exposes both <c>Content</c> (used by Flutter mobile client) and <c>Body</c> (SQL column name).
/// </summary>
public sealed record ChatMessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string Content,
    DateTimeOffset SentAt)
{
    public string Body => Content;
}

/// <summary>
/// Request to send a new message in a conversation via REST API.
/// </summary>
public sealed record SendChatMessageRequest(
    [property: Required, MinLength(1)] string Content) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Content))
        {
            yield return new ValidationResult(
                "Message content cannot be empty or whitespace.",
                [nameof(Content)]);
        }
    }
}