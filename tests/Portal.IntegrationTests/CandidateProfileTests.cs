using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.IntegrationTests;

public sealed class CandidateProfileTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Profile_CanBeReadAndUpdated()
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);

        var initialProfile = await client.GetFromJsonAsync<CandidateProfileResponse>("/api/profiles/me");
        Assert.NotNull(initialProfile);
        Assert.Equal(user.FirstName, initialProfile.FirstName);
        Assert.Equal(user.LastName, initialProfile.LastName);
        Assert.Empty(initialProfile.Skills);
        Assert.Empty(initialProfile.Experiences);
        Assert.Empty(initialProfile.Education);

        var updateRequest = new UpdateCandidateProfileRequest
        {
            FirstName = "Upravené Jméno",
            LastName = "Upravené Příjmení",
            Headline = "C# Developer",
            City = "Praha",
            CountryCode = "CZ",
            Skills = ["C#", "PostgreSQL", ".NET"]
        };

        var updateResponse = await client.PutAsJsonAsync("/api/profiles/me", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedProfile = (await updateResponse.Content.ReadFromJsonAsync<CandidateProfileResponse>())!;
        Assert.Equal("Upravené Jméno", updatedProfile.FirstName);
        Assert.Equal("C# Developer", updatedProfile.Headline);
        Assert.Equal(3, updatedProfile.Skills.Count);

        var meResponse = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal("Upravené Jméno", meResponse!.FirstName);
    }

    [Fact]
    public async Task Experience_CanBeAddedUpdatedAndRemoved()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);

        var addRequest = new CandidateExperienceRequest
        {
            Employer = "Firma A",
            Position = "Vývojář",
            StartDate = new DateOnly(2020, 1, 1)
        };

        var addResponse = await client.PostAsJsonAsync("/api/profiles/me/experiences", addRequest);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var addedExperience = (await addResponse.Content.ReadFromJsonAsync<CandidateExperienceResponse>())!;
        Assert.NotEqual(Guid.Empty, addedExperience.Id);
        Assert.Equal("Firma A", addedExperience.Employer);

        var updateRequest = new CandidateExperienceRequest
        {
            Employer = "Firma A.s.",
            Position = "Senior Vývojář",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1)
        };

        var updateResponse = await client.PutAsJsonAsync($"/api/profiles/me/experiences/{addedExperience.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedExperience = (await updateResponse.Content.ReadFromJsonAsync<CandidateExperienceResponse>())!;
        Assert.Equal("Senior Vývojář", updatedExperience.Position);

        var deleteResponse = await client.DeleteAsync($"/api/profiles/me/experiences/{addedExperience.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var profile = await client.GetFromJsonAsync<CandidateProfileResponse>("/api/profiles/me");
        Assert.Empty(profile!.Experiences);
    }

    [Fact]
    public async Task Education_CanBeAddedUpdatedAndRemoved()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);

        var addRequest = new CandidateEducationRequest
        {
            School = "Univerzita",
            FieldOfStudy = "IT",
            Degree = "Bc.",
            StartDate = new DateOnly(2015, 9, 1)
        };

        var addResponse = await client.PostAsJsonAsync("/api/profiles/me/education", addRequest);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var addedEducation = (await addResponse.Content.ReadFromJsonAsync<CandidateEducationResponse>())!;
        Assert.Equal("Univerzita", addedEducation.School);

        addRequest.Degree = "Mgr.";
        var updateResponse = await client.PutAsJsonAsync($"/api/profiles/me/education/{addedEducation.Id}", addRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/profiles/me/education/{addedEducation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role = UserRoles.Candidate)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "Valid-password-123!",
            ConfirmPassword = "Valid-password-123!",
            FirstName = "Test",
            LastName = "Candidate",
            Role = role
        });

        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth.User;
    }
}
