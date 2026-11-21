using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.IntegrationTests;

public sealed class CandidateCvTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CandidateCanUploadPdfAndDocx_ListHistorySwitchCurrentAndDownload()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client, UserRoles.Candidate);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/profile/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/profile/cv/download")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CandidateDocumentResponse[]>("/api/profile/cv/history"))!);

        var pdfBytes = "%PDF-1.7 test-cv-v1"u8.ToArray();
        var doc1 = await UploadAsync(client, "resume_v1.pdf", "application/pdf", pdfBytes, setAsCurrent: true);
        Assert.True(doc1.IsCurrentCv);
        Assert.Equal("cv", doc1.Kind);
        Assert.Equal("application/pdf", doc1.MimeType);
        Assert.Equal(pdfBytes.Length, doc1.SizeBytes);

        var docxBytes = CreateMinimalDocxBytes("Jan Novák - C# Developer");
        var doc2 = await UploadAsync(
            client,
            "resume_v2.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            docxBytes,
            setAsCurrent: false);
        Assert.False(doc2.IsCurrentCv);

        var current = (await client.GetFromJsonAsync<CandidateDocumentResponse>("/api/profile/cv"))!;
        Assert.Equal(doc1.Id, current.Id);

        var downloadCurrent = await client.GetAsync("/api/profile/cv/download");
        Assert.Equal(HttpStatusCode.OK, downloadCurrent.StatusCode);
        Assert.Equal(pdfBytes, await downloadCurrent.Content.ReadAsByteArrayAsync());

        var switchResp = await client.PutAsJsonAsync("/api/profile/cv/current", new SetCurrentCvRequest(doc2.Id));
        Assert.Equal(HttpStatusCode.NoContent, switchResp.StatusCode);

        current = (await client.GetFromJsonAsync<CandidateDocumentResponse>("/api/profile/cv"))!;
        Assert.Equal(doc2.Id, current.Id);

        var downloadById = await client.GetAsync($"/api/profile/cv/{doc1.Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadById.StatusCode);
        Assert.Equal(pdfBytes, await downloadById.Content.ReadAsByteArrayAsync());

        var history = (await client.GetFromJsonAsync<CandidateDocumentResponse[]>("/api/profile/cv/history"))!;
        Assert.Equal(2, history.Length);
        Assert.Contains(history, d => d.Id == doc1.Id && !d.IsCurrentCv);
        Assert.Contains(history, d => d.Id == doc2.Id && d.IsCurrentCv);
    }

    [Fact]
    public async Task UploadCv_RejectsUnsupportedExtensionsSpoofedHeadersAndEmptyFiles()
    {
        using var client = fixture.CreateClient();
        await RegisterAsync(client, UserRoles.Candidate);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendUploadAsync(client, "script.exe", "application/octet-stream", [0x4D, 0x5A, 0x90])).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendUploadAsync(client, "fake.pdf", "application/pdf", "not a pdf"u8.ToArray())).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendUploadAsync(client, "empty.pdf", "application/pdf", [])).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/profile/cv/current", new SetCurrentCvRequest(Guid.Empty))).StatusCode);
    }

    [Fact]
    public async Task ProfileAndCvResourcesAreIsolatedAcrossCandidatesAndRoles()
    {
        using var candidate1 = fixture.CreateClient();
        await RegisterAsync(candidate1, UserRoles.Candidate);
        var doc1 = await UploadAsync(candidate1, "secret.pdf", "application/pdf", "%PDF-1.7 secret"u8.ToArray(), setAsCurrent: true);

        var expResp = await candidate1.PostAsJsonAsync("/api/profiles/me/experiences", new CandidateExperienceRequest
        {
            Employer = "Acme",
            Position = "Dev",
            StartDate = new DateOnly(2023, 1, 1)
        });
        Assert.Equal(HttpStatusCode.Created, expResp.StatusCode);
        var exp = (await expResp.Content.ReadFromJsonAsync<CandidateExperienceResponse>())!;

        var eduResp = await candidate1.PostAsJsonAsync("/api/profiles/me/education", new CandidateEducationRequest
        {
            School = "ČVUT",
            StartDate = new DateOnly(2019, 9, 1)
        });
        Assert.Equal(HttpStatusCode.Created, eduResp.StatusCode);
        var edu = (await eduResp.Content.ReadFromJsonAsync<CandidateEducationResponse>())!;

        using var candidate2 = fixture.CreateClient();
        await RegisterAsync(candidate2, UserRoles.Candidate);

        Assert.Equal(HttpStatusCode.NotFound, (await candidate2.GetAsync($"/api/profile/cv/{doc1.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await candidate2.PutAsJsonAsync("/api/profile/cv/current", new SetCurrentCvRequest(doc1.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await candidate2.PutAsJsonAsync($"/api/profiles/me/experiences/{exp.Id}", new CandidateExperienceRequest
            {
                Employer = "Hacked",
                Position = "Hacked",
                StartDate = new DateOnly(2023, 1, 1)
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await candidate2.DeleteAsync($"/api/profiles/me/experiences/{exp.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await candidate2.PutAsJsonAsync($"/api/profiles/me/education/{edu.Id}", new CandidateEducationRequest
            {
                School = "Hacked",
                StartDate = new DateOnly(2019, 9, 1)
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await candidate2.DeleteAsync($"/api/profiles/me/education/{edu.Id}")).StatusCode);

        using var companyClient = fixture.CreateClient();
        await RegisterAsync(companyClient, UserRoles.Company);
        Assert.Equal(HttpStatusCode.Forbidden, (await companyClient.GetAsync("/api/profiles/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await companyClient.GetAsync("/api/profile/cv")).StatusCode);
    }

    [Fact]
    public async Task ExpiredJwtTokenIsRejectedAcrossProtectedEndpoints()
    {
        using var client = fixture.CreateClient();
        var auth = await RegisterAsync(client, UserRoles.Candidate);

        var expiredToken = CreateExpiredJwt(auth.User);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profiles/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/chat/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profiles/me/cv-reviews")).StatusCode);
    }

    private static string CreateExpiredJwt(UserResponse user)
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('t', 64))),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "Portal.Api",
            audience: "Portal.Clients",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("role", user.Role)
            ],
            notBefore: now.AddMinutes(-10),
            expires: now.AddMinutes(-5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] CreateMinimalDocxBytes(string text)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body>
                </w:document>
                """);
        }
        return ms.ToArray();
    }

    private static async Task<HttpResponseMessage> SendUploadAsync(
        HttpClient client, string fileName, string mimeType, byte[] content, bool setAsCurrent = true)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(fileContent, "file", fileName);
        return await client.PostAsync($"/api/profile/cv?setAsCurrent={setAsCurrent.ToString().ToLowerInvariant()}", form);
    }

    private static async Task<CandidateDocumentResponse> UploadAsync(
        HttpClient client, string fileName, string mimeType, byte[] content, bool setAsCurrent)
    {
        using var resp = await SendUploadAsync(client, fileName, mimeType, content, setAsCurrent);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<CandidateDocumentResponse>())!;
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string role)
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
        return auth;
    }
}
