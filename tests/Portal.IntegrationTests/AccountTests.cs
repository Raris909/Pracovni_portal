using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;

namespace Portal.IntegrationTests;

public sealed class AccountTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task UpdateAccount_PersistsNameAndPhoneForCompanyUser()
    {
        using var client = fixture.CreateClient();
        var auth = await RegisterAndAuthenticateAsync(client, UserRoles.Company, "AccountPass123!");

        var updateResponse = await client.PutAsJsonAsync(
            "/api/account",
            new UpdateAccountRequest
            {
                FirstName = "  Tereza  ",
                LastName = "  Králová  ",
                Phone = " +420601112233 "
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(updated);
        Assert.Equal(auth.User.Id, updated!.Id);
        Assert.Equal("Tereza", updated.FirstName);
        Assert.Equal("Králová", updated.LastName);
        Assert.Equal("+420601112233", updated.Phone);

        var fetchedAccount = await client.GetFromJsonAsync<UserResponse>("/api/account");
        var fetchedMe = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(updated, fetchedAccount);
        Assert.Equal(updated, fetchedMe);
    }

    [Fact]
    public async Task ChangePassword_ReplacesPasswordAndRejectsWrongCurrentPassword()
    {
        using var client = fixture.CreateClient();
        var auth = await RegisterAndAuthenticateAsync(client, UserRoles.Candidate, "InitialPass123!");

        var wrongCurrent = await client.PutAsJsonAsync(
            "/api/account/password",
            new ChangePasswordRequest
            {
                CurrentPassword = "WrongPass123!",
                NewPassword = "UpdatedPass456!"
            });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var changeResponse = await client.PutAsJsonAsync(
            "/api/account/password",
            new ChangePasswordRequest
            {
                CurrentPassword = "InitialPass123!",
                NewPassword = "UpdatedPass456!"
            });
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        var oldLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = auth.User.Email, Password = "InitialPass123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = auth.User.Email, Password = "UpdatedPass456!" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task DeactivateAccount_DisablesAccountAndBlocksSubsequentAccess()
    {
        using var client = fixture.CreateClient();
        var auth = await RegisterAndAuthenticateAsync(client, UserRoles.Company, "DeactivatePass1!");

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/account/deactivate",
            new DeactivateAccountRequest { CurrentPassword = "WrongPass123!" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);

        var deactivateResponse = await client.PostAsJsonAsync(
            "/api/account/deactivate",
            new DeactivateAccountRequest { CurrentPassword = "DeactivatePass1!" });
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        var isActive = await fixture.QueryAsync(
            "SELECT is_active FROM portal.users WHERE id = $1",
            auth.User.Id);
        Assert.Equal(false, isActive);

        var getAccountResponse = await client.GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.Unauthorized, getAccountResponse.StatusCode);

        var getMeResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, getMeResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = auth.User.Email, Password = "DeactivatePass1!" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    private static async Task<AuthResponse> RegisterAndAuthenticateAsync(
        HttpClient client, string role, string password)
    {
        var email = $"account-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = email,
                Password = password,
                ConfirmPassword = password,
                FirstName = "Jan",
                LastName = "Novák",
                Role = role
            });
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }
}
