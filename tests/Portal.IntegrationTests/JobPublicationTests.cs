using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Jobs;
using static System.Net.HttpStatusCode;

namespace Portal.IntegrationTests;

public sealed class JobPublicationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData(CompanyMemberRoles.Owner)]
    [InlineData(CompanyMemberRoles.Admin)]
    [InlineData(CompanyMemberRoles.Recruiter)]
    public async Task ActiveMembersCanPublishAndCloseAnotherMembersOffer(string role)
    {
        using var owner = fixture.CreateClient();
        var (company, draft) = await CreateOfferAsync(owner);
        using var member = fixture.CreateClient();
        var user = await RegisterAsync(member);
        await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, $3)
            """, company.Company.Id, user.Id, role);
        using var visitor = fixture.CreateClient();
        await AssertHiddenAsync(visitor, draft.Id);

        var published = await TransitionAsync(member, Path(company, draft), "publish");
        Assert.True(published.PublishedAt > draft.CreatedAt);
        Assert.True(published.UpdatedAt > draft.UpdatedAt);
        Assert.Equal(draft with
        {
            Status = JobOfferStatuses.Published,
            PublishedAt = published.PublishedAt,
            UpdatedAt = published.UpdatedAt
        }, published);
        Assert.Equal(published, await owner.GetFromJsonAsync<JobOfferManagementResponse>(Path(company, draft)));
        Assert.Contains((await visitor.GetFromJsonAsync<JobOfferResponse[]>("/api/jobs?limit=100"))!,
            job => job.Id == draft.Id);
        await AssertPublicDetailAsync(visitor, company, published);

        var closed = await TransitionAsync(member, Path(company, draft), "close");
        Assert.True(closed.UpdatedAt > published.UpdatedAt);
        Assert.Equal(published with { Status = JobOfferStatuses.Closed, UpdatedAt = closed.UpdatedAt }, closed);
        Assert.Equal(closed, await owner.GetFromJsonAsync<JobOfferManagementResponse>(Path(company, draft)));
        await AssertHiddenAsync(visitor, draft.Id);
    }

    [Fact]
    public async Task PublicDetailIncludesOptionalJobAndCompanyFields()
    {
        using var client = fixture.CreateClient();
        var request = NewJob();
        request.Requirements = "C# a SQL";
        request.Location = "Brno";
        request.SalaryMin = 60000.25m;
        request.SalaryMax = 90000.50m;
        request.SalaryCurrency = "CZK";
        request.SalaryPeriod = SalaryPeriods.Month;
        request.ApplicationDeadline = DateTimeOffset.UtcNow.AddDays(1);
        var (company, draft) = await CreateOfferAsync(client, request: request, companyRequest: new CreateCompanyRequest
        {
            Name = "Firma s údaji",
            CountryCode = "CZ",
            RegistrationNumber = Guid.NewGuid().ToString("N"),
            Description = "Popis firmy",
            WebsiteUrl = "https://example.com",
            Address = "Praha"
        });
        var published = await TransitionAsync(client, Path(company, draft), "publish");
        client.DefaultRequestHeaders.Authorization = null;
        await AssertPublicDetailAsync(client, company, published);
        Assert.Equal(NotFound, (await client.GetAsync($"/api/jobs/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData(JobOfferStatuses.Draft, "close")]
    [InlineData(JobOfferStatuses.Closed, "publish")]
    [InlineData(JobOfferStatuses.Archived, "publish")]
    [InlineData(JobOfferStatuses.Archived, "close")]
    public async Task InvalidTransitionsLeaveOfferUnchangedAndHidden(string status, string action)
    {
        using var client = fixture.CreateClient();
        var (company, before) = await CreateOfferAsync(client, status);
        var path = Path(company, before);

        var response = await client.PostAsync($"{path}/{action}", null);
        Assert.Equal(Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await client.GetFromJsonAsync<JobOfferManagementResponse>(path));
        client.DefaultRequestHeaders.Authorization = null;
        await AssertHiddenAsync(client, before.Id);
    }

    [Fact]
    public async Task ExpiredDraftCannotBePublishedButExpiredPublishedOfferCanBeClosed()
    {
        using var client = fixture.CreateClient();
        var request = NewJob();
        request.ApplicationDeadline = DateTimeOffset.UtcNow.AddDays(-1);
        var (company, draft) = await CreateOfferAsync(client, request: request);
        var response = await client.PostAsync($"{Path(company, draft)}/publish", null);
        Assert.Equal(Conflict, response.StatusCode);
        Assert.Equal(draft, await client.GetFromJsonAsync<JobOfferManagementResponse>(Path(company, draft)));

        await fixture.QueryAsync("""
            UPDATE portal.job_offers SET status = 'published', published_at = application_deadline - interval '1 day'
            WHERE id = $1
            """, draft.Id);
        using var visitor = fixture.CreateClient();
        await AssertHiddenAsync(visitor, draft.Id);
        var closed = await TransitionAsync(client, Path(company, draft), "close");
        Assert.Equal(JobOfferStatuses.Closed, closed.Status);
        Assert.Equal(draft.ApplicationDeadline!.Value.AddDays(-1), closed.PublishedAt);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("close")]
    public async Task OnlyCompanyMembersCanChangeOfferStatus(string action)
    {
        using var owner = fixture.CreateClient();
        var (company, draft) = await CreateOfferAsync(owner,
            action == "close" ? JobOfferStatuses.Published : JobOfferStatuses.Draft);
        var anotherCompany = await CreateCompanyAsync(owner);
        using var outsider = fixture.CreateClient();
        var path = $"{Path(company, draft)}/{action}";
        Assert.Equal(Unauthorized, (await outsider.PostAsync(path, null)).StatusCode);
        await RegisterAsync(outsider, UserRoles.Candidate);
        Assert.Equal(Forbidden, (await outsider.PostAsync(path, null)).StatusCode);
        await RegisterAsync(outsider);

        Assert.Equal(NotFound, (await outsider.PostAsync($"{Path(company, draft)}/{action}", null)).StatusCode);
        Assert.Equal(NotFound, (await owner.PostAsync($"{Path(anotherCompany, draft)}/{action}", null)).StatusCode);
        Assert.Equal(NotFound, (await owner.PostAsync($"/api/companies/{Guid.NewGuid()}/jobs/{draft.Id}/{action}", null)).StatusCode);
        Assert.Equal(NotFound, (await owner.PostAsync($"/api/companies/{company.Company.Id}/jobs/{Guid.NewGuid()}/{action}", null)).StatusCode);
        Assert.Equal(draft, await owner.GetFromJsonAsync<JobOfferManagementResponse>(Path(company, draft)));
    }

    [Theory]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE user_id = $1", "publish")]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE user_id = $1", "close")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1", "publish")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1", "close")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1", "publish")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1", "close")]
    public async Task RevokedAccessPreventsTransitionsWithPreviouslyIssuedToken(string update, string action)
    {
        using var client = fixture.CreateClient();
        var (company, before) = await CreateOfferAsync(client,
            action == "close" ? JobOfferStatuses.Published : JobOfferStatuses.Draft);
        await fixture.QueryAsync(update, company.Membership.UserId);

        Assert.Equal(NotFound, (await client.PostAsync($"{Path(company, before)}/{action}", null)).StatusCode);
        Assert.Equal(before.Status, await fixture.QueryAsync("SELECT status FROM portal.job_offers WHERE id = $1", before.Id));
        Assert.Equal(before.UpdatedAt.UtcDateTime, await fixture.QueryAsync("SELECT updated_at FROM portal.job_offers WHERE id = $1", before.Id));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("close")]
    public async Task ConcurrentTransitionsSucceedOnlyOnce(string action)
    {
        using var client = fixture.CreateClient();
        var (company, draft) = await CreateOfferAsync(client,
            action == "close" ? JobOfferStatuses.Published : JobOfferStatuses.Draft);
        var path = $"{Path(company, draft)}/{action}";

        var responses = await Task.WhenAll(client.PostAsync(path, null), client.PostAsync(path, null));
        Assert.Single(responses, response => response.StatusCode == OK);
        Assert.Single(responses, response => response.StatusCode == Conflict);
        var changed = await responses.Single(response => response.StatusCode == OK)
            .Content.ReadFromJsonAsync<JobOfferManagementResponse>();
        Assert.Equal(changed, await client.GetFromJsonAsync<JobOfferManagementResponse>(Path(company, draft)));
    }

    [Fact]
    public async Task PublicListUsesPaginationAndStablePublicationOrdering()
    {
        using var client = fixture.CreateClient();
        var (company, first) = await CreateOfferAsync(client);
        var second = await CreateJobAsync(client, company);
        var older = await CreateJobAsync(client, company);
        await fixture.QueryAsync("""
            UPDATE portal.job_offers SET status = 'published', published_at = '2100-01-02T00:00:00Z'
            WHERE id IN ($1, $2)
            """, first.Id, second.Id);
        await fixture.QueryAsync("""
            UPDATE portal.job_offers SET status = 'published', published_at = '2100-01-01T00:00:00Z' WHERE id = $1
            """, older.Id);
        client.DefaultRequestHeaders.Authorization = null;
        var expected = new[] { first.Id, second.Id }.Order().Append(older.Id).ToArray();

        var jobs = (await client.GetFromJsonAsync<JobOfferResponse[]>("/api/jobs?limit=3"))!;
        Assert.Equal(expected, jobs.Select(job => job.Id));
        Assert.All(jobs, job => Assert.Equal(new JobOfferResponse(
            job.Id, "Vývojář", company.Company.Name, null, WorkModes.Remote), job));
        var page = (await client.GetFromJsonAsync<JobOfferResponse[]>("/api/jobs?offset=1&limit=1"))!;
        Assert.Equal(expected[1], Assert.Single(page).Id);
        Assert.Empty((await client.GetFromJsonAsync<JobOfferResponse[]>("/api/jobs?offset=1000"))!);
        await fixture.QueryAsync("UPDATE portal.job_offers SET status = 'closed' WHERE company_id = $1", company.Company.Id);
    }

    private async Task<(CompanyMembershipResponse Company, JobOfferManagementResponse Job)> CreateOfferAsync(
        HttpClient client, string status = JobOfferStatuses.Draft,
        JobOfferRequest? request = null, CreateCompanyRequest? companyRequest = null)
    {
        await RegisterAsync(client);
        var company = await CreateCompanyAsync(client, companyRequest);
        var job = await CreateJobAsync(client, company, request);
        if (status != JobOfferStatuses.Draft)
        {
            await fixture.QueryAsync("UPDATE portal.job_offers SET status = $2, published_at = now() WHERE id = $1", job.Id, status);
            job = (await client.GetFromJsonAsync<JobOfferManagementResponse>(Path(company, job)))!;
        }
        return (company, job);
    }

    private static Task<JobOfferManagementResponse> TransitionAsync(HttpClient client, string path, string action) =>
        ReadAsync<JobOfferManagementResponse>(client.PostAsync($"{path}/{action}", null), OK);

    private static async Task AssertHiddenAsync(HttpClient client, Guid jobId)
    {
        using var response = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain((await client.GetFromJsonAsync<JobOfferResponse[]>("/api/jobs?limit=100"))!,
            job => job.Id == jobId);
    }

    private static async Task AssertPublicDetailAsync(
        HttpClient client, CompanyMembershipResponse company, JobOfferManagementResponse job)
    {
        using var response = await client.GetAsync($"/api/jobs/{job.Id}");
        Assert.Equal(OK, response.StatusCode);
        Assert.Equal(new JobOfferDetailResponse(
            job.Id, company.Company, job.Title, job.Description, job.Requirements,
            job.Location, job.WorkMode, job.EmploymentType, job.SalaryMin, job.SalaryMax,
            job.SalaryCurrency, job.SalaryPeriod, job.PublishedAt!.Value, job.ApplicationDeadline),
            await response.Content.ReadFromJsonAsync<JobOfferDetailResponse>());
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("createdByMemberId", json);
        Assert.DoesNotContain("membership", json);
    }

    private static string Path(CompanyMembershipResponse company, JobOfferManagementResponse job) =>
        $"/api/companies/{company.Company.Id}/jobs/{job.Id}";

    private static JobOfferRequest NewJob() => new()
    {
        Title = "Vývojář",
        Description = "Vývoj webových aplikací",
        WorkMode = WorkModes.Remote,
        EmploymentType = EmploymentTypes.FullTime
    };

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role = UserRoles.Company)
    {
        var auth = await ReadAsync<AuthResponse>(client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "Valid-password-123",
            ConfirmPassword = "Valid-password-123",
            FirstName = "Test",
            LastName = "User",
            Role = role
        }), Created);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth.User;
    }

    private static Task<CompanyMembershipResponse> CreateCompanyAsync(HttpClient client, CreateCompanyRequest? request = null) =>
        ReadAsync<CompanyMembershipResponse>(client.PostAsJsonAsync("/api/companies",
            request ?? new CreateCompanyRequest { Name = "Test company", CountryCode = "CZ" }), Created);

    private static Task<JobOfferManagementResponse> CreateJobAsync(
        HttpClient client, CompanyMembershipResponse company, JobOfferRequest? request = null) =>
        ReadAsync<JobOfferManagementResponse>(client.PostAsJsonAsync(
            $"/api/companies/{company.Company.Id}/jobs", request ?? NewJob()), Created);

    private static async Task<T> ReadAsync<T>(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
