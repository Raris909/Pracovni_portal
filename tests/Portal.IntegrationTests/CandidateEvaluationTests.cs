using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Recruitment;

namespace Portal.IntegrationTests;

public sealed class CandidateEvaluationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData(CompanyMemberRoles.Owner, 0)]
    [InlineData(CompanyMemberRoles.Admin, 100)]
    [InlineData(CompanyMemberRoles.Recruiter, null)]
    public async Task ActiveMembersCanCreateReadAndEditTheirEvaluations(string role, int? score)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client, role);
        Assert.Empty((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path))!);
        var request = new UpsertCandidateEvaluationRequest((short?)score, "  Dobrá znalost C#.\nZkušenost s SQL.  ");
        var response = await client.PostAsJsonAsync(context.Path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(context.Path, response.Headers.Location?.AbsolutePath);
        var created = (await response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>())!;
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(context.ApplicationId, created.ApplicationId);
        Assert.Equal(context.StageId, created.StageId);
        Assert.Equal(context.CompanyId, created.CompanyId);
        Assert.Equal(context.MemberId, created.AuthorMemberId);
        Assert.Equal(request.Score, created.Score);
        Assert.Equal(request.Note.Trim(), created.Note);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
        Assert.Equal(created, Assert.Single((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(response.Headers.Location))!));

        request = new(null, "  Doplněné hodnocení  ");
        response = await client.PutAsJsonAsync($"{context.Path}/{created.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>())!;
        Assert.Equal(created with { Score = null, Note = request.Note.Trim(), UpdatedAt = updated.UpdatedAt }, updated);
        response = await client.PutAsJsonAsync($"{context.Path}/{created.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(updated, await response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>());
        Assert.Equal(updated, Assert.Single((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path))!));
        Assert.Equal(1L, await fixture.QueryAsync("""
            SELECT count(*) FROM portal.candidate_evaluations
            WHERE id = $1 AND application_id = $2 AND stage_id = $3 AND author_member_id = $4
              AND score IS NULL AND note = $5
            """, updated.Id, context.ApplicationId, context.StageId, context.MemberId, updated.Note));
        Assert.Equal("submitted", await fixture.QueryAsync("SELECT status FROM portal.applications WHERE id = $1", context.ApplicationId));
        Assert.Equal("pending", await fixture.QueryAsync("SELECT status FROM portal.application_stages WHERE application_id = $1", context.ApplicationId));
    }

    [Fact]
    public async Task MembersShareFeedbackButCanOnlyEditTheirOwnRecords()
    {
        using var owner = fixture.CreateClient();
        var context = await CreateScenarioAsync(owner);
        var first = await CreateAsync(owner, context.Path, new(80, "První hodnocení"));
        var second = await CreateAsync(owner, context.Path, new(85, "Doplňující hodnocení stejného autora"));
        await fixture.QueryAsync("UPDATE portal.applications SET assigned_member_id = $2 WHERE id = $1", context.ApplicationId, context.MemberId);
        using var recruiter = fixture.CreateClient();
        var user = await RegisterAsync(recruiter);
        var memberId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, 'recruiter') RETURNING id
            """, context.CompanyId, user.Id))!;
        var third = await CreateAsync(recruiter, context.Path, new(null, "Názor druhého HR"));
        Assert.Equal(memberId, third.AuthorMemberId);
        Assert.Equal(new[] { first, second, third }, await owner.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path));
        Assert.Equal(new[] { first, second, third }, await recruiter.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path));

        var change = new UpsertCandidateEvaluationRequest(10, "Přepsání cizí poznámky");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"{context.Path}/{third.Id}", change)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await recruiter.PutAsJsonAsync($"{context.Path}/{first.Id}", change)).StatusCode);
        Assert.Equal(new[] { first, second, third }, await owner.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path));
        Assert.Equal(HttpStatusCode.OK, (await recruiter.PutAsJsonAsync($"{context.Path}/{third.Id}", new UpsertCandidateEvaluationRequest(90, "Vlastní oprava"))).StatusCode);

        await fixture.QueryAsync("UPDATE portal.company_members SET is_active = false WHERE id = $1", memberId);
        var historical = (await owner.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path))!;
        Assert.Equal(3, historical.Length);
        Assert.Equal(memberId, historical[2].AuthorMemberId);
        Assert.Equal("Vlastní oprava", historical[2].Note);
    }

    [Fact]
    public async Task ClientCannotSupplyIdentityAuthorOrTimestamps()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var suppliedId = Guid.NewGuid();
        var request = new
        {
            id = suppliedId, applicationId = Guid.NewGuid(), stageId = Guid.NewGuid(),
            companyId = Guid.NewGuid(), authorMemberId = Guid.NewGuid(),
            createdAt = "2000-01-01T00:00:00Z", updatedAt = "2000-01-01T00:00:00Z",
            score = 75, note = "Hodnocení"
        };
        var response = await client.PostAsJsonAsync(context.Path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>())!;
        Assert.NotEqual(suppliedId, created.Id);
        Assert.Equal(context.ApplicationId, created.ApplicationId);
        Assert.Equal(context.StageId, created.StageId);
        Assert.Equal(context.CompanyId, created.CompanyId);
        Assert.Equal(context.MemberId, created.AuthorMemberId);
        Assert.NotEqual(DateTimeOffset.Parse(request.createdAt), created.CreatedAt);
        response = await client.PutAsJsonAsync($"{context.Path}/{created.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created, await response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>());
    }

    [Theory]
    [InlineData(-1, "Poznámka")]
    [InlineData(101, "Poznámka")]
    [InlineData(32768, "Poznámka")]
    [InlineData(1.5, "Poznámka")]
    [InlineData(50, null)]
    [InlineData(50, "")]
    [InlineData(50, "   ")]
    public async Task InvalidFeedbackIsRejectedWithoutChangingData(object score, string? note)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var original = await CreateAsync(client, context.Path, new(75, "Původní poznámka"));
        var request = new { score, note };
        var response = await client.PostAsJsonAsync(context.Path, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{context.Path}/{original.Id}", request)).StatusCode);
        Assert.Equal(original, Assert.Single((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path))!));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("in_progress")]
    [InlineData("passed")]
    [InlineData("failed")]
    [InlineData("skipped")]
    public async Task FeedbackCanBeRecordedWithoutChangingTheStageOutcome(string status)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        await fixture.QueryAsync("""
            UPDATE portal.application_stages SET status = $2,
                started_at = CASE WHEN $2 = 'pending' THEN NULL ELSE '2026-10-01T10:00:00Z'::timestamptz END,
                completed_at = CASE WHEN $2 IN ('passed', 'failed', 'skipped') THEN '2026-10-01T11:00:00Z'::timestamptz ELSE NULL END
            WHERE application_id = $1
            """, context.ApplicationId, status);
        var updatedAt = await fixture.QueryAsync("SELECT updated_at FROM portal.application_stages WHERE application_id = $1", context.ApplicationId);
        var evaluation = await CreateAsync(client, context.Path, new(100, "Poznámka HR"));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{context.Path}/{evaluation.Id}", new UpsertCandidateEvaluationRequest(50, "Oprava"))).StatusCode);
        Assert.Equal(status, await fixture.QueryAsync("SELECT status FROM portal.application_stages WHERE application_id = $1", context.ApplicationId));
        Assert.Equal(updatedAt, await fixture.QueryAsync("SELECT updated_at FROM portal.application_stages WHERE application_id = $1", context.ApplicationId));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public async Task ClosedApplicationsKeepReadableFeedbackAndRejectWrites(string status)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var evaluation = await CreateAsync(client, context.Path, new(70, "Hodnocení"));
        await fixture.QueryAsync("UPDATE portal.applications SET status = $2 WHERE id = $1", context.ApplicationId, status);
        var request = new UpsertCandidateEvaluationRequest(90, "Změna");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(context.Path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{context.Path}/{evaluation.Id}", request)).StatusCode);
        Assert.Equal(evaluation, Assert.Single((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path))!));
    }

    [Fact]
    public async Task FeedbackIsIsolatedByCompanyApplicationAndStage()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var evaluation = await CreateAsync(client, first.Path, new(80, "Soukromá poznámka"));
        var second = await CreateScenarioAsync(client, userId: first.UserId);
        var request = new UpsertCandidateEvaluationRequest(10, "Cizí změna");
        Assert.Empty((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(second.Path))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{second.Path}/{evaluation.Id}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{first.Path}/{Guid.NewGuid()}", request)).StatusCode);

        var otherApplicationId = await CreateApplicationAsync(first.CompanyId, first.JobId);
        await AssignStageAsync(first.CompanyId, first.JobId, otherApplicationId, first.StageId);
        var otherPath = $"/api/companies/{first.CompanyId}/applications/{otherApplicationId}/stages/{first.StageId}/evaluations";
        Assert.Empty((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(otherPath))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{otherPath}/{evaluation.Id}", request)).StatusCode);

        var unassignedPath = $"/api/companies/{first.CompanyId}/applications/{first.ApplicationId}/stages/{first.OtherStageId}/evaluations";
        await AssertDeniedAsync(client, unassignedPath, evaluation.Id, HttpStatusCode.NotFound);
        await AssignStageAsync(first.CompanyId, first.JobId, first.ApplicationId, first.OtherStageId);
        Assert.Empty((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(unassignedPath))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{unassignedPath}/{evaluation.Id}", request)).StatusCode);
        foreach (var path in new[]
        {
            $"/api/companies/{second.CompanyId}/applications/{first.ApplicationId}/stages/{first.StageId}/evaluations",
            $"/api/companies/{first.CompanyId}/applications/{first.ApplicationId}/stages/{second.StageId}/evaluations",
            $"/api/companies/{first.CompanyId}/applications/{Guid.NewGuid()}/stages/{first.StageId}/evaluations",
            $"/api/companies/{Guid.NewGuid()}/applications/{first.ApplicationId}/stages/{first.StageId}/evaluations"
        })
            await AssertDeniedAsync(client, path, evaluation.Id, HttpStatusCode.NotFound);

        using var outsider = fixture.CreateClient();
        await RegisterAsync(outsider);
        await AssertDeniedAsync(outsider, first.Path, evaluation.Id, HttpStatusCode.NotFound);
        Assert.Equal(evaluation, Assert.Single((await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(first.Path))!));
    }

    [Theory]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE user_id = $1")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1")]
    public async Task RevokedAccessIsRejectedWithExistingJwt(string sql)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var evaluation = await CreateAsync(client, context.Path, new(75, "Původní hodnocení"));
        await fixture.QueryAsync(sql, context.UserId);
        await AssertDeniedAsync(client, context.Path, evaluation.Id, HttpStatusCode.NotFound);
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.candidate_evaluations WHERE application_id = $1", context.ApplicationId));
        Assert.Equal(evaluation.Note, await fixture.QueryAsync("SELECT note FROM portal.candidate_evaluations WHERE id = $1", evaluation.Id));
    }

    [Fact]
    public async Task AnonymousAndCandidateRequestsCannotAccessInternalFeedback()
    {
        using var owner = fixture.CreateClient();
        var context = await CreateScenarioAsync(owner);
        var evaluation = await CreateAsync(owner, context.Path, new(90, "Interní hodnocení"));
        using var client = fixture.CreateClient();
        await AssertDeniedAsync(client, context.Path, evaluation.Id, HttpStatusCode.Unauthorized);
        await RegisterAsync(client, UserRoles.Candidate);
        await AssertDeniedAsync(client, context.Path, evaluation.Id, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConcurrentFeedbackAndEditsKeepCompleteRecords()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(context.Path, new UpsertCandidateEvaluationRequest(10, "První")),
            client.PostAsJsonAsync(context.Path, new UpsertCandidateEvaluationRequest(20, "Druhé")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var evaluations = (await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path))!;
        Assert.Equal(2, evaluations.Length);
        Assert.NotEqual(evaluations[0].Id, evaluations[1].Id);
        var original = evaluations[0];
        var path = $"{context.Path}/{original.Id}";
        responses = await Task.WhenAll(
            client.PutAsJsonAsync(path, new UpsertCandidateEvaluationRequest(30, "Třetí")),
            client.PutAsJsonAsync(path, new UpsertCandidateEvaluationRequest(40, "Čtvrté")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var final = (await client.GetFromJsonAsync<CandidateEvaluationResponse[]>(context.Path))![0];
        var results = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>()));
        Assert.Contains(final, results);
        Assert.Equal(original.CreatedAt, final.CreatedAt);
        Assert.Equal(original.AuthorMemberId, final.AuthorMemberId);
    }

    private static async Task AssertDeniedAsync(HttpClient client, string path, Guid evaluationId, HttpStatusCode status)
    {
        var request = new UpsertCandidateEvaluationRequest(50, "Změna");
        Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync($"{path}/{evaluationId}", request)).StatusCode);
    }

    private sealed record TestContext(
        Guid UserId, Guid CompanyId, Guid MemberId, Guid JobId, Guid ApplicationId, Guid StageId, Guid OtherStageId)
    {
        public string Path => $"/api/companies/{CompanyId}/applications/{ApplicationId}/stages/{StageId}/evaluations";
    }

    private async Task<TestContext> CreateScenarioAsync(HttpClient client, string role = CompanyMemberRoles.Owner, Guid? userId = null)
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
        var stageId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position)
            VALUES ($1, 'Screening', 'screening', 1) RETURNING id
            """, jobId))!;
        var otherStageId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position)
            VALUES ($1, 'Interview', 'interview', 2) RETURNING id
            """, jobId))!;
        var applicationId = await CreateApplicationAsync(companyId, jobId);
        await AssignStageAsync(companyId, jobId, applicationId, stageId);
        return new(currentUserId, companyId, memberId, jobId, applicationId, stageId, otherStageId);
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

    private async Task AssignStageAsync(Guid companyId, Guid jobId, Guid applicationId, Guid stageId) =>
        await fixture.QueryAsync("""
            INSERT INTO portal.application_stages (company_id, job_offer_id, application_id, stage_id) VALUES ($1, $2, $3, $4)
            """, companyId, jobId, applicationId, stageId);

    private static async Task<CandidateEvaluationResponse> CreateAsync(HttpClient client, string path, UpsertCandidateEvaluationRequest request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>())!;
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
