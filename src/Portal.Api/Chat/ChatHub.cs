using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Portal.Api.Chat;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IChatRepository _chatRepository;

    public ChatHub(IChatRepository chatRepository)
    {
        _chatRepository = chatRepository;
    }

    public async Task JoinConversation(string conversationId)
    {
        if (!Guid.TryParse(conversationId, out var convGuid) || !TryGetUserId(out var userId))
        {
            throw new HubException("Unauthorized or invalid conversation ID.");
        }

        if (!await _chatRepository.CanUserAccessConversationAsync(convGuid, userId))
        {
            throw new HubException("Access to this conversation is denied.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, convGuid.ToString());
    }

    public async Task LeaveConversation(string conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId);
    }

    public async Task SendMessage(string conversationId, string content)
    {
        if (!Guid.TryParse(conversationId, out var convGuid) || !TryGetUserId(out var userId))
        {
            throw new HubException("Unauthorized or invalid conversation ID.");
        }

        var saved = await _chatRepository.AddMessageAsync(convGuid, userId, content);
        if (saved is null)
        {
            throw new HubException("Unable to send message to this conversation.");
        }

        await Clients.Group(convGuid.ToString()).SendAsync("ReceiveMessage", saved);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}