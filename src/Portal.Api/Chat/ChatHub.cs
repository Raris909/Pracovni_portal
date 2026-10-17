using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Portal.Api.Chat;

[Authorize]
public sealed class ChatHub : Hub
{
}
