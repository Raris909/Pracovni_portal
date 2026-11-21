using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Portal.Contracts.Chat;

namespace Portal.Api.Chat;

[ApiController]
[Authorize]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatRepository _chatRepository;
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatController(IChatRepository chatRepository, IHubContext<ChatHub> hubContext)
    {
        _chatRepository = chatRepository;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Lists all conversations accessible to the current user (candidate or active company HR member).
    /// </summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<ChatConversationResponse>>> GetConversations(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var conversations = await _chatRepository.GetUserConversationsAsync(userId, cancellationToken);
        return Ok(conversations);
    }

    /// <summary>
    /// Gets the conversation associated with a specific job application.
    /// </summary>
    [HttpGet("applications/{applicationId:guid}/conversation")]
    public async Task<ActionResult<ChatConversationResponse>> GetConversationByApplication(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var conversation = await _chatRepository.GetConversationByApplicationIdAsync(
            applicationId,
            userId,
            cancellationToken);

        if (conversation is null)
        {
            return NotFound(new { message = "Conversation not found or access denied." });
        }

        return Ok(conversation);
    }

    /// <summary>
    /// Returns the chronological message history for a specific conversation.
    /// </summary>
    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageResponse>>> GetMessages(
        Guid conversationId,
        [FromQuery] int limit = 100,
        [FromQuery] DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var messages = await _chatRepository.GetConversationMessagesAsync(
            conversationId,
            userId,
            limit,
            before,
            cancellationToken);

        if (messages is null)
        {
            return NotFound(new { message = "Conversation not found or access denied." });
        }

        return Ok(messages);
    }

    /// <summary>
    /// Persists a new chat message to PostgreSQL and broadcasts it via SignalR to the conversation group.
    /// </summary>
    [HttpPost("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<ChatMessageResponse>> SendMessage(
        Guid conversationId,
        [FromBody] SendChatMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var savedMessage = await _chatRepository.AddMessageAsync(
            conversationId,
            userId,
            request.Content,
            cancellationToken);

        if (savedMessage is null)
        {
            return NotFound(new { message = "Conversation not found or access denied." });
        }

        await _hubContext.Clients
            .Group(conversationId.ToString())
            .SendAsync("ReceiveMessage", savedMessage, cancellationToken);

        return Created(
            $"/api/chat/conversations/{conversationId}/messages/{savedMessage.Id}",
            savedMessage);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}