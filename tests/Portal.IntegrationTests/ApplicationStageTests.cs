using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Recruitment;

namespace Portal.IntegrationTests;

public sealed class ApplicationStageTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
    private static readonly DateTimeOffset CompletedAt = StartedAt.AddHours(1);

    [Theory]
    [InlineData(CompanyMemberRoles.Owner)]
    [InlineData(CompanyMemberRoles.Admin)]
    [InlineData(CompanyMemberRoles.Recruiter)]
    public async Task ActiveMembersCanAssignStagesAndRecordProgress(string role)
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client, role);
        Assert.Empty((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!);

        var response = await client.PutAsync($"{application.Path}/{application.FirstStageId}", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(application.Path, response.Headers.Location?.AbsolutePath);
        var stage = (await response.Content.ReadFromJsonAsync<ApplicationStageResponse>())!;
        Assert.Equal(application.ApplicationId, stage.ApplicationId);
        Assert.Equal(application.FirstStageId, stage.StageId);
        Assert.Equal(application.JobId, stage.JobOfferId);
        Assert.Equal(application.CompanyId, stage.CompanyId);
        Assert.Equal("Screening", stage.StageName);
        Assert.Equal("screening", stage.StageKind);
        Assert.Equal(1, stage.StagePosition);
        Assert.Equal("pending", stage.Status);
        Assert.Null(stage.StartedAt);
        Assert.Null(stage.CompletedAt);
        Assert.NotEqual(default, stage.UpdatedAt);
        Assert.Equal(stage, Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(response.Headers.Location))!));

        var request = new UpdateApplicationStageStatusRequest("in_progress", StartedAt.ToOffset(TimeSpan.FromHours(2)));
        response = await client.PatchAsJsonAsync($"{application.Path}/{stage.StageId}/status", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var progress = (await response.Content.ReadFromJsonAsync<ApplicationStageResponse>())!;
        Assert.Equal(stage with { Status = request.Status, StartedAt = StartedAt, UpdatedAt = progress.UpdatedAt }, progress);
        Assert.Equal(TimeSpan.Zero, progress.StartedAt!.Value.Offset);

        request = new("passed", StartedAt, CompletedAt.ToOffset(TimeSpan.FromHours(2)));
        response = await client.PatchAsJsonAsync($"{application.Path}/{stage.StageId}/status", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = (await response.Content.ReadFromJsonAsync<ApplicationStageResponse>())!;
        Assert.Equal(progress with { Status = request.Status, CompletedAt = CompletedAt, UpdatedAt = completed.UpdatedAt }, completed);
        Assert.Equal(completed, Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!));

        response = await client.PatchAsJsonAsync($"{application.Path}/{stage.StageId}/status", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(completed, await response.Content.ReadFromJsonAsync<ApplicationStageResponse>());
        response = await client.PutAsync($"{application.Path}/{stage.StageId}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(completed, await response.Content.ReadFromJsonAsync<ApplicationStageResponse>());
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.application_stages WHERE application_id = $1", application.ApplicationId));
        Assert.Equal("submitted", await fixture.QueryAsync("SELECT status FROM portal.applications WHERE id = $1", application.ApplicationId));
    }

    [Theory]
    [InlineData("pending", "pending", HttpStatusCode.OK)]
    [InlineData("pending", "in_progress", HttpStatusCode.OK)]
    [InlineData("pending", "passed", HttpStatusCode.OK)]
    [InlineData("pending", "failed", HttpStatusCode.OK)]
    [InlineData("pending", "skipped", HttpStatusCode.OK)]
    [InlineData("in_progress", "in_progress", HttpStatusCode.OK)]
    [InlineData("in_progress", "passed", HttpStatusCode.OK)]
    [InlineData("in_progress", "failed", HttpStatusCode.OK)]
    [InlineData("in_progress", "skipped", HttpStatusCode.OK)]
    [InlineData("in_progress", "pending", HttpStatusCode.Conflict)]
    [InlineData("passed", "pending", HttpStatusCode.Conflict)]
    [InlineData("failed", "in_progress", HttpStatusCode.Conflict)]
    [InlineData("skipped", "in_progress", HttpStatusCode.Conflict)]
    [InlineData("passed", "failed", HttpStatusCode.Conflict)]
    [InlineData("failed", "skipped", HttpStatusCode.Conflict)]
    [InlineData("skipped", "passed", HttpStatusCode.Conflict)]
    [InlineData("passed", "passed", HttpStatusCode.OK)]
    [InlineData("failed", "failed", HttpStatusCode.OK)]
    [InlineData("skipped", "skipped", HttpStatusCode.OK)]
    public async Task StatusTransitionsRespectTheCurrentState(string from, string to, HttpStatusCode expected)
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        await AssignAsync(client, application.Path, application.FirstStageId);
        await fixture.QueryAsync("""
            UPDATE portal.application_stages SET status = $3,
                started_at = CASE WHEN $3 = 'pending' THEN NULL ELSE $4::timestamptz END,
                completed_at = CASE WHEN $3 IN ('passed', 'failed', 'skipped') THEN $5::timestamptz ELSE NULL END
            WHERE application_id = $1 AND stage_id = $2
            """, application.ApplicationId, application.FirstStageId, from, StartedAt, CompletedAt);
        var before = Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!);

        var request = StatusRequest(to);
        var response = await client.PatchAsJsonAsync($"{application.Path}/{application.FirstStageId}/status", request);
        Assert.Equal(expected, response.StatusCode);
        var after = Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(to, after.Status);
            Assert.Equal(request.StartedAt, after.StartedAt);
            Assert.Equal(request.CompletedAt, after.CompletedAt);
            Assert.Equal(after, await response.Content.ReadFromJsonAsync<ApplicationStageResponse>());
            if (from == to)
                Assert.Equal(before, after);
        }
        else
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(before, after);
        }
    }

    [Fact]
    public async Task InvalidStatusRequestsDoNotChangeProgress()
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        var stage = await AssignAsync(client, application.Path, application.FirstStageId);
        UpdateApplicationStageStatusRequest[] invalidRequests =
        [
            new(null!), new(""), new("accepted"), new("in_progress"), new("passed"),
            new("passed", StartedAt), new("failed", null, CompletedAt), new("skipped", StartedAt),
            new("pending", StartedAt, CompletedAt), new("in_progress", StartedAt, CompletedAt),
            new("passed", CompletedAt, StartedAt)
        ];
        foreach (var request in invalidRequests)
        {
            var response = await client.PatchAsJsonAsync($"{application.Path}/{stage.StageId}/status", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(stage, Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!));
        }

        var malformed = await client.PatchAsJsonAsync($"{application.Path}/{stage.StageId}/status",
            new { status = "in_progress", startedAt = "not-a-timestamp" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public async Task ClosedApplicationsCanBeReadButTheirStagesCannotBeChanged(string status)
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        var stage = await AssignAsync(client, application.Path, application.FirstStageId);
        await fixture.QueryAsync("UPDATE portal.applications SET status = $2 WHERE id = $1", application.ApplicationId, status);
        var updatedAt = await fixture.QueryAsync("SELECT updated_at FROM portal.applications WHERE id = $1", application.ApplicationId);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync($"{application.Path}/{application.SecondStageId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync($"{application.Path}/{stage.StageId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync($"{application.Path}/{stage.StageId}/status", StatusRequest("passed"))).StatusCode);
        Assert.Equal(stage, Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!));
        Assert.Equal(status, await fixture.QueryAsync("SELECT status FROM portal.applications WHERE id = $1", application.ApplicationId));
        Assert.Equal(updatedAt, await fixture.QueryAsync("SELECT updated_at FROM portal.applications WHERE id = $1", application.ApplicationId));
    }

    [Fact]
    public async Task ListUsesCurrentStageDefinitionsAndOrdering()
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        var second = await AssignAsync(client, application.Path, application.SecondStageId);
        var first = await AssignAsync(client, application.Path, application.FirstStageId);
        Assert.Equal(new[] { first, second }, await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path));
        var definitionPath = $"/api/companies/{application.CompanyId}/jobs/{application.JobId}/stages";
        var response = await client.PutAsJsonAsync($"{definitionPath}/{first.StageId}",
            new UpsertRecruitmentStageRequest("Úvodní screening", "screening", 1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response = await client.PutAsJsonAsync($"{definitionPath}/order",
            new ReorderRecruitmentStagesRequest([second.StageId, first.StageId]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expected = new[]
        {
            second with { StagePosition = 1 },
            first with { StageName = "Úvodní screening", StagePosition = 2 }
        };
        Assert.Equal(expected, await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path));
    }

    [Fact]
    public async Task RecruiterCanManageAnApplicationAssignedToAnotherMember()
    {
        using var owner = fixture.CreateClient();
        var application = await CreateScenarioAsync(owner);
        var stage = await AssignAsync(owner, application.Path, application.FirstStageId);
        await fixture.QueryAsync("""
            UPDATE portal.applications SET status = 'in_progress',
                assigned_member_id = (SELECT id FROM portal.company_members WHERE company_id = $2 AND user_id = $3)
            WHERE id = $1
            """, application.ApplicationId, application.CompanyId, application.UserId);
        using var recruiter = fixture.CreateClient();
        var user = await RegisterAsync(recruiter);
        await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, 'recruiter')
            """, application.CompanyId, user.Id);

        Assert.Equal(stage, Assert.Single((await recruiter.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!));
        var response = await recruiter.PatchAsJsonAsync($"{application.Path}/{stage.StageId}/status", StatusRequest("passed"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = (await response.Content.ReadFromJsonAsync<ApplicationStageResponse>())!;
        Assert.Equal("passed", completed.Status);
        Assert.Equal(completed, Assert.Single((await owner.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!));
        await AssignAsync(recruiter, application.Path, application.SecondStageId);
        Assert.Equal(2, (await owner.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!.Length);
        Assert.Equal("in_progress", await fixture.QueryAsync("SELECT status FROM portal.applications WHERE id = $1", application.ApplicationId));
    }

    [Fact]
    public async Task RepeatingTimestampCorrectionsKeepsTheSameStoredValueAndUpdatedAt()
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        await AssignAsync(client, application.Path, application.FirstStageId);
        var path = $"{application.Path}/{application.FirstStageId}/status";
        var response = await client.PatchAsJsonAsync(path, StatusRequest("passed"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var request = new UpdateApplicationStageStatusRequest(
            "passed", StartedAt.AddMinutes(1).AddTicks(1234567), CompletedAt.AddMinutes(2).AddTicks(7654321));
        response = await client.PatchAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var corrected = (await response.Content.ReadFromJsonAsync<ApplicationStageResponse>())!;
        Assert.Equal("passed", corrected.Status);
        Assert.Equal(StartedAt.AddMinutes(1).AddTicks(1234560), corrected.StartedAt);
        Assert.Equal(CompletedAt.AddMinutes(2).AddTicks(7654320), corrected.CompletedAt);

        response = await client.PatchAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(corrected, await response.Content.ReadFromJsonAsync<ApplicationStageResponse>());
    }

    [Fact]
    public async Task StagesAreIsolatedByCompanyOfferAndApplication()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var stage = await AssignAsync(client, first.Path, first.FirstStageId);
        var second = await CreateScenarioAsync(client, userId: first.UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"{second.Path}/{stage.StageId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PatchAsJsonAsync($"{second.Path}/{stage.StageId}/status", StatusRequest("passed"))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ApplicationStageResponse[]>(second.Path))!);

        var sameOfferApplicationId = await CreateApplicationAsync(first.CompanyId, first.JobId);
        var sameOfferPath = $"/api/companies/{first.CompanyId}/applications/{sameOfferApplicationId}/stages";
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PatchAsJsonAsync($"{sameOfferPath}/{stage.StageId}/status", StatusRequest("passed"))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ApplicationStageResponse[]>(sameOfferPath))!);
        Assert.Equal("pending", (await AssignAsync(client, sameOfferPath, stage.StageId)).Status);

        foreach (var path in new[]
        {
            $"/api/companies/{second.CompanyId}/applications/{first.ApplicationId}/stages",
            $"/api/companies/{first.CompanyId}/applications/{Guid.NewGuid()}/stages",
            $"/api/companies/{Guid.NewGuid()}/applications/{first.ApplicationId}/stages"
        })
            await AssertDeniedAsync(client, path, stage.StageId, HttpStatusCode.NotFound);

        using var outsider = fixture.CreateClient();
        await RegisterAsync(outsider);
        await AssertDeniedAsync(outsider, first.Path, stage.StageId, HttpStatusCode.NotFound);
        Assert.Equal(stage, Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(first.Path))!));
    }

    [Theory]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE user_id = $1")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1")]
    public async Task RevokedAccessIsRejectedWithExistingJwt(string sql)
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        await AssignAsync(client, application.Path, application.FirstStageId);
        await fixture.QueryAsync(sql, application.UserId);
        await AssertDeniedAsync(client, application.Path, application.FirstStageId, HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"{application.Path}/{application.SecondStageId}", null)).StatusCode);
        Assert.Equal("pending", await fixture.QueryAsync("SELECT status FROM portal.application_stages WHERE application_id = $1", application.ApplicationId));
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.application_stages WHERE application_id = $1", application.ApplicationId));
    }

    [Fact]
    public async Task AnonymousAndCandidateRequestsAreRejected()
    {
        using var owner = fixture.CreateClient();
        var application = await CreateScenarioAsync(owner);
        using var client = fixture.CreateClient();
        await AssertDeniedAsync(client, application.Path, application.FirstStageId, HttpStatusCode.Unauthorized);
        await RegisterAsync(client, UserRoles.Candidate);
        await AssertDeniedAsync(client, application.Path, application.FirstStageId, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConcurrentAssignmentsCreateOnePendingStage()
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        var path = $"{application.Path}/{application.FirstStageId}";
        var responses = await Task.WhenAll(client.PutAsync(path, null), client.PutAsync(path, null));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(await responses[0].Content.ReadFromJsonAsync<ApplicationStageResponse>(),
            await responses[1].Content.ReadFromJsonAsync<ApplicationStageResponse>());
        Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!);
    }

    [Fact]
    public async Task ConcurrentResultsCannotReplaceACompletedOutcome()
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        await AssignAsync(client, application.Path, application.FirstStageId);
        var path = $"{application.Path}/{application.FirstStageId}/status";
        var responses = await Task.WhenAll(
            client.PatchAsJsonAsync(path, StatusRequest("passed")),
            client.PatchAsJsonAsync(path, StatusRequest("failed")));
        var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(await winner.Content.ReadFromJsonAsync<ApplicationStageResponse>(),
            Assert.Single((await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!));
    }

    [Fact]
    public async Task ConcurrentAssignmentAndDefinitionDeletionKeepValidReferences()
    {
        using var client = fixture.CreateClient();
        var application = await CreateScenarioAsync(client);
        var assignment = client.PutAsync($"{application.Path}/{application.FirstStageId}", null);
        var deletion = client.DeleteAsync($"/api/companies/{application.CompanyId}/jobs/{application.JobId}/stages/{application.FirstStageId}");
        await Task.WhenAll(assignment, deletion);
        var stages = (await client.GetFromJsonAsync<ApplicationStageResponse[]>(application.Path))!;
        if ((await assignment).StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(HttpStatusCode.Conflict, (await deletion).StatusCode);
            Assert.Equal(application.FirstStageId, Assert.Single(stages).StageId);
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, (await assignment).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await deletion).StatusCode);
            Assert.Empty(stages);
        }
    }

    private static UpdateApplicationStageStatusRequest StatusRequest(string status) => new(
        status, status == "pending" ? null : StartedAt,
        ApplicationStageStatuses.CompletedStatuses.Contains(status) ? CompletedAt : null);

    private static async Task AssertDeniedAsync(HttpClient client, string path, Guid stageId, HttpStatusCode status)
    {
        Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(status, (await client.PutAsync($"{path}/{stageId}", null)).StatusCode);
        Assert.Equal(status, (await client.PatchAsJsonAsync($"{path}/{stageId}/status", StatusRequest("passed"))).StatusCode);
    }

    private sealed record TestApplication(
        Guid UserId, Guid CompanyId, Guid JobId, Guid ApplicationId, Guid FirstStageId, Guid SecondStageId)
    {
        public string Path => $"/api/companies/{CompanyId}/applications/{ApplicationId}/stages";
    }

    private async Task<TestApplication> CreateScenarioAsync(
        HttpClient client, string role = CompanyMemberRoles.Owner, Guid? userId = null)
    {
        var currentUserId = userId ?? (await RegisterAsync(client)).Id;
        var companyId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.companies (name, country_code) VALUES ('Test company', 'CZ') RETURNING id
            """))!;
        var memberId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, $3) RETURNING id
            """, companyId, currentUserId, role))!;
        var jobId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.job_offers (company_id, created_by_member_id, title, description, work_mode, employment_type)
            VALUES ($1, $2, 'Developer', 'Description', 'remote', 'full_time') RETURNING id
            """, companyId, memberId))!;
        var firstStageId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position)
            VALUES ($1, 'Screening', 'screening', 1) RETURNING id
            """, jobId))!;
        var secondStageId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position)
            VALUES ($1, 'Interview', 'interview', 2) RETURNING id
            """, jobId))!;
        var applicationId = await CreateApplicationAsync(companyId, jobId);
        return new(currentUserId, companyId, jobId, applicationId, firstStageId, secondStageId);
    }

    private async Task<Guid> CreateApplicationAsync(Guid companyId, Guid jobId)
    {
        var candidateId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.users (role_id, email, password_hash, first_name, last_name)
            SELECT id, $1, 'unused-test-hash', 'Test', 'Candidate' FROM portal.roles WHERE name = 'candidate'
            RETURNING id
            """, $"{Guid.NewGuid():N}@example.com"))!;
        await fixture.QueryAsync("INSERT INTO portal.candidate_profiles (user_id) VALUES ($1)", candidateId);
        return (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.applications (job_offer_id, company_id, candidate_id, profile_snapshot)
            VALUES ($1, $2, $3, '{}') RETURNING id
            """, jobId, companyId, candidateId))!;
    }

    private static async Task<ApplicationStageResponse> AssignAsync(HttpClient client, string path, Guid stageId)
    {
        var response = await client.PutAsync($"{path}/{stageId}", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApplicationStageResponse>())!;
    }

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role = UserRoles.Company)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Valid-password-123",
            ConfirmPassword = "Valid-password-123", FirstName = "Test", LastName = "User", Role = role
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth.User;
    }
}
