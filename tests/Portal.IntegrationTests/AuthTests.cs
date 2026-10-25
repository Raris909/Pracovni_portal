using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;

namespace Portal.IntegrationTests;

public sealed class AuthTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData(UserRoles.Candidate)]
    [InlineData(UserRoles.Company)]
    public async Task RegisterLoginAndReadOwnAccount(string role)
    {
        using var client = fixture.CreateClient();
        var request = NewRegistration(role);
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var registration = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(role, registration.User.Role);

        var hash = await fixture.QueryAsync("SELECT password_hash FROM portal.users WHERE id = $1", registration.User.Id);
        Assert.NotEqual(request.Password, Assert.IsType<string>(hash));
        var profiles = await fixture.QueryAsync("SELECT count(*) FROM portal.candidate_profiles WHERE user_id = $1", registration.User.Id);
        Assert.Equal(role == UserRoles.Candidate ? 1L : 0L, profiles);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = request.Email.ToUpperInvariant(),
            Password = request.Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(registration.User, me);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null)).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken + "tampered");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/hubs/chat/negotiate?negotiateVersion=1&access_token={auth.AccessToken}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/auth/me?access_token={auth.AccessToken}")).StatusCode);
    }

    [Fact]
    public async Task DuplicateEmailIsCaseInsensitiveAndWrongPasswordIsRejected()
    {
        using var client = fixture.CreateClient();
        var request = NewRegistration(UserRoles.Candidate);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        request.Email = request.Email.ToUpperInvariant();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        var login = new LoginRequest { Email = request.Email, Password = "wrong-password" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", login)).StatusCode);
    }

    [Theory]
    [InlineData("admin", "valid-password-123")]
    [InlineData(UserRoles.Candidate, "short")]
    public async Task InvalidRegistrationIsRejected(string role, string password)
    {
        using var client = fixture.CreateClient();
        var request = NewRegistration(role);
        request.Password = password;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
    }

    [Fact]
    public async Task HealthPublicJobsAndProtectedEndpointsHaveExpectedAccess()
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/jobs?limit=101")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    private static RegisterRequest NewRegistration(string role) => new()
    {
        Email = $"{Guid.NewGuid():N}@example.com",
        Password = "valid-password-123",
        ConfirmPassword = "valid-password-123",
        FirstName = "Test",
        LastName = "User",
        Role = role
    };
}
