using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Api.Applications;
using Portal.Contracts.Applications;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Jobs;
using Portal.Contracts.Profiles;
using Portal.Contracts.Recruitment;

namespace Portal.IntegrationTests;

public sealed class ApplicationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CandidateCanSubmitApplication_ProfileSnapshotAndSelectedDocumentsStayImmutable()
    {
        using var hr = fixture.CreateClient();
        var (company, job) = await CreatePublishedJobAsync(hr, withStages: true);

        using var candidate = fixture.CreateClient();
        await RegisterAsync(candidate, UserRoles.Candidate);

        var updateProfile = await candidate.PutAsJsonAsync("/api/profiles/me", new UpdateCandidateProfileRequest
        {
            FirstName = "Jan",
            LastName = "Novák",
            Phone = "+420123456789",
            Headline = "Backend .NET Developer",
            Summary = "5 let zkušeností s C#.",
            City = "Praha",
            CountryCode = "CZ",
            Skills = ["C#", "PostgreSQL"]
        });
        Assert.Equal(HttpStatusCode.OK, updateProfile.StatusCode);

        var expResp = await candidate.PostAsJsonAsync("/api/profiles/me/experiences", new CandidateExperienceRequest
        {
            Employer = "Tech s.r.o.",
            Position = "Developer",
            StartDate = new DateOnly(2022, 1, 1)
        });
        Assert.Equal(HttpStatusCode.Created, expResp.StatusCode);

        var attachedPdfBytes = "%PDF-1.7 attached-cv-content"u8.ToArray();
        var attachedCv = await UploadPdfAsync(candidate, "attached_cv.pdf", attachedPdfBytes, setAsCurrent: true);
        var unattachedCv = await UploadPdfAsync(candidate, "private_cv.pdf", "%PDF-1.7 private-cv"u8.ToArray(), setAsCurrent: false);

        var submitResp = await candidate.PostAsJsonAsync(
            $"/api/jobs/{job.Id}/applications",
            new SubmitJobApplicationBodyRequest("  Mám velký zájem o pozici.  ", [attachedCv.Id]));

        Assert.Equal(HttpStatusCode.Created, submitResp.StatusCode);
        var submitted = (await submitResp.Content.ReadFromJsonAsync<SubmittedApplicationResult>())!;
        Assert.Equal($"/api/applications/{submitted.Id}", submitResp.Headers.Location?.OriginalString);
        Assert.Equal(job.Id, submitted.JobOfferId);
        Assert.Equal(company.Company.Id, submitted.CompanyId);
        Assert.Equal(ApplicationStatuses.Submitted, submitted.Status);
        Assert.Equal("Mám velký zájem o pozici.", submitted.CoverMessage);
        Assert.Single(submitted.AttachedDocumentIds, attachedCv.Id);
        Assert.Equal("Backend .NET Developer", submitted.ProfileSnapshot.Headline);
        Assert.Single(submitted.ProfileSnapshot.Experiences);

        var laterUpdate = await candidate.PutAsJsonAsync("/api/profiles/me", new UpdateCandidateProfileRequest
        {
            FirstName = "Jan",
            LastName = "Změněný",
            Headline = "Completely Different Role",
            Skills = ["Rust"]
        });
        Assert.Equal(HttpStatusCode.OK, laterUpdate.StatusCode);

        var myApps = (await candidate.GetFromJsonAsync<CandidateApplicationSummaryResponse[]>("/api/applications"))!;
        var summary = Assert.Single(myApps);
        Assert.Equal(submitted.Id, summary.Id);
        Assert.Equal(ApplicationStatuses.Submitted, summary.Status);
        Assert.Equal("Screening", summary.CurrentStageName);

        var filtered = (await candidate.GetFromJsonAsync<CandidateApplicationSummaryResponse[]>("/api/applications?status=submitted"))!;
        Assert.Single(filtered);
        Assert.Empty((await candidate.GetFromJsonAsync<CandidateApplicationSummaryResponse[]>("/api/applications?status=accepted"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await candidate.GetAsync("/api/applications?status=invalid")).StatusCode);

        var candidateDetail = (await candidate.GetFromJsonAsync<CandidateApplicationDetailResponse>($"/api/applications/{submitted.Id}"))!;
        Assert.Equal("Backend .NET Developer", candidateDetail.ProfileSnapshot.Headline);
        Assert.Equal("Novák", candidateDetail.ProfileSnapshot.LastName);
        Assert.NotNull(candidateDetail.ConversationId);
        Assert.Equal(2, candidateDetail.Stages.Count);
        Assert.Single(candidateDetail.AttachedDocuments, d => d.DocumentId == attachedCv.Id);

        var hrDetail = (await hr.GetFromJsonAsync<CompanyApplicationDetailResponse>(
            $"/api/companies/{company.Company.Id}/applications/{submitted.Id}"))!;
        Assert.Equal("Backend .NET Developer", hrDetail.ProfileSnapshot.Headline);
        Assert.Single(hrDetail.AttachedDocuments, d => d.DocumentId == attachedCv.Id);

        var hrDocs = (await hr.GetFromJsonAsync<ApplicationAttachedDocumentResponse[]>(
            $"/api/companies/{company.Company.Id}/applications/{submitted.Id}/documents"))!;
        Assert.Single(hrDocs, d => d.DocumentId == attachedCv.Id);

        var downloadResp = await hr.GetAsync(
            $"/api/companies/{company.Company.Id}/applications/{submitted.Id}/documents/{attachedCv.Id}");
        Assert.Equal(HttpStatusCode.OK, downloadResp.StatusCode);
        Assert.Equal("application/pdf", downloadResp.Content.Headers.ContentType?.MediaType);
        Assert.Equal(attachedPdfBytes, await downloadResp.Content.ReadAsByteArrayAsync());

        var unattachedResp = await hr.GetAsync(
            $"/api/companies/{company.Company.Id}/applications/{submitted.Id}/documents/{unattachedCv.Id}");
        Assert.Equal(HttpStatusCode.NotFound, unattachedResp.StatusCode);
    }

    [Fact]
    public async Task SubmitApplication_DefaultsToCurrentCv_AndRejectsDuplicatesClosedOffersAndForeignDocuments()
    {
        using var hr = fixture.CreateClient();
        var (company, publishedJob) = await CreatePublishedJobAsync(hr);
        var draftJob = await CreateJobAsync(hr, company.Company.Id);

        using var candidate1 = fixture.CreateClient();
        await RegisterAsync(candidate1, UserRoles.Candidate);
        var currentCv = await UploadPdfAsync(candidate1, "default_cv.pdf", "%PDF-1.7 default"u8.ToArray(), setAsCurrent: true);

        using var candidate2 = fixture.CreateClient();
        await RegisterAsync(candidate2, UserRoles.Candidate);
        var foreignCv = await UploadPdfAsync(candidate2, "foreign_cv.pdf", "%PDF-1.7 foreign"u8.ToArray(), setAsCurrent: true);

        var foreignDocResp = await candidate1.PostAsJsonAsync(
            "/api/applications",
            new SubmitApplicationWithJobIdRequest(publishedJob.Id, null, [foreignCv.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, foreignDocResp.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await candidate1.PostAsJsonAsync($"/api/jobs/{draftJob.Id}/applications", new SubmitJobApplicationBodyRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await candidate1.PostAsJsonAsync($"/api/jobs/{Guid.NewGuid()}/applications", new SubmitJobApplicationBodyRequest())).StatusCode);

        var firstSubmit = await candidate1.PostAsJsonAsync(
            "/api/applications",
            new SubmitApplicationWithJobIdRequest(publishedJob.Id));
        Assert.Equal(HttpStatusCode.Created, firstSubmit.StatusCode);
        var created = (await firstSubmit.Content.ReadFromJsonAsync<SubmittedApplicationResult>())!;
        Assert.Single(created.AttachedDocumentIds, currentCv.Id);

        var duplicateSubmit = await candidate1.PostAsJsonAsync(
            $"/api/jobs/{publishedJob.Id}/applications",
            new SubmitJobApplicationBodyRequest());
        Assert.Equal(HttpStatusCode.Conflict, duplicateSubmit.StatusCode);

        var closeResp = await hr.PostAsync($"/api/companies/{company.Company.Id}/jobs/{publishedJob.Id}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);

        var closedSubmit = await candidate2.PostAsJsonAsync(
            $"/api/jobs/{publishedJob.Id}/applications",
            new SubmitJobApplicationBodyRequest());
        Assert.Equal(HttpStatusCode.NotFound, closedSubmit.StatusCode);
    }

    [Fact]
    public async Task HrCanFilterAssignAndUpdateStatus_AndApplicationsAreIsolatedAcrossUsersAndCompanies()
    {
        using var owner = fixture.CreateClient();
        var (company, job) = await CreatePublishedJobAsync(owner);

        using var recruiter = fixture.CreateClient();
        var recruiterUser = await RegisterAsync(recruiter, UserRoles.Company);
        var addMemberResp = await owner.PostAsJsonAsync(
            $"/api/companies/{company.Company.Id}/members",
            new AddCompanyMemberRequest { Email = recruiterUser.Email, Role = CompanyMemberRoles.Recruiter });
        Assert.Equal(HttpStatusCode.Created, addMemberResp.StatusCode);
        var recruiterMember = (await addMemberResp.Content.ReadFromJsonAsync<CompanyMemberResponse>())!;

        using var otherCompanyClient = fixture.CreateClient();
        var (otherCompany, _) = await CreatePublishedJobAsync(otherCompanyClient);

        using var candidate1 = fixture.CreateClient();
        await RegisterAsync(candidate1, UserRoles.Candidate);
        var appResp = await candidate1.PostAsJsonAsync(
            $"/api/jobs/{job.Id}/applications",
            new SubmitJobApplicationBodyRequest("Hello"));
        Assert.Equal(HttpStatusCode.Created, appResp.StatusCode);
        var app = (await appResp.Content.ReadFromJsonAsync<SubmittedApplicationResult>())!;

        using var candidate2 = fixture.CreateClient();
        await RegisterAsync(candidate2, UserRoles.Candidate);

        Assert.Equal(HttpStatusCode.NotFound, (await candidate2.GetAsync($"/api/applications/{app.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await candidate1.GetAsync($"/api/companies/{company.Company.Id}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/applications")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherCompanyClient.GetAsync($"/api/companies/{company.Company.Id}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherCompanyClient.GetAsync($"/api/companies/{company.Company.Id}/applications/{app.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherCompanyClient.GetAsync($"/api/companies/{Guid.NewGuid()}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherCompanyClient.GetAsync($"/api/companies/{otherCompany.Company.Id}/applications/{app.Id}")).StatusCode);

        var assignPath = $"/api/companies/{company.Company.Id}/applications/{app.Id}/assignee";
        var invalidAssign = await owner.PutAsJsonAsync(assignPath, new AssignApplicationMemberRequest(otherCompany.Membership.Id));
        Assert.Equal(HttpStatusCode.BadRequest, invalidAssign.StatusCode);

        var assignResp = await owner.PutAsJsonAsync(assignPath, new AssignApplicationMemberRequest(recruiterMember.Id));
        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);
        var assigned = (await assignResp.Content.ReadFromJsonAsync<CompanyApplicationDetailResponse>())!;
        Assert.Equal(recruiterMember.Id, assigned.AssignedMemberId);

        var statusPath = $"/api/companies/{company.Company.Id}/applications/{app.Id}/status";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await recruiter.PutAsJsonAsync(statusPath, new UpdateCompanyApplicationStatusRequest(ApplicationStatuses.Withdrawn))).StatusCode);

        var statusResp = await recruiter.PutAsJsonAsync(statusPath, new UpdateCompanyApplicationStatusRequest(ApplicationStatuses.InProgress));
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        var updated = (await statusResp.Content.ReadFromJsonAsync<CompanyApplicationDetailResponse>())!;
        Assert.Equal(ApplicationStatuses.InProgress, updated.Status);

        var jobApps = (await recruiter.GetFromJsonAsync<CompanyApplicationSummaryResponse[]>(
            $"/api/companies/{company.Company.Id}/jobs/{job.Id}/applications?status=in_progress&assignedMemberId={recruiterMember.Id}"))!;
        var item = Assert.Single(jobApps);
        Assert.Equal(app.Id, item.Id);
        Assert.Equal(recruiterMember.Id, item.AssignedMemberId);

        var unassignResp = await owner.PutAsJsonAsync(assignPath, new AssignApplicationMemberRequest(null));
        Assert.Equal(HttpStatusCode.OK, unassignResp.StatusCode);
        Assert.Null((await unassignResp.Content.ReadFromJsonAsync<CompanyApplicationDetailResponse>())!.AssignedMemberId);
    }

    private async Task<(CompanyMembershipResponse Company, JobOfferManagementResponse Job)> CreatePublishedJobAsync(
        HttpClient client, bool withStages = false)
    {
        await RegisterAsync(client, UserRoles.Company);
        var compResp = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest
        {
            Name = "Test Company",
            CountryCode = "CZ"
        });
        Assert.Equal(HttpStatusCode.Created, compResp.StatusCode);
        var company = (await compResp.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;

        var draft = await CreateJobAsync(client, company.Company.Id);

        if (withStages)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                $"/api/companies/{company.Company.Id}/jobs/{draft.Id}/stages",
                new UpsertRecruitmentStageRequest("Screening", RecruitmentStageKinds.Screening, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                $"/api/companies/{company.Company.Id}/jobs/{draft.Id}/stages",
                new UpsertRecruitmentStageRequest("Interview", RecruitmentStageKinds.Interview, 2))).StatusCode);
        }

        var pubResp = await client.PostAsync($"/api/companies/{company.Company.Id}/jobs/{draft.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, pubResp.StatusCode);
        var published = (await pubResp.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
        return (company, published);
    }

    private static async Task<JobOfferManagementResponse> CreateJobAsync(HttpClient client, Guid companyId)
    {
        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/jobs", new JobOfferRequest
        {
            Title = "Senior .NET Developer",
            Description = "Vývoj backendu.",
            Requirements = "C#, PostgreSQL",
            WorkMode = WorkModes.Hybrid,
            EmploymentType = EmploymentTypes.FullTime
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JobOfferManagementResponse>())!;
    }

    private static async Task<CandidateDocumentResponse> UploadPdfAsync(
        HttpClient client, string fileName, byte[] content, bool setAsCurrent)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", fileName);

        var resp = await client.PostAsync($"/api/profile/cv?setAsCurrent={setAsCurrent.ToString().ToLowerInvariant()}", form);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<CandidateDocumentResponse>())!;
    }

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "Valid-password-123!",
            ConfirmPassword = "Valid-password-123!",
            FirstName = "Test",
            LastName = "User",
            Role = role
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth.User;
    }
}
