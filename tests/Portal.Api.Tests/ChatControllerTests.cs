using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Portal.Api.Chat;
using Portal.Contracts.Auth;
using Portal.Contracts.Chat;
using Xunit;

namespace Portal.Api.Tests;

public sealed class ChatControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly Mock<IChatRepository> _repoMock = new();
    private readonly Mock<IHubContext<ChatHub>> _hubContextMock = new();
    private readonly Mock<IHubClients> _hubClientsMock = new();
    private readonly Mock<IClientProxy> _clientProxyMock = new();

    public ChatControllerTests()
    {
        _hubClientsMock
            .Setup(c => c.Group(It.IsAny<string>()))
            .Returns(_clientProxyMock.Object);

        _hubContextMock
            .SetupGet(h => h.Clients)
            .Returns(_hubClientsMock.Object);
    }

    private ChatController CreateController(Guid? userId = null)
    {
        var controller = new ChatController(_repoMock.Object, _hubContextMock.Object);
        var claims = new List<Claim>();
        var effectiveId = userId ?? _userId;
        if (effectiveId != Guid.Empty)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, effectiveId.ToString()));
            claims.Add(new Claim(ClaimTypes.Role, UserRoles.Candidate));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };

        return controller;
    }

    [Fact]
    public async Task GetConversations_ReturnsUserConversations()
    {
        var now = DateTimeOffset.UtcNow;
        var expected = new List<ChatConversationResponse>
        {
            new(
                _conversationId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                ".NET Developer",
                Guid.NewGuid(),
                "Acme s.r.o.",
                _userId,
                "Jan Novák",
                "Dobrý den, děkujeme za přihlášku.",
                now,
                now.AddHours(-1))
        };

        _repoMock
            .Setup(r => r.GetUserConversationsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController();
        var result = await controller.GetConversations();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<ChatConversationResponse>>(okResult.Value);
        Assert.Single(items);
        Assert.Equal("Acme s.r.o.", items[0].CompanyName);
    }

    [Fact]
    public async Task GetMessages_AccessibleConversation_ReturnsMessageHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var expected = new List<ChatMessageResponse>
        {
            new(Guid.NewGuid(), _conversationId, _userId, "Jan Novák", "Dobrý den!", now)
        };

        _repoMock
            .Setup(r => r.GetConversationMessagesAsync(_conversationId, _userId, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController();
        var result = await controller.GetMessages(_conversationId);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var messages = Assert.IsAssignableFrom<IReadOnlyList<ChatMessageResponse>>(okResult.Value);
        Assert.Single(messages);
        Assert.Equal("Dobrý den!", messages[0].Content);
        Assert.Equal("Dobrý den!", messages[0].Body);
    }

    [Fact]
    public async Task GetMessages_InaccessibleConversation_ReturnsNotFound()
    {
        _repoMock
            .Setup(r => r.GetConversationMessagesAsync(_conversationId, _userId, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChatMessageResponse>?)null);

        var controller = CreateController();
        var result = await controller.GetMessages(_conversationId);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_ValidMessage_PersistsAndBroadcastsViaSignalR()
    {
        var now = DateTimeOffset.UtcNow;
        var savedMsg = new ChatMessageResponse(
            Guid.NewGuid(),
            _conversationId,
            _userId,
            "Jan Novák",
            "Kdy se bude konat pohovor?",
            now);

        _repoMock
            .Setup(r => r.AddMessageAsync(_conversationId, _userId, "Kdy se bude konat pohovor?", It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedMsg);

        var controller = CreateController();
        var result = await controller.SendMessage(
            _conversationId,
            new SendChatMessageRequest("Kdy se bude konat pohovor?"));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var returned = Assert.IsType<ChatMessageResponse>(created.Value);
        Assert.Equal(savedMsg.Id, returned.Id);

        _clientProxyMock.Verify(
            p => p.SendCoreAsync(
                "ReceiveMessage",
                It.Is<object?[]>(args => args.Length == 1 && ReferenceEquals(args[0], savedMsg)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}