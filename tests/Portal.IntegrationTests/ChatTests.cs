using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Portal.Api.Applications;
using Portal.Contracts.Applications;
using Portal.Contracts.Auth;
using Portal.Contracts.Chat;
using Portal.Contracts.Companies;
using Portal.Contracts.Jobs;

namespace Portal.IntegrationTests;

public sealed class ChatTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CandidateAndHrCanExchangeMessagesViaRestAndReadHistory()
    {
        using var hr = fixture.CreateClient();
        using var candidate = fixture.CreateClient();
        var ctx = await CreateApplicationConversationAsync(hr, candidate);

        var candidateConvs = (await candidate.GetFromJsonAsync<ChatConversationResponse[]>("/api/chat/conversations"))!;
        var hrConvs = (await hr.GetFromJsonAsync<ChatConversationResponse[]>("/api/chat/conversations"))!;
        Assert.Contains(candidateConvs, c => c.Id == ctx.ConversationId && c.ApplicationId == ctx.ApplicationId);
        Assert.Contains(hrConvs, c => c.Id == ctx.ConversationId && c.ApplicationId == ctx.ApplicationId);

        var byApp = await hr.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/chat/applications/{ctx.ApplicationId}/conversation");
        Assert.NotNull(byApp);
        Assert.Equal(ctx.ConversationId, byApp.Id);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await candidate.PostAsJsonAsync(
                $"/api/chat/conversations/{ctx.ConversationId}/messages",
                new SendChatMessageRequest("   "))).StatusCode);

        var firstResp = await candidate.PostAsJsonAsync(
            $"/api/chat/conversations/{ctx.ConversationId}/messages",
            new SendChatMessageRequest("Dobrý den, děkuji za možnost se přihlásit."));
        Assert.Equal(HttpStatusCode.Created, firstResp.StatusCode);
        var firstMsg = (await firstResp.Content.ReadFromJsonAsync<ChatMessageResponse>())!;
        Assert.Equal(ctx.CandidateUser.Id, firstMsg.SenderId);
        Assert.Equal("Dobrý den, děkuji za možnost se přihlásit.", firstMsg.Content);

        var secondResp = await hr.PostAsJsonAsync(
            $"/api/chat/conversations/{ctx.ConversationId}/messages",
            new SendChatMessageRequest("Dobrý den, brzy se ozveme s termínem pohovoru."));
        Assert.Equal(HttpStatusCode.Created, secondResp.StatusCode);
        var secondMsg = (await secondResp.Content.ReadFromJsonAsync<ChatMessageResponse>())!;
        Assert.Equal(ctx.HrUser.Id, secondMsg.SenderId);

        var history = (await candidate.GetFromJsonAsync<ChatMessageResponse[]>(
            $"/api/chat/conversations/{ctx.ConversationId}/messages"))!;
        Assert.Equal(2, history.Length);
        Assert.Equal(firstMsg.Id, history[0].Id);
        Assert.Equal(secondMsg.Id, history[1].Id);

        var updatedConv = (await candidate.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/chat/applications/{ctx.ApplicationId}/conversation"))!;
        Assert.Equal(secondMsg.Content, updatedConv.LastMessage);
        Assert.NotNull(updatedConv.LastMessageAt);
    }

    [Fact]
    public async Task SignalRHub_DeliversMessagesInRealTimeAndPersistsToDatabase()
    {
        using var hr = fixture.CreateClient();
        using var candidate = fixture.CreateClient();
        var ctx = await CreateApplicationConversationAsync(hr, candidate);

        await using var hrHub = CreateHubConnection(ctx.HrToken);
        await using var candidateHub = CreateHubConnection(ctx.CandidateToken);

        var candidateReceived = new TaskCompletionSource<ChatMessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hrReceived = new TaskCompletionSource<ChatMessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        candidateHub.On<ChatMessageResponse>("ReceiveMessage", msg =>
        {
            if (msg.SenderId == ctx.HrUser.Id)
                candidateReceived.TrySetResult(msg);
        });
        hrHub.On<ChatMessageResponse>("ReceiveMessage", msg =>
        {
            if (msg.SenderId == ctx.CandidateUser.Id)
                hrReceived.TrySetResult(msg);
        });

        await hrHub.StartAsync();
        await candidateHub.StartAsync();

        await hrHub.InvokeAsync("JoinConversation", ctx.ConversationId.ToString());
        await candidateHub.InvokeAsync("JoinConversation", ctx.ConversationId.ToString());

        await hrHub.InvokeAsync("SendMessage", ctx.ConversationId.ToString(), "Ahoj přes SignalR!");
        var fromHub = await candidateReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ctx.ConversationId, fromHub.ConversationId);
        Assert.Equal(ctx.HrUser.Id, fromHub.SenderId);
        Assert.Equal("Ahoj přes SignalR!", fromHub.Content);

        var restSend = await candidate.PostAsJsonAsync(
            $"/api/chat/conversations/{ctx.ConversationId}/messages",
            new SendChatMessageRequest("Odpověď přes REST API."));
        Assert.Equal(HttpStatusCode.Created, restSend.StatusCode);

        var fromRest = await hrReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ctx.CandidateUser.Id, fromRest.SenderId);
        Assert.Equal("Odpověď přes REST API.", fromRest.Content);

        var persisted = (await hr.GetFromJsonAsync<ChatMessageResponse[]>(
            $"/api/chat/conversations/{ctx.ConversationId}/messages"))!;
        Assert.Equal(2, persisted.Length);
    }

    [Fact]
    public async Task OutsidersAndDeactivatedMembersAreDeniedViaRestAndSignalR()
    {
        using var hr = fixture.CreateClient();
        using var candidate = fixture.CreateClient();
        var ctx = await CreateApplicationConversationAsync(hr, candidate);

        using var outsiderCandidate = fixture.CreateClient();
        var outsiderAuth = await RegisterAsync(outsiderCandidate, UserRoles.Candidate);

        using var outsiderHr = fixture.CreateClient();
        await RegisterAsync(outsiderHr, UserRoles.Company);

        foreach (var outsider in new[] { outsiderCandidate, outsiderHr })
        {
            var convs = (await outsider.GetFromJsonAsync<ChatConversationResponse[]>("/api/chat/conversations"))!;
            Assert.DoesNotContain(convs, c => c.Id == ctx.ConversationId);
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.GetAsync($"/api/chat/applications/{ctx.ApplicationId}/conversation")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.GetAsync($"/api/chat/conversations/{ctx.ConversationId}/messages")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.PostAsJsonAsync(
                    $"/api/chat/conversations/{ctx.ConversationId}/messages",
                    new SendChatMessageRequest("Neoprávněná zpráva"))).StatusCode);
        }

        await using var outsiderHub = CreateHubConnection(outsiderAuth.AccessToken);
        await outsiderHub.StartAsync();
        await Assert.ThrowsAsync<HubException>(() =>
            outsiderHub.InvokeAsync("JoinConversation", ctx.ConversationId.ToString()));
        await Assert.ThrowsAsync<HubException>(() =>
            outsiderHub.InvokeAsync("SendMessage", ctx.ConversationId.ToString(), "Hack"));

        await fixture.QueryAsync("UPDATE portal.company_members SET is_active = false WHERE user_id = $1", ctx.HrUser.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await hr.GetAsync($"/api/chat/conversations/{ctx.ConversationId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await hr.PostAsJsonAsync(
                $"/api/chat/conversations/{ctx.ConversationId}/messages",
                new SendChatMessageRequest("Po odebrání z firmy"))).StatusCode);

        using var anon = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/chat/conversations")).StatusCode);
    }

    private HubConnection CreateHubConnection(string accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var wsClient = fixture.Server.CreateWebSocketClient();
                    wsClient.ConfigureRequest = request =>
                    {
                        request.Headers.Authorization = $"Bearer {accessToken}";
                    };
                    return await wsClient.ConnectAsync(context.Uri, cancellationToken);
                };
            })
            .Build();

    private sealed record ChatScenario(
        UserResponse HrUser,
        string HrToken,
        UserResponse CandidateUser,
        string CandidateToken,
        Guid ApplicationId,
        Guid ConversationId);

    private async Task<ChatScenario> CreateApplicationConversationAsync(HttpClient hr, HttpClient candidate)
    {
        var hrAuth = await RegisterAsync(hr, UserRoles.Company);
        var compResp = await hr.PostAsJsonAsync("/api/companies", new CreateCompanyRequest
        {
            Name = "Chat Company",
            CountryCode = "CZ"
        });
        Assert.Equal(HttpStatusCode.Created, compResp.StatusCode);
        var company = (await compResp.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;

        var jobResp = await hr.PostAsJsonAsync($"/api/companies/{company.Company.Id}/jobs", new JobOfferRequest
        {
            Title = "Chat Job",
            Description = "Popis pozice",
            Requirements = "C#",
            WorkMode = WorkModes.Remote,
            EmploymentType = EmploymentTypes.FullTime
        });
        Assert.Equal(HttpStatusCode.Created, jobResp.StatusCode);
        var job = (await jobResp.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await hr.PostAsync($"/api/companies/{company.Company.Id}/jobs/{job.Id}/publish", null)).StatusCode);

        var candAuth = await RegisterAsync(candidate, UserRoles.Candidate);
        var appResp = await candidate.PostAsJsonAsync(
            $"/api/jobs/{job.Id}/applications",
            new SubmitJobApplicationBodyRequest("Zájem o pozici"));
        Assert.Equal(HttpStatusCode.Created, appResp.StatusCode);
        var app = (await appResp.Content.ReadFromJsonAsync<SubmittedApplicationResult>())!;

        var detail = (await candidate.GetFromJsonAsync<CandidateApplicationDetailResponse>($"/api/applications/{app.Id}"))!;
        Assert.NotNull(detail.ConversationId);

        return new(hrAuth.User, hrAuth.AccessToken, candAuth.User, candAuth.AccessToken, app.Id, detail.ConversationId.Value);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string role)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "Valid-password-123!",
            ConfirmPassword = "Valid-password-123!",
            FirstName = "Test",
            LastName = "User",
            Role = role
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }
}
