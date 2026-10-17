using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;

namespace Portal.Web.Services;

public sealed class AuthSession(HttpClient http)
{
    public UserResponse? User { get; private set; }
    public event Action? Changed;

    public async Task<string?> RegisterAsync(RegisterRequest request) =>
        await CompleteLoginAsync(await http.PostAsJsonAsync("api/auth/register", request));

    public async Task<string?> LoginAsync(LoginRequest request) =>
        await CompleteLoginAsync(await http.PostAsJsonAsync("api/auth/login", request));

    public void Logout()
    {
        User = null;
        http.DefaultRequestHeaders.Authorization = null;
        Changed?.Invoke();
    }

    private async Task<string?> CompleteLoginAsync(HttpResponseMessage response)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Conflict => "E-mail je již registrován.",
                    System.Net.HttpStatusCode.Unauthorized => "Nesprávný e-mail nebo heslo.",
                    System.Net.HttpStatusCode.BadRequest => "Zkontrolujte vyplněné údaje.",
                    _ => "Přihlášení se nezdařilo. Zkuste to později."
                };
            }

            var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
            User = auth.User;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            Changed?.Invoke();
            return null;
        }
    }
}
