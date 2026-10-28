using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.IntegrationTests;

public sealed class CompanyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CreateCompanyStoresFounderAndReturnsReadableLocation()
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        var request = NewCompany();
        request.Name = "  Test company  ";
        request.RegistrationNumber = $"  {request.RegistrationNumber}  ";
        request.Description = "Popis firmy";
        request.WebsiteUrl = "https://example.com";
        request.Address = "Praha";

        var response = await client.PostAsJsonAsync("/api/companies", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;
        Assert.NotEqual(Guid.Empty, created.Company.Id);
        Assert.Equal("Test company", created.Company.Name);
        Assert.Equal(request.RegistrationNumber.Trim(), created.Company.RegistrationNumber);
        Assert.Equal(request.CountryCode, created.Company.CountryCode);
        Assert.Equal(request.Description, created.Company.Description);
        Assert.Equal(request.WebsiteUrl, created.Company.WebsiteUrl);
        Assert.Equal(request.Address, created.Company.Address);
        Assert.NotEqual(default, created.Company.CreatedAt);
        Assert.Equal(created.Company.CreatedAt, created.Company.UpdatedAt);
        Assert.Equal(created.Company.Id, created.Membership.CompanyId);
        Assert.Equal(user.Id, created.Membership.UserId);
        Assert.Equal(user.Email, created.Membership.Email);
        Assert.Equal(user.FirstName, created.Membership.FirstName);
        Assert.Equal(user.LastName, created.Membership.LastName);
        Assert.Equal(CompanyMemberRoles.Owner, created.Membership.Role);
        Assert.True(created.Membership.IsActive);
        Assert.NotEqual(default, created.Membership.CreatedAt);
        Assert.Equal(created.Membership.CreatedAt, created.Membership.UpdatedAt);
        Assert.Equal(1L, await fixture.QueryAsync("""
            SELECT count(*) FROM portal.companies c
            JOIN portal.company_members m ON m.company_id = c.id
            WHERE c.id = $1 AND m.id = $2 AND m.user_id = $3 AND m.role = 'owner' AND m.is_active
            """, created.Company.Id, created.Membership.Id, user.Id));

        Assert.Equal($"/api/companies/{created.Company.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(created, await client.GetFromJsonAsync<CompanyMembershipResponse>(response.Headers.Location));
        Assert.Equal(created, Assert.Single((await client.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!));
    }

    [Fact]
    public async Task CompanyRoleWithoutMembershipCannotAccessAnotherCompany()
    {
        using var first = fixture.CreateClient();
        await RegisterAsync(first);
        var company = await CreateAsync(first);
        using var second = fixture.CreateClient();
        await RegisterAsync(second);
        var ownCompany = await CreateAsync(second);

        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/companies/{company.Company.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/companies/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(ownCompany, Assert.Single((await second.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!));
    }

    [Theory]
    [InlineData(CompanyMemberRoles.Owner)]
    [InlineData(CompanyMemberRoles.Admin)]
    [InlineData(CompanyMemberRoles.Recruiter)]
    public async Task ActiveMembersCanReadCompanyAndRevocationTakesEffectImmediately(string role)
    {
        using var founder = fixture.CreateClient();
        await RegisterAsync(founder);
        var company = await CreateAsync(founder);
        using var member = fixture.CreateClient();
        var user = await RegisterAsync(member);
        await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, $3)
            """, company.Company.Id, user.Id, role);

        var detail = (await member.GetFromJsonAsync<CompanyMembershipResponse>($"/api/companies/{company.Company.Id}"))!;
        Assert.Equal(company.Company, detail.Company);
        Assert.Equal(user.Id, detail.Membership.UserId);
        Assert.Equal(role, detail.Membership.Role);
        Assert.Equal(detail, Assert.Single((await member.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!));

        await fixture.QueryAsync("UPDATE portal.company_members SET is_active = false WHERE id = $1", detail.Membership.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/companies/{company.Company.Id}")).StatusCode);
        Assert.Empty((await member.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!);
    }

    [Theory]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1")]
    public async Task StaleTokenCannotBypassCurrentAccountState(string update)
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        var company = await CreateAsync(client);
        await fixture.QueryAsync(update, user.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/companies/{company.Company.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!);
        var rejected = NewCompany();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/companies", rejected)).StatusCode);
        Assert.Equal(0L, await fixture.QueryAsync("SELECT count(*) FROM portal.companies WHERE registration_number = $1", rejected.RegistrationNumber!));
    }

    [Fact]
    public async Task AnonymousAndCandidateRequestsAreRejected()
    {
        using var client = fixture.CreateClient();
        var path = $"/api/companies/{Guid.NewGuid()}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/companies", NewCompany())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);

        await RegisterAsync(client, UserRoles.Candidate);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/companies", NewCompany())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task DuplicateRegistrationNumberInSameCountryIsRejectedWithoutAddingMembership()
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        var request = NewCompany();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/companies", request)).StatusCode);
        request.RegistrationNumber = $"  {request.RegistrationNumber}  ";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/companies", request)).StatusCode);
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.company_members WHERE user_id = $1", user.Id));
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.companies WHERE registration_number = $1", request.RegistrationNumber.Trim()));

        request.CountryCode = "SK";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/companies", request)).StatusCode);
    }

    [Fact]
    public async Task FailedFounderMembershipRollsBackCompany()
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        var request = NewCompany();
        await fixture.QueryAsync("""
            ALTER TABLE portal.company_members
            ADD CONSTRAINT reject_owner_for_test CHECK (role <> 'owner') NOT VALID
            """);
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError,
                (await client.PostAsJsonAsync("/api/companies", request)).StatusCode);
            Assert.Equal(0L, await fixture.QueryAsync(
                "SELECT count(*) FROM portal.companies WHERE registration_number = $1", request.RegistrationNumber!));
            Assert.Equal(0L, await fixture.QueryAsync(
                "SELECT count(*) FROM portal.company_members WHERE user_id = $1", user.Id));
        }
        finally
        {
            await fixture.QueryAsync("ALTER TABLE portal.company_members DROP CONSTRAINT reject_owner_for_test");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MultipleCompaniesCanOmitRegistrationNumber(string? registrationNumber)
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var request = NewCompany();
        request.RegistrationNumber = registrationNumber;
        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsJsonAsync("/api/companies", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = (await response.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;
            Assert.Null(created.Company.RegistrationNumber);
            Assert.Null(created.Company.Description);
            Assert.Null(created.Company.WebsiteUrl);
            Assert.Null(created.Company.Address);
        }
        Assert.Equal(2, (await client.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!.Length);
    }

    [Theory]
    [InlineData("name", "   ")]
    [InlineData("countryCode", "cz")]
    [InlineData("countryCode", "CZE")]
    [InlineData("websiteUrl", "ftp://example.com")]
    [InlineData("websiteUrl", "invalid")]
    public async Task InvalidCompanyDataIsRejected(string field, string value)
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var request = new Dictionary<string, string>
        {
            ["name"] = "Test company", ["countryCode"] = "CZ", [field] = value
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/companies", request)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CompanyMembershipResponse[]>("/api/companies"))!);
    }

    [Fact]
    public async Task CompanyNameCannotExceedContractLimit()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var request = NewCompany();
        request.Name = new string('a', 201);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/companies", request)).StatusCode);
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

    private static async Task<CompanyMembershipResponse> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/companies", NewCompany());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;
    }

    private static CreateCompanyRequest NewCompany() => new()
    {
        Name = "Test company",
        RegistrationNumber = Guid.NewGuid().ToString("N"),
        CountryCode = "CZ"
    };
}
