using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.IntegrationTests;

public sealed class CompanyMemberTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OwnerCanAddUpdateRemoveAndReactivateMember()
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        using var recruiter = fixture.CreateClient();
        var user = await RegisterAsync(recruiter);
        var path = MembersPath(company);

        var response = await owner.PostAsJsonAsync(path, new AddCompanyMemberRequest
        {
            Email = user.Email.ToUpperInvariant()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = (await response.Content.ReadFromJsonAsync<CompanyMemberResponse>())!;
        Assert.NotEqual(Guid.Empty, member.Id);
        Assert.Equal(new CompanyMemberResponse(member.Id, company.Company.Id, user.Id,
            user.Email, user.FirstName, user.LastName, CompanyMemberRoles.Recruiter, true,
            member.CreatedAt, member.CreatedAt), member);
        Assert.NotEqual(default, member.CreatedAt);
        Assert.Equal(path, response.Headers.Location?.AbsolutePath);
        var listed = (await recruiter.GetFromJsonAsync<CompanyMemberResponse[]>(response.Headers.Location))!;
        Assert.Equal(2, listed.Length);
        Assert.Contains(member, listed);
        Assert.Contains(company.Membership, listed);
        Assert.Equal(member, (await recruiter.GetFromJsonAsync<CompanyMembershipResponse>($"/api/companies/{company.Company.Id}"))!.Membership);

        var updatedResponse = await owner.PatchAsJsonAsync($"{path}/{member.Id}", new UpdateCompanyMemberRequest
        {
            Role = CompanyMemberRoles.Admin
        });
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<CompanyMemberResponse>())!;
        Assert.Equal(member with { Role = CompanyMemberRoles.Admin, UpdatedAt = updated.UpdatedAt }, updated);
        Assert.True(updated.UpdatedAt >= member.UpdatedAt);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{path}/{member.Id}")).StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<CompanyMemberResponse[]>(path))!);
        Assert.Equal(HttpStatusCode.NotFound, (await recruiter.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recruiter.GetAsync($"/api/companies/{company.Company.Id}")).StatusCode);
        Assert.Empty((await recruiter.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{path}/{member.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PatchAsJsonAsync($"{path}/{member.Id}",
            new UpdateCompanyMemberRequest { Role = CompanyMemberRoles.Admin })).StatusCode);

        var reactivated = await AddAsync(owner, path, user.Email);
        Assert.Equal(member with { UpdatedAt = reactivated.UpdatedAt }, reactivated);
        Assert.Equal(2, (await recruiter.GetFromJsonAsync<CompanyMemberResponse[]>(path))!.Length);
    }

    [Fact]
    public async Task AdminCanManageTeamAndDemotionImmediatelyRevokesManagement()
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        var path = MembersPath(company);
        using var admin = fixture.CreateClient();
        var adminUser = await RegisterAsync(admin);
        var adminMember = await AddAsync(owner, path, adminUser.Email, CompanyMemberRoles.Admin);
        using var recruiter = fixture.CreateClient();
        var recruiterUser = await RegisterAsync(recruiter);

        var member = await AddAsync(admin, path, recruiterUser.Email);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"{path}/{member.Id}",
            new UpdateCompanyMemberRequest { Role = CompanyMemberRoles.Admin })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{path}/{member.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync($"{path}/{adminMember.Id}",
            new UpdateCompanyMemberRequest { Role = CompanyMemberRoles.Recruiter })).StatusCode);

        await AssertManagementDeniedAsync(admin, path, adminMember, HttpStatusCode.Forbidden);
        Assert.Equal(2, (await admin.GetFromJsonAsync<CompanyMemberResponse[]>(path))!.Length);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{path}/{adminMember.Id}")).StatusCode);
        await AssertManagementDeniedAsync(admin, path, adminMember, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OwnerMembershipCannotBeChangedOrRemoved()
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        var path = MembersPath(company);
        using var admin = fixture.CreateClient();
        var user = await RegisterAsync(admin);
        await AddAsync(owner, path, user.Email, CompanyMemberRoles.Admin);

        foreach (var actor in new[] { owner, admin })
            await AssertManagementDeniedAsync(actor, path, company.Membership, HttpStatusCode.Conflict);

        Assert.Equal(company.Membership, (await owner.GetFromJsonAsync<CompanyMembershipResponse>($"/api/companies/{company.Company.Id}"))!.Membership);
    }

    [Fact]
    public async Task DuplicateConcurrentAddsCreateOneMemberWithoutChangingRole()
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        var path = MembersPath(company);
        using var memberClient = fixture.CreateClient();
        var user = await RegisterAsync(memberClient);
        var request = new AddCompanyMemberRequest { Email = user.Email, Role = CompanyMemberRoles.Admin };

        var responses = await Task.WhenAll(owner.PostAsJsonAsync(path, request), owner.PostAsJsonAsync(path, request));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path,
            new AddCompanyMemberRequest { Email = user.Email })).StatusCode);
        var members = (await owner.GetFromJsonAsync<CompanyMemberResponse[]>(path))!;
        Assert.Equal(2, members.Length);
        Assert.Equal(CompanyMemberRoles.Admin, Assert.Single(members, member => member.UserId == user.Id).Role);
    }

    [Fact]
    public async Task CompanyAndMemberIdsCannotBeUsedAcrossCompanies()
    {
        using var owner = fixture.CreateClient();
        var first = await RegisterCompanyAsync(owner);
        var second = await CreateCompanyAsync(owner);
        var firstPath = MembersPath(first);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PatchAsJsonAsync($"{firstPath}/{second.Membership.Id}",
            new UpdateCompanyMemberRequest { Role = CompanyMemberRoles.Admin })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{firstPath}/{second.Membership.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{firstPath}/{Guid.NewGuid()}")).StatusCode);

        using var outsider = fixture.CreateClient();
        await RegisterAsync(outsider);
        foreach (var path in new[] { firstPath, $"/api/companies/{Guid.NewGuid()}/members" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
            await AssertManagementDeniedAsync(outsider, path, first.Membership, HttpStatusCode.NotFound);
        }
    }

    [Theory]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1")]
    public async Task CurrentAccountStateIsCheckedForEveryTeamOperation(string update)
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        var path = MembersPath(company);
        await fixture.QueryAsync(update, company.Membership.UserId);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path)).StatusCode);
        await AssertManagementDeniedAsync(owner, path, company.Membership, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnlyExistingActiveCompanyAccountsCanBeAdded()
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        var path = MembersPath(company);
        using var candidateClient = fixture.CreateClient();
        var candidate = await RegisterAsync(candidateClient, UserRoles.Candidate);
        using var inactiveClient = fixture.CreateClient();
        var inactive = await RegisterAsync(inactiveClient);
        await fixture.QueryAsync("UPDATE portal.users SET is_active = false WHERE id = $1", inactive.Id);

        foreach (var email in new[] { candidate.Email, inactive.Email, "missing@example.com" })
            Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(path,
                new AddCompanyMemberRequest { Email = email })).StatusCode);

        Assert.Single((await owner.GetFromJsonAsync<CompanyMemberResponse[]>(path))!);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(UserRoles.Candidate, HttpStatusCode.Forbidden)]
    public async Task AnonymousAndCandidateRequestsAreRejected(string? role, HttpStatusCode status)
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        using var client = fixture.CreateClient();
        if (role is not null)
            await RegisterAsync(client, role);

        var path = MembersPath(company);
        Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
        await AssertManagementDeniedAsync(client, path, company.Membership, status);
    }

    [Theory]
    [InlineData("email", "invalid")]
    [InlineData("email", "")]
    [InlineData("email", null)]
    [InlineData("role", "owner")]
    [InlineData("role", "invalid")]
    [InlineData("role", "")]
    [InlineData("role", null)]
    public async Task InvalidMemberDataIsRejected(string field, string? value)
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        var path = MembersPath(company);
        var request = new Dictionary<string, string?>
        {
            ["email"] = "hr@example.com", ["role"] = CompanyMemberRoles.Recruiter, [field] = value
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, request)).StatusCode);
        if (field == "role")
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"{path}/{company.Membership.Id}", request)).StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<CompanyMemberResponse[]>(path))!);
    }

    [Fact]
    public async Task RemovalPreservesOfferAndApplicationReferences()
    {
        using var owner = fixture.CreateClient();
        var company = await RegisterCompanyAsync(owner);
        var path = MembersPath(company);
        using var recruiter = fixture.CreateClient();
        var user = await RegisterAsync(recruiter);
        var member = await AddAsync(owner, path, user.Email);
        using var candidateClient = fixture.CreateClient();
        var candidate = await RegisterAsync(candidateClient, UserRoles.Candidate);
        var offerId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.job_offers (company_id, created_by_member_id, title, description, work_mode, employment_type)
            VALUES ($1, $2, 'Test offer', 'Description', 'remote', 'full_time') RETURNING id
            """, company.Company.Id, member.Id))!;
        var applicationId = await fixture.QueryAsync("""
            INSERT INTO portal.applications (job_offer_id, company_id, candidate_id, assigned_member_id, profile_snapshot)
            VALUES ($1, $2, $3, $4, '{}') RETURNING id
            """, offerId, company.Company.Id, candidate.Id, member.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{path}/{member.Id}")).StatusCode);
        Assert.Equal(member.Id, await fixture.QueryAsync("SELECT created_by_member_id FROM portal.job_offers WHERE id = $1", offerId));
        Assert.Equal(member.Id, await fixture.QueryAsync("SELECT assigned_member_id FROM portal.applications WHERE id = $1", applicationId!));
        Assert.Equal(false, await fixture.QueryAsync("SELECT is_active FROM portal.company_members WHERE id = $1", member.Id));
    }

    private static string MembersPath(CompanyMembershipResponse company) => $"/api/companies/{company.Company.Id}/members";

    private static async Task AssertManagementDeniedAsync(
        HttpClient client, string path, CompanyMemberResponse member, HttpStatusCode status)
    {
        Assert.Equal(status, (await client.PostAsJsonAsync(path,
            new AddCompanyMemberRequest { Email = member.Email })).StatusCode);
        Assert.Equal(status, (await client.PatchAsJsonAsync($"{path}/{member.Id}",
            new UpdateCompanyMemberRequest { Role = CompanyMemberRoles.Admin })).StatusCode);
        Assert.Equal(status, (await client.DeleteAsync($"{path}/{member.Id}")).StatusCode);
    }

    private static async Task<CompanyMembershipResponse> RegisterCompanyAsync(HttpClient client)
    {
        await RegisterAsync(client);
        return await CreateCompanyAsync(client);
    }

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role = UserRoles.Company)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "valid-password-123",
            ConfirmPassword = "valid-password-123",
            FirstName = "Test",
            LastName = "User",
            Role = role
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth.User;
    }

    private static async Task<CompanyMembershipResponse> CreateCompanyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest
        {
            Name = "Test company", CountryCode = "CZ"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;
    }

    private static async Task<CompanyMemberResponse> AddAsync(
        HttpClient client, string path, string email, string role = CompanyMemberRoles.Recruiter)
    {
        var response = await client.PostAsJsonAsync(path, new AddCompanyMemberRequest { Email = email, Role = role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyMemberResponse>())!;
    }
}
