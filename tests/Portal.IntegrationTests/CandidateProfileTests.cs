using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.IntegrationTests;

public sealed class CandidateProfileTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Profile_CanBeReadAndUpdated()
    {
        const string path = "/api/profile";
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);

        var initialProfile = await client.GetFromJsonAsync<CandidateProfileResponse>(path);
        Assert.NotNull(initialProfile);
        Assert.Equal(user.FirstName, initialProfile.FirstName);
        Assert.Equal(user.LastName, initialProfile.LastName);
        Assert.Empty(initialProfile.Skills);
        Assert.Empty(initialProfile.Experiences);
        Assert.Empty(initialProfile.Education);

        var updateRequest = new UpdateCandidateProfileRequest
        {
            FirstName = "  Upravené Jméno  ",
            LastName = "Upravené Příjmení",
            Phone = "  +420 123 456 789  ",
            Headline = "C# Developer",
            Summary = "Vývoj webových aplikací",
            City = "Praha",
            CountryCode = "CZ",
            Skills = ["  C#  ", "PostgreSQL", ".NET"]
        };

        var updateResponse = await client.PutAsJsonAsync(path, updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedProfile = (await updateResponse.Content.ReadFromJsonAsync<CandidateProfileResponse>())!;
        Assert.Equal("Upravené Jméno", updatedProfile.FirstName);
        Assert.Equal("C# Developer", updatedProfile.Headline);
        Assert.Equal("+420 123 456 789", updatedProfile.Phone);
        Assert.Equal(updateRequest.Summary, updatedProfile.Summary);
        Assert.Equal(updateRequest.City, updatedProfile.City);
        Assert.Equal("CZ", updatedProfile.CountryCode);
        Assert.Equal(new[] { "C#", "PostgreSQL", ".NET" }, updatedProfile.Skills);
        Assert.Equal(initialProfile.CreatedAt, updatedProfile.CreatedAt);
        Assert.True(updatedProfile.UpdatedAt > initialProfile.UpdatedAt);

        var reloaded = (await client.GetFromJsonAsync<CandidateProfileResponse>(path))!;
        Assert.Equal(updatedProfile.FirstName, reloaded.FirstName);
        Assert.Equal(updatedProfile.Phone, reloaded.Phone);
        Assert.Equal(updatedProfile.Skills, reloaded.Skills);

        var meResponse = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal("Upravené Jméno", meResponse!.FirstName);
    }

    [Fact]
    public async Task Experience_CanBeAddedUpdatedAndRemoved()
    {
        const string path = "/api/profile";
        using var client = fixture.CreateClient();
        await RegisterAsync(client);

        var addRequest = new CandidateExperienceRequest
        {
            Employer = "  Firma A  ",
            Position = "  Vývojář  ",
            Description = "  Vývoj API  ",
            StartDate = new DateOnly(2020, 1, 1)
        };

        Assert.Empty((await client.GetFromJsonAsync<CandidateExperienceResponse[]>($"{path}/experiences"))!);
        var addResponse = await client.PostAsJsonAsync($"{path}/experiences", addRequest);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var addedExperience = (await addResponse.Content.ReadFromJsonAsync<CandidateExperienceResponse>())!;
        Assert.NotEqual(Guid.Empty, addedExperience.Id);
        Assert.Equal("Firma A", addedExperience.Employer);
        Assert.Equal("Vývojář", addedExperience.Position);
        Assert.Equal("Vývoj API", addedExperience.Description);
        Assert.Null(addedExperience.EndDate);
        Assert.NotNull(addResponse.Headers.Location);
        Assert.Equal(addedExperience, await client.GetFromJsonAsync<CandidateExperienceResponse>(addResponse.Headers.Location));
        Assert.Equal(addedExperience, Assert.Single((await client.GetFromJsonAsync<CandidateExperienceResponse[]>($"{path}/experiences"))!));
        var profileWithExperience = (await client.GetFromJsonAsync<CandidateProfileResponse>(path))!;
        Assert.Equal(addedExperience, Assert.Single(profileWithExperience.Experiences));

        var updateRequest = new CandidateExperienceRequest
        {
            Employer = "  Firma A.s.  ",
            Position = "  Senior Vývojář  ",
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2023, 1, 1)
        };

        var updateResponse = await client.PutAsJsonAsync($"{path}/experiences/{addedExperience.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedExperience = (await updateResponse.Content.ReadFromJsonAsync<CandidateExperienceResponse>())!;
        Assert.Equal("Senior Vývojář", updatedExperience.Position);
        Assert.Equal("Firma A.s.", updatedExperience.Employer);
        Assert.Equal(updateRequest.EndDate, updatedExperience.EndDate);
        Assert.Null(updatedExperience.Description);
        Assert.Equal(updatedExperience, await client.GetFromJsonAsync<CandidateExperienceResponse>($"{path}/experiences/{addedExperience.Id}"));

        var deleteResponse = await client.DeleteAsync($"{path}/experiences/{addedExperience.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{path}/experiences/{addedExperience.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{path}/experiences/{addedExperience.Id}")).StatusCode);

        var profile = await client.GetFromJsonAsync<CandidateProfileResponse>(path);
        Assert.Empty(profile!.Experiences);
    }

    [Fact]
    public async Task Education_CanBeAddedUpdatedAndRemoved()
    {
        const string path = "/api/profile";
        using var client = fixture.CreateClient();
        await RegisterAsync(client);

        var addRequest = new CandidateEducationRequest
        {
            School = "  Univerzita  ",
            FieldOfStudy = "  IT  ",
            Degree = "  Bc.  ",
            StartDate = new DateOnly(2015, 9, 1)
        };

        Assert.Empty((await client.GetFromJsonAsync<CandidateEducationResponse[]>($"{path}/education"))!);
        var addResponse = await client.PostAsJsonAsync($"{path}/education", addRequest);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var addedEducation = (await addResponse.Content.ReadFromJsonAsync<CandidateEducationResponse>())!;
        Assert.Equal("Univerzita", addedEducation.School);
        Assert.Equal("IT", addedEducation.FieldOfStudy);
        Assert.Equal("Bc.", addedEducation.Degree);
        Assert.Null(addedEducation.EndDate);
        Assert.NotNull(addResponse.Headers.Location);
        Assert.Equal(addedEducation, await client.GetFromJsonAsync<CandidateEducationResponse>(addResponse.Headers.Location));
        Assert.Equal(addedEducation, Assert.Single((await client.GetFromJsonAsync<CandidateEducationResponse[]>($"{path}/education"))!));
        var profile = (await client.GetFromJsonAsync<CandidateProfileResponse>(path))!;
        Assert.Equal(addedEducation, Assert.Single(profile.Education));

        addRequest.Degree = "Mgr.";
        addRequest.EndDate = addRequest.StartDate;
        var updateResponse = await client.PutAsJsonAsync($"{path}/education/{addedEducation.Id}", addRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedEducation = (await updateResponse.Content.ReadFromJsonAsync<CandidateEducationResponse>())!;
        Assert.Equal("Mgr.", updatedEducation.Degree);
        Assert.Equal(addRequest.StartDate, updatedEducation.EndDate);
        Assert.Equal(updatedEducation, await client.GetFromJsonAsync<CandidateEducationResponse>($"{path}/education/{addedEducation.Id}"));

        var deleteResponse = await client.DeleteAsync($"{path}/education/{addedEducation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{path}/education/{addedEducation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{path}/education/{addedEducation.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CandidateProfileResponse>(path))!.Education);
    }

    [Theory]
    [InlineData("experiences")]
    [InlineData("education")]
    public async Task EntriesCannotBeReadChangedOrDeletedByAnotherCandidate(string collection)
    {
        using var owner = fixture.CreateClient();
        var ownerUser = await RegisterAsync(owner);
        var request = NewEntry(collection);
        var response = await owner.PostAsJsonAsync($"/api/profile/{collection}", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = entry.GetProperty("id").GetGuid();

        using var other = fixture.CreateClient();
        var otherUser = await RegisterAsync(other);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement[]>($"/api/profile/{collection}"))!);
        var otherProfile = (await other.GetFromJsonAsync<CandidateProfileResponse>($"/api/profile?userId={ownerUser.Id}"))!;
        Assert.Equal(otherUser.Id, otherProfile.UserId);
        Assert.Empty(otherProfile.Experiences);
        Assert.Empty(otherProfile.Education);

        foreach (var unavailableId in new[] { id, Guid.NewGuid() })
        {
            var path = $"/api/profile/{collection}/{unavailableId}";
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync(path, NewEntry(collection))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(path)).StatusCode);
        }

        var unchanged = await owner.GetFromJsonAsync<JsonElement>($"/api/profile/{collection}/{id}");
        Assert.Equal(entry.GetRawText(), unchanged.GetRawText());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(UserRoles.Company, HttpStatusCode.Forbidden)]
    public async Task AllProfileEndpointsRequireACandidate(string? role, HttpStatusCode expectedStatus)
    {
        using var client = fixture.CreateClient();
        if (role is not null)
            await RegisterAsync(client, role);

        await AssertAccessDeniedAsync(client, expectedStatus, Guid.NewGuid(), Guid.NewGuid());
    }

    [Theory]
    [InlineData("firstName", " ")]
    [InlineData("lastName", null)]
    [InlineData("countryCode", "cz")]
    [InlineData("countryCode", "CZE")]
    public async Task InvalidProfileDataIsRejectedWithoutChangingUser(string field, string? value)
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        var request = new Dictionary<string, object?>
        {
            ["firstName"] = "Changed",
            ["lastName"] = "Candidate",
            [field] = value
        };

        var response = await client.PutAsJsonAsync("/api/profile", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(errors.GetProperty("errors").EnumerateObject().Any());
        Assert.Equal(user.FirstName, (await client.GetFromJsonAsync<CandidateProfileResponse>("/api/profile"))!.FirstName);
    }

    [Theory]
    [MemberData(nameof(InvalidSkills))]
    public async Task InvalidSkillsAreRejected(string?[]? skills)
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var request = new { firstName = "Test", lastName = "Candidate", skills };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/profile", request)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CandidateProfileResponse>("/api/profile"))!.Skills);
    }

    public static TheoryData<string?[]?> InvalidSkills => new()
    {
        null,
        new string?[] { null },
        new[] { " " },
        new[] { "C#", " c# " },
        new[] { new string('a', 101) },
        Enumerable.Range(0, 51).Select(i => $"Skill {i}").ToArray()
    };

    [Theory]
    [InlineData("experiences", "employer", " ")]
    [InlineData("experiences", "position", null)]
    [InlineData("education", "school", " ")]
    [InlineData("experiences", "startDate", null)]
    [InlineData("education", "startDate", "0001-01-01")]
    [InlineData("experiences", "endDate", "2019-12-31")]
    [InlineData("education", "endDate", "2019-12-31")]
    [InlineData("education", "startDate", "invalid")]
    public async Task InvalidEntryDataIsRejectedForCreateAndUpdate(string collection, string field, string? value)
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var createdResponse = await client.PostAsJsonAsync($"/api/profile/{collection}", NewEntry(collection));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var request = NewEntry(collection);
        request[field] = value;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/profile/{collection}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/profile/{collection}/{id}", request)).StatusCode);
        var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/profile/{collection}/{id}");
        Assert.Equal(created.GetRawText(), unchanged.GetRawText());
        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/api/profile/{collection}"))!);
    }

    [Fact]
    public async Task ProfileUpdateRollsBackUserChangesWhenProfileIsMissing()
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        await fixture.QueryAsync("DELETE FROM portal.candidate_profiles WHERE user_id = $1", user.Id);

        var request = new UpdateCandidateProfileRequest { FirstName = "Changed", LastName = "Name" };
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/profile", request)).StatusCode);
        Assert.Equal(user.FirstName, await fixture.QueryAsync("SELECT first_name FROM portal.users WHERE id = $1", user.Id));
    }

    private static async Task AssertAccessDeniedAsync(HttpClient client, HttpStatusCode status, Guid experienceId, Guid educationId)
    {
        Assert.Equal(status, (await client.GetAsync("/api/profile")).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync("/api/profile",
            new UpdateCandidateProfileRequest { FirstName = "Changed", LastName = "Name" })).StatusCode);

        foreach (var (collection, id) in new[] { ("experiences", experienceId), ("education", educationId) })
        {
            var path = $"/api/profile/{collection}";
            var request = NewEntry(collection);
            request[collection == "experiences" ? "employer" : "school"] = "Changed";
            Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(status, (await client.GetAsync($"{path}/{id}")).StatusCode);
            Assert.Equal(status, (await client.PostAsJsonAsync(path, request)).StatusCode);
            Assert.Equal(status, (await client.PutAsJsonAsync($"{path}/{id}", request)).StatusCode);
            Assert.Equal(status, (await client.DeleteAsync($"{path}/{id}")).StatusCode);
        }
    }

    private static Dictionary<string, object?> NewEntry(string collection) => collection == "experiences"
        ? new() { ["employer"] = "Company", ["position"] = "Developer", ["startDate"] = "2020-01-01" }
        : new() { ["school"] = "University", ["startDate"] = "2020-01-01" };

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
