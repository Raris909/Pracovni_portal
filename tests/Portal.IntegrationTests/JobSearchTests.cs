using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Jobs;

namespace Portal.IntegrationTests;

public sealed class JobSearchTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task FulltextSearchMatchesTitleDescriptionAndRequirementsOnlyOnActivePublishedOffers()
    {
        using var hr = fixture.CreateClient();
        var company = await CreateCompanyAsync(hr);
        try
        {
            var token = $"uniq{Guid.NewGuid():N}";
            var inTitle = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Senior {token} Architect",
                Description = "Návrh systémů",
                Requirements = "Architektura",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime
            });
            var inDescription = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = "Backend Engineer",
                Description = $"Vývoj mikroslužeb s využitím {token} a Dockeru.",
                Requirements = "Linux",
                WorkMode = WorkModes.Hybrid,
                EmploymentType = EmploymentTypes.FullTime
            });
            var inRequirements = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = "Platform Developer",
                Description = "Správa infrastruktury",
                Requirements = $"Zkušenost s {token} a PostgreSQL",
                WorkMode = WorkModes.Onsite,
                EmploymentType = EmploymentTypes.Contract
            });
            await CreateJobAsync(hr, company, new JobOfferRequest
            {
                Title = $"Draft {token}",
                Description = "Koncept",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime
            });

            using var visitor = fixture.CreateClient();
            var results = (await visitor.GetFromJsonAsync<JobOfferResponse[]>(
                $"/api/jobs?q={token}&limit=100"))!;

            Assert.Equal(3, results.Length);
            Assert.Contains(results, j => j.Id == inTitle.Id);
            Assert.Contains(results, j => j.Id == inDescription.Id);
            Assert.Contains(results, j => j.Id == inRequirements.Id);

            var multiWord = (await visitor.GetFromJsonAsync<JobOfferResponse[]>(
                $"/api/jobs?q={Uri.EscapeDataString($"{token} PostgreSQL")}&limit=100"))!;
            Assert.Equal(inRequirements.Id, Assert.Single(multiWord).Id);
        }
        finally
        {
            await CloseCompanyJobsAsync(company.Company.Id);
        }
    }

    [Fact]
    public async Task FiltersByWorkModeEmploymentTypeLocationAndSalaryMin()
    {
        using var hr = fixture.CreateClient();
        var company = await CreateCompanyAsync(hr);
        try
        {
            var marker = $"filt{Guid.NewGuid():N}";
            var match = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Senior .NET {marker}",
                Description = "Backend vývoj",
                Location = "Praha 4 - Pankrác",
                WorkMode = WorkModes.Hybrid,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 70000,
                SalaryMax = 100000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month
            });
            var onlyMinSalary = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Medior .NET {marker}",
                Description = "Backend vývoj",
                Location = "Praha 1",
                WorkMode = WorkModes.Hybrid,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 95000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month
            });
            await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Junior .NET {marker}",
                Description = "Backend vývoj",
                Location = "Praha 2",
                WorkMode = WorkModes.Hybrid,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 40000,
                SalaryMax = 55000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month
            });
            await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Bez mzdy {marker}",
                Description = "Backend vývoj",
                Location = "Praha 3",
                WorkMode = WorkModes.Hybrid,
                EmploymentType = EmploymentTypes.FullTime
            });
            await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Brno Remote {marker}",
                Description = "Backend vývoj",
                Location = "Brno",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.Contract,
                SalaryMin = 120000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month
            });

            using var visitor = fixture.CreateClient();
            var results = (await visitor.GetFromJsonAsync<JobOfferResponse[]>(
                $"/api/jobs?q={marker}&workMode=hybrid&employmentType=full_time&location=praha&salaryMin=85000&limit=100"))!;

            Assert.Equal(2, results.Length);
            Assert.Contains(results, j => j.Id == match.Id);
            Assert.Contains(results, j => j.Id == onlyMinSalary.Id);

            var remoteContract = (await visitor.GetFromJsonAsync<JobOfferResponse[]>(
                $"/api/jobs?q={marker}&workMode=remote&employmentType=contract&location=BRN"))!;
            Assert.Single(remoteContract);
            Assert.Equal("Brno", remoteContract[0].Location);
        }
        finally
        {
            await CloseCompanyJobsAsync(company.Company.Id);
        }
    }

    [Fact]
    public async Task SortsBySalaryDescAndDeadlineAscWithNullsLast()
    {
        using var hr = fixture.CreateClient();
        var company = await CreateCompanyAsync(hr);
        try
        {
            var marker = $"sort{Guid.NewGuid():N}";
            var now = DateTimeOffset.UtcNow;

            var lowSalarySoonDeadline = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"LowSalary {marker}",
                Description = "Popis",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 50000,
                SalaryMax = 60000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month,
                ApplicationDeadline = now.AddDays(3)
            });
            var highSalaryLateDeadline = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"HighSalary {marker}",
                Description = "Popis",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 90000,
                SalaryMax = 130000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month,
                ApplicationDeadline = now.AddDays(30)
            });
            var noSalaryNoDeadline = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"NoMeta {marker}",
                Description = "Popis",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime
            });

            using var visitor = fixture.CreateClient();

            var bySalary = (await visitor.GetFromJsonAsync<JobOfferResponse[]>(
                $"/api/jobs?q={marker}&sort=salary_desc"))!;
            Assert.Equal(
                [highSalaryLateDeadline.Id, lowSalarySoonDeadline.Id, noSalaryNoDeadline.Id],
                bySalary.Select(j => j.Id).ToArray());

            var byDeadline = (await visitor.GetFromJsonAsync<JobOfferResponse[]>(
                $"/api/jobs?q={marker}&sort=deadline_asc"))!;
            Assert.Equal(
                [lowSalarySoonDeadline.Id, highSalaryLateDeadline.Id, noSalaryNoDeadline.Id],
                byDeadline.Select(j => j.Id).ToArray());
        }
        finally
        {
            await CloseCompanyJobsAsync(company.Company.Id);
        }
    }

    [Theory]
    [InlineData("workMode=invalid")]
    [InlineData("employmentType=invalid")]
    [InlineData("salaryMin=-100")]
    [InlineData("sort=random")]
    public async Task RejectsInvalidSearchParametersWithBadRequestProblemDetails(string query)
    {
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync($"/api/jobs?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private Task CloseCompanyJobsAsync(Guid companyId) =>
        fixture.QueryAsync("UPDATE portal.job_offers SET status = 'closed' WHERE company_id = $1", companyId);

    private static async Task<CompanyMembershipResponse> CreateCompanyAsync(HttpClient client)
    {
        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "Valid-password-123",
            ConfirmPassword = "Valid-password-123",
            FirstName = "Test",
            LastName = "HR",
            Role = UserRoles.Company
        });
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var comp = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest
        {
            Name = "Search Test s.r.o.",
            CountryCode = "CZ"
        });
        Assert.Equal(HttpStatusCode.Created, comp.StatusCode);
        return (await comp.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;
    }

    private static async Task<JobOfferManagementResponse> CreateJobAsync(
        HttpClient client, CompanyMembershipResponse company, JobOfferRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{company.Company.Id}/jobs", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
    }

    private static async Task<JobOfferManagementResponse> CreateAndPublishAsync(
        HttpClient client, CompanyMembershipResponse company, JobOfferRequest request)
    {
        var draft = await CreateJobAsync(client, company, request);
        var publish = await client.PostAsync($"/api/companies/{company.Company.Id}/jobs/{draft.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return (await publish.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
    }

    [Fact]
    public async Task SearchExcludesDraftClosedAndExpiredOffers()
    {
        using var hr = fixture.CreateClient();
        var company = await CreateCompanyAsync(hr);
        try
        {
            var token = $"vis{Guid.NewGuid():N}";

            var active = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Active {token}",
                Description = "Otevřená pozice",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime
            });

            await CreateJobAsync(hr, company, new JobOfferRequest
            {
                Title = $"Draft {token}",
                Description = "Koncept",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime
            });

            var closed = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Closed {token}",
                Description = "Uzavřená pozice",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime
            });
            var closeResp = await hr.PostAsync($"/api/companies/{company.Company.Id}/jobs/{closed.Id}/close", null);
            Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);

            var expiredDraft = await CreateJobAsync(hr, company, new JobOfferRequest
            {
                Title = $"Expired {token}",
                Description = "Expirovaná pozice",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime,
                ApplicationDeadline = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await fixture.QueryAsync("""
                UPDATE portal.job_offers
                SET status = 'published', published_at = application_deadline - interval '1 day'
                WHERE id = $1
                """, expiredDraft.Id);

            using var visitor = fixture.CreateClient();
            var results = (await visitor.GetFromJsonAsync<JobOfferResponse[]>(
                $"/api/jobs?q={token}&workMode=remote&limit=100"))!;

            Assert.Equal(active.Id, Assert.Single(results).Id);
        }
        finally
        {
            await CloseCompanyJobsAsync(company.Company.Id);
        }
    }

    [Fact]
    public async Task PaginationWorksWithActiveFiltersAndSorting()
    {
        using var hr = fixture.CreateClient();
        var company = await CreateCompanyAsync(hr);
        try
        {
            var marker = $"page{Guid.NewGuid():N}";
            var job1 = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Offer 1 {marker}",
                Description = "Popis",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 100000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month
            });
            var job2 = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Offer 2 {marker}",
                Description = "Popis",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 80000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month
            });
            var job3 = await CreateAndPublishAsync(hr, company, new JobOfferRequest
            {
                Title = $"Offer 3 {marker}",
                Description = "Popis",
                WorkMode = WorkModes.Remote,
                EmploymentType = EmploymentTypes.FullTime,
                SalaryMin = 60000,
                SalaryCurrency = "CZK",
                SalaryPeriod = SalaryPeriods.Month
            });

            using var visitor = fixture.CreateClient();
            var baseQuery = $"/api/jobs?q={marker}&workMode=remote&sort=salary_desc";

            var page1 = (await visitor.GetFromJsonAsync<JobOfferResponse[]>($"{baseQuery}&offset=0&limit=2"))!;
            Assert.Equal([job1.Id, job2.Id], page1.Select(j => j.Id).ToArray());

            var page2 = (await visitor.GetFromJsonAsync<JobOfferResponse[]>($"{baseQuery}&offset=2&limit=2"))!;
            Assert.Equal([job3.Id], page2.Select(j => j.Id).ToArray());

            var page3 = (await visitor.GetFromJsonAsync<JobOfferResponse[]>($"{baseQuery}&offset=4&limit=2"))!;
            Assert.Empty(page3);
        }
        finally
        {
            await CloseCompanyJobsAsync(company.Company.Id);
        }
    }
}
