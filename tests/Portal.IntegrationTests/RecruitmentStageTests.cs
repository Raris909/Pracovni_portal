using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Recruitment;

namespace Portal.IntegrationTests;

public sealed class RecruitmentStageTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData(CompanyMemberRoles.Owner)]
    [InlineData(CompanyMemberRoles.Admin)]
    [InlineData(CompanyMemberRoles.Recruiter)]
    public async Task ActiveMembersCanManageStageDefinitionsAndOrder(string role)
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client, role);
        Assert.Empty((await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path))!);

        var first = await CreateStageAsync(client, offer.Path, 5);
        var response = await client.PostAsJsonAsync(offer.Path,
            new UpsertRecruitmentStageRequest("  Pohovor  ", "interview", 2, "Online pohovor"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(offer.Path, response.Headers.Location?.AbsolutePath);
        var second = (await response.Content.ReadFromJsonAsync<RecruitmentStageResponse>())!;
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.Equal(offer.JobId, second.JobOfferId);
        Assert.Equal("Pohovor", second.Name);
        Assert.Equal("interview", second.Kind);
        Assert.Equal(2, second.Position);
        Assert.Equal("Online pohovor", second.Description);
        Assert.Equal(new[] { second, first }, await client.GetFromJsonAsync<RecruitmentStageResponse[]>(response.Headers.Location));

        var request = new UpsertRecruitmentStageRequest("  Technický test  ", "test", 3);
        response = await client.PutAsJsonAsync($"{offer.Path}/{second.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        second = second with { Name = request.Name.Trim(), Kind = request.Kind, Position = 3, Description = null };
        Assert.Equal(second, await response.Content.ReadFromJsonAsync<RecruitmentStageResponse>());

        response = await client.PutAsJsonAsync($"{offer.Path}/order",
            new ReorderRecruitmentStagesRequest([first.Id, second.Id]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ordered = new[] { first with { Position = 1 }, second with { Position = 2 } };
        Assert.Equal(ordered, await response.Content.ReadFromJsonAsync<RecruitmentStageResponse[]>());
        Assert.Equal(ordered, await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path));

        response = await client.DeleteAsync($"{offer.Path}/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ordered[1], Assert.Single((await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path))!));
        Assert.Equal(0L, await fixture.QueryAsync("SELECT count(*) FROM portal.recruitment_stages WHERE id = $1", first.Id));
    }

    [Fact]
    public async Task PositionsAreUniqueAndReorderCanSwapOccupiedPositions()
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client);
        var first = await CreateStageAsync(client, offer.Path, 1);
        var second = await CreateStageAsync(client, offer.Path, 2);
        var request = new UpsertRecruitmentStageRequest("Změna", "test", 1);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(offer.Path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{offer.Path}/{second.Id}", request)).StatusCode);
        Assert.Equal(new[] { first, second }, await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path));

        var response = await client.PutAsJsonAsync($"{offer.Path}/order",
            new ReorderRecruitmentStagesRequest([second.Id, first.Id]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = new[] { second with { Position = 1 }, first with { Position = 2 } };
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<RecruitmentStageResponse[]>());
        Assert.Equal(expected, await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path));
    }

    [Fact]
    public async Task InvalidOrdersLeaveAllStagesUnchanged()
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client);
        var first = await CreateStageAsync(client, offer.Path, 1);
        var second = await CreateStageAsync(client, offer.Path, 2);
        var other = await CreateOfferAsync(client, userId: offer.UserId);
        var foreignStage = await CreateStageAsync(client, other.Path, 1);
        Guid[][] invalidOrders = [[], [first.Id], [first.Id, first.Id], [first.Id, Guid.Empty],
            [first.Id, Guid.NewGuid()], [first.Id, foreignStage.Id], [first.Id, second.Id, Guid.NewGuid()]];
        foreach (var ids in invalidOrders)
        {
            var response = await client.PutAsJsonAsync($"{offer.Path}/order", new ReorderRecruitmentStagesRequest(ids));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(new[] { first, second }, await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path));
        }

        var nullOrder = await client.PutAsJsonAsync($"{offer.Path}/order", new { orderedStageIds = (Guid[]?)null });
        Assert.Equal(HttpStatusCode.BadRequest, nullOrder.StatusCode);
    }

    [Theory]
    [InlineData("name", "   ")]
    [InlineData("name", null)]
    [InlineData("kind", "invalid")]
    [InlineData("kind", null)]
    [InlineData("position", 0)]
    [InlineData("position", -1)]
    public async Task InvalidDefinitionsAreRejected(string field, object? value)
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client);
        var stage = await CreateStageAsync(client, offer.Path, 1);
        var request = new Dictionary<string, object?>
        {
            ["name"] = "Pohovor", ["kind"] = "interview", ["position"] = 2, [field] = value
        };
        var response = await client.PostAsJsonAsync(offer.Path, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{offer.Path}/{stage.Id}", request)).StatusCode);
        Assert.Equal(stage, Assert.Single((await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path))!));
    }

    [Fact]
    public async Task StagesAreIsolatedByCompanyAndOffer()
    {
        using var client = fixture.CreateClient();
        var first = await CreateOfferAsync(client);
        var stage = await CreateStageAsync(client, first.Path, 1);
        var second = await CreateOfferAsync(client, userId: first.UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{second.Path}/{stage.Id}",
            new UpsertRecruitmentStageRequest("Změna", "test", 2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{second.Path}/{stage.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"{second.Path}/order", new ReorderRecruitmentStagesRequest([stage.Id]))).StatusCode);
        foreach (var path in new[]
        {
            $"/api/companies/{second.CompanyId}/jobs/{first.JobId}/stages",
            $"/api/companies/{first.CompanyId}/jobs/{Guid.NewGuid()}/stages",
            $"/api/companies/{Guid.NewGuid()}/jobs/{first.JobId}/stages"
        })
            await AssertAccessDeniedAsync(client, path, stage.Id, HttpStatusCode.NotFound);

        using var outsider = fixture.CreateClient();
        await RegisterAsync(outsider);
        await AssertAccessDeniedAsync(outsider, first.Path, stage.Id, HttpStatusCode.NotFound);
        Assert.Equal(stage, Assert.Single((await client.GetFromJsonAsync<RecruitmentStageResponse[]>(first.Path))!));
    }

    [Theory]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE user_id = $1")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1")]
    public async Task RevokedAccessIsRejectedWithExistingToken(string sql)
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client);
        var stage = await CreateStageAsync(client, offer.Path, 1);
        await fixture.QueryAsync(sql, offer.UserId);
        await AssertAccessDeniedAsync(client, offer.Path, stage.Id, HttpStatusCode.NotFound);
        Assert.Equal(stage.Name, await fixture.QueryAsync("SELECT name FROM portal.recruitment_stages WHERE id = $1", stage.Id));
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.recruitment_stages WHERE job_offer_id = $1", offer.JobId));
    }

    [Fact]
    public async Task AnonymousAndCandidateRequestsAreRejected()
    {
        using var client = fixture.CreateClient();
        var path = $"/api/companies/{Guid.NewGuid()}/jobs/{Guid.NewGuid()}/stages";
        await AssertAccessDeniedAsync(client, path, Guid.NewGuid(), HttpStatusCode.Unauthorized);
        await RegisterAsync(client, UserRoles.Candidate);
        await AssertAccessDeniedAsync(client, path, Guid.NewGuid(), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsedStagesKeepTheirIdentityAndReferences(bool usedBySlot)
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client);
        var stage = await CreateStageAsync(client, offer.Path, 1);
        if (usedBySlot)
        {
            await fixture.QueryAsync("""
                INSERT INTO portal.interview_slots
                    (job_offer_id, company_id, stage_id, interviewer_member_id, starts_at, ends_at)
                VALUES ($1, $2, $3, $4, now(), now() + interval '1 hour')
                """, offer.JobId, offer.CompanyId, stage.Id, offer.MemberId);
        }
        else
        {
            using var candidate = fixture.CreateClient();
            var user = await RegisterAsync(candidate, UserRoles.Candidate);
            var applicationId = (Guid)(await fixture.QueryAsync("""
                INSERT INTO portal.applications (job_offer_id, company_id, candidate_id, profile_snapshot)
                VALUES ($1, $2, $3, '{}') RETURNING id
                """, offer.JobId, offer.CompanyId, user.Id))!;
            await fixture.QueryAsync("""
                INSERT INTO portal.application_stages (application_id, stage_id, job_offer_id, company_id)
                VALUES ($1, $2, $3, $4)
                """, applicationId, stage.Id, offer.JobId, offer.CompanyId);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{offer.Path}/{stage.Id}")).StatusCode);
        var request = new UpsertRecruitmentStageRequest("Změna", "test", 2);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{offer.Path}/{stage.Id}", request)).StatusCode);
        Assert.Equal(stage, Assert.Single((await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path))!));

        request = request with { Kind = stage.Kind };
        var response = await client.PutAsJsonAsync($"{offer.Path}/{stage.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(stage with { Name = request.Name, Position = 2 }, await response.Content.ReadFromJsonAsync<RecruitmentStageResponse>());
        response = await client.PutAsJsonAsync($"{offer.Path}/order", new ReorderRecruitmentStagesRequest([stage.Id]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(stage with { Name = request.Name }, Assert.Single((await response.Content.ReadFromJsonAsync<RecruitmentStageResponse[]>())!));
    }

    [Fact]
    public async Task ConcurrentCreatesAtSamePositionHaveOneWinner()
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client);
        var request = new UpsertRecruitmentStageRequest("Pohovor", "interview", 1);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(offer.Path, request), client.PostAsJsonAsync(offer.Path, request));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path))!);
    }

    [Fact]
    public async Task ConcurrentReordersKeepACompleteConsistentOrder()
    {
        using var client = fixture.CreateClient();
        var offer = await CreateOfferAsync(client);
        var first = await CreateStageAsync(client, offer.Path, 1);
        var second = await CreateStageAsync(client, offer.Path, 2);
        var third = await CreateStageAsync(client, offer.Path, 3);
        Guid[][] orders = [[third.Id, first.Id, second.Id], [second.Id, third.Id, first.Id]];
        var responses = await Task.WhenAll(orders.Select(ids => client.PutAsJsonAsync(
            $"{offer.Path}/order", new ReorderRecruitmentStagesRequest(ids))));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var stages = (await client.GetFromJsonAsync<RecruitmentStageResponse[]>(offer.Path))!;
        Assert.Equal(new[] { 1, 2, 3 }, stages.Select(stage => stage.Position));
        Assert.Contains(orders, order => order.SequenceEqual(stages.Select(stage => stage.Id)));
    }

    private static async Task AssertAccessDeniedAsync(
        HttpClient client, string path, Guid stageId, HttpStatusCode status)
    {
        var request = new UpsertRecruitmentStageRequest("Změna", "test", 2);
        Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync($"{path}/order", new ReorderRecruitmentStagesRequest([stageId]))).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync($"{path}/{stageId}", request)).StatusCode);
        Assert.Equal(status, (await client.DeleteAsync($"{path}/{stageId}")).StatusCode);
    }

    private sealed record TestOffer(Guid CompanyId, Guid JobId, Guid MemberId, Guid UserId)
    {
        public string Path => $"/api/companies/{CompanyId}/jobs/{JobId}/stages";
    }

    private async Task<TestOffer> CreateOfferAsync(
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
        return new(companyId, jobId, memberId, currentUserId);
    }

    private static async Task<RecruitmentStageResponse> CreateStageAsync(HttpClient client, string path, int position)
    {
        var response = await client.PostAsJsonAsync(path, new UpsertRecruitmentStageRequest("Pohovor", "interview", position));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecruitmentStageResponse>())!;
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
