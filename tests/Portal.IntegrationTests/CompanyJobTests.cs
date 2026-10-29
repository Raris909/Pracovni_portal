using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Jobs;

namespace Portal.IntegrationTests;

public sealed class CompanyJobTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CreateDraftStoresFieldsAndReturnsReadableLocation()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var company = await CreateCompanyAsync(client);
        var request = NewJob();
        request.Title = "  Vývojář .NET  ";
        request.Requirements = "Znalost C# a SQL";
        request.Location = "Praha";
        request.SalaryMin = 60000.25m;
        request.SalaryMax = 90000.50m;
        request.SalaryCurrency = "CZK";
        request.SalaryPeriod = SalaryPeriods.Month;
        request.ApplicationDeadline = new DateTimeOffset(2027, 1, 15, 12, 0, 0, TimeSpan.FromHours(1));

        var response = await client.PostAsJsonAsync(Path(company), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal(company.Company.Id, job.CompanyId);
        Assert.Equal(company.Membership.Id, job.CreatedByMemberId);
        Assert.Equal(request.Title.Trim(), job.Title);
        Assert.Equal(request.Description, job.Description);
        Assert.Equal(request.Requirements, job.Requirements);
        Assert.Equal(request.Location, job.Location);
        Assert.Equal(request.WorkMode, job.WorkMode);
        Assert.Equal(request.EmploymentType, job.EmploymentType);
        Assert.Equal(request.SalaryMin, job.SalaryMin);
        Assert.Equal(request.SalaryMax, job.SalaryMax);
        Assert.Equal(request.SalaryCurrency, job.SalaryCurrency);
        Assert.Equal(request.SalaryPeriod, job.SalaryPeriod);
        Assert.Equal(request.ApplicationDeadline, job.ApplicationDeadline);
        Assert.Equal(JobOfferStatuses.Draft, job.Status);
        Assert.Null(job.PublishedAt);
        Assert.NotEqual(default, job.CreatedAt);
        Assert.Equal(job.CreatedAt, job.UpdatedAt);
        Assert.Equal($"{Path(company)}/{job.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(job, await client.GetFromJsonAsync<JobOfferManagementResponse>(response.Headers.Location));
        Assert.Equal(job, Assert.Single((await client.GetFromJsonAsync<JobOfferManagementResponse[]>(Path(company)))!));
        Assert.Equal(1L, await fixture.QueryAsync("""
            SELECT count(*) FROM portal.job_offers
            WHERE id = $1 AND company_id = $2 AND created_by_member_id = $3 AND status = 'draft'
            """, job.Id, company.Company.Id, company.Membership.Id));
        Assert.DoesNotContain((await client.GetFromJsonAsync<JobOfferResponse[]>("/api/jobs?limit=100"))!,
            item => item.Id == job.Id);
    }

    [Theory]
    [InlineData(CompanyMemberRoles.Owner)]
    [InlineData(CompanyMemberRoles.Admin)]
    [InlineData(CompanyMemberRoles.Recruiter)]
    public async Task AnyActiveMemberCanCreateAndEditCompanyDrafts(string role)
    {
        using var owner = fixture.CreateClient();
        await RegisterAsync(owner);
        var company = await CreateCompanyAsync(owner);
        var original = await CreateJobAsync(owner, company);
        using var member = fixture.CreateClient();
        var user = await RegisterAsync(member);
        var memberId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, $3) RETURNING id
            """, company.Company.Id, user.Id, role))!;
        var memberDraft = await CreateJobAsync(member, company);
        Assert.Equal(memberId, memberDraft.CreatedByMemberId);

        var request = NewJob();
        request.Title = "Upravený koncept";
        request.Description = "Nový popis";
        request.Requirements = "Nové požadavky";
        request.Location = "Brno";
        request.WorkMode = WorkModes.Onsite;
        request.EmploymentType = EmploymentTypes.PartTime;
        request.SalaryMax = 50000;
        request.SalaryCurrency = "CZK";
        request.SalaryPeriod = SalaryPeriods.Month;
        request.ApplicationDeadline = DateTimeOffset.Parse("2027-02-01T12:00:00Z");
        var response = await member.PutAsJsonAsync($"{Path(company)}/{original.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
        Assert.Equal(original with
        {
            Title = request.Title, Description = request.Description, Requirements = request.Requirements,
            Location = request.Location, WorkMode = request.WorkMode, EmploymentType = request.EmploymentType,
            SalaryMax = request.SalaryMax, SalaryCurrency = request.SalaryCurrency, SalaryPeriod = request.SalaryPeriod,
            ApplicationDeadline = request.ApplicationDeadline, UpdatedAt = updated.UpdatedAt
        }, updated);
        Assert.True(updated.UpdatedAt > original.UpdatedAt);
        Assert.Equal(updated, await owner.GetFromJsonAsync<JobOfferManagementResponse>($"{Path(company)}/{original.Id}"));

        response = await member.PutAsJsonAsync($"{Path(company)}/{original.Id}", NewJob());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cleared = (await response.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
        Assert.Equal("", cleared.Requirements);
        Assert.Null(cleared.Location);
        Assert.Null(cleared.SalaryMin);
        Assert.Null(cleared.SalaryMax);
        Assert.Null(cleared.SalaryCurrency);
        Assert.Null(cleared.SalaryPeriod);
        Assert.Null(cleared.ApplicationDeadline);
        Assert.Equal(original.CreatedByMemberId, cleared.CreatedByMemberId);
        Assert.Equal(JobOfferStatuses.Draft, cleared.Status);
        Assert.Null(cleared.PublishedAt);
    }

    [Fact]
    public async Task ClientCannotAssignIdentityCreatorCompanyOrPublicationState()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var company = await CreateCompanyAsync(client);
        var request = new
        {
            title = "Test", description = "Description", requirements = "",
            workMode = WorkModes.Remote, employmentType = EmploymentTypes.FullTime,
            id = Guid.NewGuid(), companyId = Guid.NewGuid(), createdByMemberId = Guid.NewGuid(),
            status = JobOfferStatuses.Published, publishedAt = "2026-01-01T00:00:00Z",
            createdAt = "2026-01-01T00:00:00Z", updatedAt = "2026-01-01T00:00:00Z"
        };
        var response = await client.PostAsJsonAsync(Path(company), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
        Assert.NotEqual(request.id, created.Id);
        Assert.Equal(company.Company.Id, created.CompanyId);
        Assert.Equal(company.Membership.Id, created.CreatedByMemberId);
        Assert.Equal(JobOfferStatuses.Draft, created.Status);
        Assert.Null(created.PublishedAt);

        response = await client.PutAsJsonAsync($"{Path(company)}/{created.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
        Assert.Equal(created with { UpdatedAt = updated.UpdatedAt }, updated);
    }

    [Theory]
    [InlineData(JobOfferStatuses.Published)]
    [InlineData(JobOfferStatuses.Closed)]
    [InlineData(JobOfferStatuses.Archived)]
    public async Task NonDraftOffersCanBeReadButCannotBeEdited(string status)
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var company = await CreateCompanyAsync(client);
        var job = await CreateJobAsync(client, company);
        await fixture.QueryAsync("UPDATE portal.job_offers SET status = $2, published_at = now() WHERE id = $1", job.Id, status);
        var path = $"{Path(company)}/{job.Id}";
        var before = await client.GetFromJsonAsync<JobOfferManagementResponse>(path);
        Assert.Equal(status, before!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, NewJob())).StatusCode);
        Assert.Equal(before, await client.GetFromJsonAsync<JobOfferManagementResponse>(path));
        Assert.Equal(before, Assert.Single((await client.GetFromJsonAsync<JobOfferManagementResponse[]>(Path(company)))!));
    }

    [Fact]
    public async Task OffersAreIsolatedByCompanyEvenForMemberOfBothCompanies()
    {
        using var first = fixture.CreateClient();
        await RegisterAsync(first);
        var company = await CreateCompanyAsync(first);
        var job = await CreateJobAsync(first, company);
        using var second = fixture.CreateClient();
        await RegisterAsync(second);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsJsonAsync(Path(company), NewJob())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync(Path(company))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"{Path(company)}/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PutAsJsonAsync($"{Path(company)}/{job.Id}", NewJob())).StatusCode);

        var anotherCompany = await CreateCompanyAsync(first);
        Assert.Empty((await first.GetFromJsonAsync<JobOfferManagementResponse[]>(Path(anotherCompany)))!);
        foreach (var jobId in new[] { job.Id, Guid.NewGuid() })
        {
            var path = $"{Path(anotherCompany)}/{jobId}";
            Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await first.PutAsJsonAsync(path, NewJob())).StatusCode);
        }
        var missingCompany = $"/api/companies/{Guid.NewGuid()}/jobs";
        Assert.Equal(HttpStatusCode.NotFound, (await first.PostAsJsonAsync(missingCompany, NewJob())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync(missingCompany)).StatusCode);
        Assert.Equal(job, await first.GetFromJsonAsync<JobOfferManagementResponse>($"{Path(company)}/{job.Id}"));
    }

    [Theory]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE user_id = $1")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1")]
    public async Task RevokedAccessIsRejectedWithPreviouslyIssuedToken(string update)
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        var company = await CreateCompanyAsync(client);
        var job = await CreateJobAsync(client, company);
        await fixture.QueryAsync(update, user.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path(company))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Path(company)}/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Path(company), NewJob())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{Path(company)}/{job.Id}", NewJob())).StatusCode);
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.job_offers WHERE company_id = $1", company.Company.Id));
        Assert.Equal(job.UpdatedAt.UtcDateTime, await fixture.QueryAsync("SELECT updated_at FROM portal.job_offers WHERE id = $1", job.Id));
    }

    [Fact]
    public async Task AnonymousAndCandidateRequestsAreRejected()
    {
        using var client = fixture.CreateClient();
        var path = $"/api/companies/{Guid.NewGuid()}/jobs";
        foreach (var expected in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        {
            Assert.Equal(expected, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"{path}/{Guid.NewGuid()}")).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync(path, NewJob())).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync($"{path}/{Guid.NewGuid()}", NewJob())).StatusCode);
            if (expected == HttpStatusCode.Unauthorized)
                await RegisterAsync(client, UserRoles.Candidate);
        }
    }

    [Theory]
    [InlineData("title", "   ")]
    [InlineData("description", "   ")]
    [InlineData("requirements", null)]
    [InlineData("workMode", "office")]
    [InlineData("employmentType", "full-time")]
    [InlineData("salaryMin", "-1")]
    [InlineData("salaryMax", "10000000000")]
    [InlineData("salaryMin", "1.001")]
    [InlineData("salaryMax", "1.001")]
    [InlineData("salaryCurrency", "czk")]
    [InlineData("salaryPeriod", "week")]
    [InlineData("applicationDeadline", "invalid")]
    public async Task InvalidDataIsRejectedWithoutChangingDraft(string field, string? value)
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var company = await CreateCompanyAsync(client);
        var job = await CreateJobAsync(client, company);
        var request = new Dictionary<string, object?>
        {
            ["title"] = "Test", ["description"] = "Description", ["requirements"] = "",
            ["workMode"] = WorkModes.Remote, ["employmentType"] = EmploymentTypes.FullTime,
            [field] = field is "salaryMin" or "salaryMax"
                ? decimal.Parse(value!, System.Globalization.CultureInfo.InvariantCulture) : value
        };
        if (field is "salaryMin" or "salaryMax")
        {
            request["salaryCurrency"] = "CZK";
            request["salaryPeriod"] = SalaryPeriods.Month;
        }
        var create = await client.PostAsJsonAsync(Path(company), request);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal("application/problem+json", create.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"{Path(company)}/{job.Id}", request)).StatusCode);
        Assert.Equal(job, await client.GetFromJsonAsync<JobOfferManagementResponse>($"{Path(company)}/{job.Id}"));
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.job_offers WHERE company_id = $1", company.Company.Id));
    }

    [Fact]
    public async Task SalaryDependenciesAndTextLimitsAreValidated()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var company = await CreateCompanyAsync(client);
        var invalidRequests = new List<JobOfferRequest>();
        var missingSalaryMetadata = NewJob();
        missingSalaryMetadata.SalaryMin = 100;
        invalidRequests.Add(missingSalaryMetadata);
        var missingAmount = NewJob();
        missingAmount.SalaryCurrency = "CZK";
        missingAmount.SalaryPeriod = SalaryPeriods.Month;
        invalidRequests.Add(missingAmount);
        var reversedRange = NewJob();
        reversedRange.SalaryMin = 200;
        reversedRange.SalaryMax = 100;
        reversedRange.SalaryCurrency = "CZK";
        reversedRange.SalaryPeriod = SalaryPeriods.Month;
        invalidRequests.Add(reversedRange);
        foreach (var field in new[] { "Title", "Description", "Requirements", "Location" })
        {
            var request = NewJob();
            typeof(JobOfferRequest).GetProperty(field)!.SetValue(request,
                new string('a', field is "Title" or "Location" ? 201 : 20001));
            invalidRequests.Add(request);
        }
        foreach (var request in invalidRequests)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(company), request)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JobOfferManagementResponse[]>(Path(company)))!);
    }

    [Fact]
    public async Task ManagementListUsesPaginationAndStableOrdering()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client);
        var company = await CreateCompanyAsync(client);
        var first = await CreateJobAsync(client, company);
        var second = await CreateJobAsync(client, company);
        await fixture.QueryAsync("UPDATE portal.job_offers SET created_at = '2026-01-01T00:00:00Z' WHERE company_id = $1", company.Company.Id);
        var expected = new[] { first.Id, second.Id }.Order().ToArray();
        var jobs = (await client.GetFromJsonAsync<JobOfferManagementResponse[]>(Path(company)))!;
        Assert.Equal(expected, jobs.Select(job => job.Id));
        var page = (await client.GetFromJsonAsync<JobOfferManagementResponse[]>($"{Path(company)}?offset=1&limit=1"))!;
        Assert.Equal(expected[1], Assert.Single(page).Id);
        Assert.Empty((await client.GetFromJsonAsync<JobOfferManagementResponse[]>($"{Path(company)}?offset=2"))!);
        foreach (var query in new[] { "offset=-1", "limit=0", "limit=101" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Path(company)}?{query}")).StatusCode);
    }

    private static string Path(CompanyMembershipResponse company) => $"/api/companies/{company.Company.Id}/jobs";

    private static JobOfferRequest NewJob() => new()
    {
        Title = "Vývojář", Description = "Vývoj webových aplikací",
        WorkMode = WorkModes.Remote, EmploymentType = EmploymentTypes.FullTime
    };

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role = UserRoles.Company)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "valid-password-123",
            ConfirmPassword = "valid-password-123", FirstName = "Test", LastName = "User", Role = role
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth.User;
    }

    private static async Task<CompanyMembershipResponse> CreateCompanyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest { Name = "Test company", CountryCode = "CZ" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;
    }

    private static async Task<JobOfferManagementResponse> CreateJobAsync(HttpClient client, CompanyMembershipResponse company)
    {
        var response = await client.PostAsJsonAsync(Path(company), NewJob());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
    }
}
