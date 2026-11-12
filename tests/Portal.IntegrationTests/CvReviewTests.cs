using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Portal.Api.Ai;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.CvReviews;
using Portal.Contracts.Profiles;
using Xunit;

namespace Portal.IntegrationTests;

public sealed class CvReviewTests : IClassFixture<ApiFixture>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CvReviewTests(ApiFixture fixture)
    {
        _factory = fixture.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICvAnalysisClient>();
                services.AddSingleton<ICvAnalysisClient, FakeCvAnalysisClient>();
            });
        });
    }

    [Fact]
    public async Task CandidateCvReviews_UploadAndAnalyze_PersistsAndReturnsSucceededReview()
    {
        using var client = _factory.CreateClient();
        var auth = await RegisterUserAsync(client, $"candidate-cv-{Guid.NewGuid():N}@example.com", UserRoles.Candidate);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var uploadedDoc = await UploadPdfCvAsync(client, "my_resume.pdf");

        var createResponse = await client.PostAsJsonAsync(
            "/api/profiles/me/cv-reviews",
            new CreateCandidateCvReviewRequest(uploadedDoc.Id));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var createdReview = await createResponse.Content.ReadFromJsonAsync<CandidateCvReviewResponse>();
        Assert.NotNull(createdReview);
        Assert.Equal(CvReviewStatuses.Succeeded, createdReview.Status);
        Assert.Equal((short)88, createdReview.Score);
        Assert.Equal(uploadedDoc.Id, createdReview.DocumentId);
        Assert.NotNull(createdReview.Result);
        Assert.Equal("Strong candidate profile", createdReview.Result.Summary);

        var listResponse = await client.GetAsync("/api/profiles/me/cv-reviews");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<CandidateCvReviewSummaryResponse>>();
        Assert.NotNull(list);
        Assert.Contains(list, r => r.Id == createdReview.Id && r.Score == 88);

        var detailResponse = await client.GetAsync($"/api/profiles/me/cv-reviews/{createdReview.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CandidateCvReviewResponse>();
        Assert.NotNull(detail);
        Assert.Equal(createdReview.Id, detail.Id);
        Assert.Equal("Strong candidate profile", detail.Result?.Summary);
    }

    [Fact]
    public async Task ApplicationCvReviews_HrTriggersAnalysis_PersistsJobRequirementsSnapshotAndResult()
    {
        using var candidateClient = _factory.CreateClient();
        var candidateAuth = await RegisterUserAsync(candidateClient, $"cand-app-{Guid.NewGuid():N}@example.com", UserRoles.Candidate);
        candidateClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", candidateAuth.AccessToken);
        var cvDoc = await UploadPdfCvAsync(candidateClient, "application_cv.pdf");

        using var hrClient = _factory.CreateClient();
        var hrAuth = await RegisterUserAsync(hrClient, $"hr-{Guid.NewGuid():N}@example.com", UserRoles.Company);
        hrClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hrAuth.AccessToken);

        var createCompanyResp = await hrClient.PostAsJsonAsync(
            "/api/companies",
            new CreateCompanyRequest
            {
                Name = "AI Test Company",
                RegistrationNumber = Random.Shared.Next(10000000, 99999999).ToString(),
                CountryCode = "CZ",
                Description = "Tech company",
                WebsiteUrl = "https://example.com",
                Address = "Praha"
            });

        if (!createCompanyResp.IsSuccessStatusCode)
        {
            var body = await createCompanyResp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"CreateCompany failed ({(int)createCompanyResp.StatusCode}): {body}");
        }

        var companyMembership = (await createCompanyResp.Content.ReadFromJsonAsync<CompanyMembershipResponse>())!;

        var applicationId = await SeedJobAndApplicationAsync(
            companyMembership.Company.Id,
            companyMembership.Membership.Id,
            candidateAuth.User.Id,
            cvDoc.Id);

        var reviewUrl = $"/api/companies/{companyMembership.Company.Id}/applications/{applicationId}/cv-reviews";
        var createReviewResp = await hrClient.PostAsJsonAsync(
            reviewUrl,
            new CreateApplicationCvReviewRequest(cvDoc.Id));

        Assert.Equal(HttpStatusCode.OK, createReviewResp.StatusCode);
        var review = await createReviewResp.Content.ReadFromJsonAsync<ApplicationCvReviewResponse>();
        Assert.NotNull(review);
        Assert.Equal(CvReviewStatuses.Succeeded, review.Status);
        Assert.Equal((short)93, review.Score);
        Assert.Equal("Senior .NET Developer", review.JobRequirementsSnapshot.Title);
        Assert.NotNull(review.Result);
        Assert.Contains("C#", review.Result.MatchingSkills);

        var listResp = await hrClient.GetAsync(reviewUrl);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var reviews = await listResp.Content.ReadFromJsonAsync<List<ApplicationCvReviewSummaryResponse>>();
        Assert.NotNull(reviews);
        Assert.Single(reviews);

        var getByIdResp = await hrClient.GetAsync($"{reviewUrl}/{review.Id}");
        Assert.Equal(HttpStatusCode.OK, getByIdResp.StatusCode);
        var fetched = await getByIdResp.Content.ReadFromJsonAsync<ApplicationCvReviewResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(review.Id, fetched.Id);

        using var outsiderClient = _factory.CreateClient();
        var outsiderAuth = await RegisterUserAsync(outsiderClient, $"outsider-{Guid.NewGuid():N}@example.com", UserRoles.Company);
        outsiderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsiderAuth.AccessToken);

        var forbidResp = await outsiderClient.GetAsync(reviewUrl);
        Assert.Equal(HttpStatusCode.Forbidden, forbidResp.StatusCode);
    }

    private async Task<Guid> SeedJobAndApplicationAsync(
        Guid companyId,
        Guid memberId,
        Guid candidateId,
        Guid documentId)
    {
        var dataSource = _factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await dataSource.OpenConnectionAsync();

        Guid jobOfferId;
        const string jobSql = """
            INSERT INTO portal.job_offers (
                company_id, created_by_member_id, title, description, requirements,
                location, work_mode, employment_type, status, published_at
            )
            VALUES (
                @CompanyId, @MemberId, 'Senior .NET Developer', 'Backend development',
                'C#, ASP.NET Core, PostgreSQL', 'Praha', 'hybrid', 'full_time', 'published', now()
            )
            RETURNING id;
            """;

        await using (var cmd = new NpgsqlCommand(jobSql, conn))
        {
            cmd.Parameters.AddWithValue("CompanyId", companyId);
            cmd.Parameters.AddWithValue("MemberId", memberId);
            jobOfferId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        Guid applicationId;
        const string appSql = """
            INSERT INTO portal.applications (job_offer_id, company_id, candidate_id, status, profile_snapshot)
            VALUES (@JobOfferId, @CompanyId, @CandidateId, 'submitted', '{}'::jsonb)
            RETURNING id;
            """;

        await using (var cmd = new NpgsqlCommand(appSql, conn))
        {
            cmd.Parameters.AddWithValue("JobOfferId", jobOfferId);
            cmd.Parameters.AddWithValue("CompanyId", companyId);
            cmd.Parameters.AddWithValue("CandidateId", candidateId);
            applicationId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        const string appDocSql = """
            INSERT INTO portal.application_documents (application_id, document_id, candidate_id)
            VALUES (@ApplicationId, @DocumentId, @CandidateId);
            """;

        await using (var cmd = new NpgsqlCommand(appDocSql, conn))
        {
            cmd.Parameters.AddWithValue("ApplicationId", applicationId);
            cmd.Parameters.AddWithValue("DocumentId", documentId);
            cmd.Parameters.AddWithValue("CandidateId", candidateId);
            await cmd.ExecuteNonQueryAsync();
        }

        return applicationId;
    }

    private static async Task<AuthResponse> RegisterUserAsync(HttpClient client, string email, string role)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = email,
                Password = "StrongPassword123!",
                ConfirmPassword = "StrongPassword123!",
                FirstName = "Jan",
                LastName = "Novak",
                Role = role
            });

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Register failed ({(int)response.StatusCode}): {body}");
        }

        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<CandidateDocumentResponse> UploadPdfCvAsync(HttpClient client, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var pdfBytes = "%PDF-1.7 fake pdf content for integration test"u8.ToArray();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", fileName);

        var response = await client.PostAsync("/api/profile/cv?setAsCurrent=true", form);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"UploadPdfCv failed ({(int)response.StatusCode}): {body}");
        }

        return (await response.Content.ReadFromJsonAsync<CandidateDocumentResponse>())!;
    }

    private sealed class FakeCvAnalysisClient : ICvAnalysisClient
    {
        public Task<AiCandidateCvAnalysisOutput> AnalyzeCandidateCvAsync(
            Stream documentStream,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            var payload = new CandidateCvReviewResultPayload(
                "Strong candidate profile",
                ["C#", "ASP.NET Core"],
                ["Include measurable achievements"],
                ["C#", "ASP.NET Core", "PostgreSQL"]);

            return Task.FromResult(new AiCandidateCvAnalysisOutput(
                88,
                payload,
                new AiMetadata("Gemini", "gemini-3.8-flash", "v1")));
        }

        public Task<AiApplicationCvAnalysisOutput> AnalyzeApplicationCvAsync(
            Stream documentStream,
            string mimeType,
            JobRequirementsSnapshot jobRequirements,
            CancellationToken cancellationToken = default)
        {
            var payload = new ApplicationCvReviewResultPayload(
                "Candidate matches job requirements",
                ["C#", "PostgreSQL"],
                [],
                ["Backend experience"],
                [],
                ["Describe transaction isolation levels"],
                "Invite to interview");

            return Task.FromResult(new AiApplicationCvAnalysisOutput(
                93,
                payload,
                new AiMetadata("Gemini", "gemini-3.8-flash", "v1")));
        }
    }
}
