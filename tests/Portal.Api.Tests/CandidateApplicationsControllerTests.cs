using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portal.Api.Applications;
using Portal.Contracts.Applications;
using Xunit;

namespace Portal.Api.Tests;

public sealed class CandidateApplicationsControllerTests
{
    private static CandidateApplicationsController CreateControllerWithUser(
        ICandidateApplicationRepository repository,
        Guid? userId)
    {
        var controller = new CandidateApplicationsController(repository);
        var claims = new List<Claim>();
        if (userId.HasValue)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, userId.Value.ToString()));
            claims.Add(new Claim(ClaimTypes.Role, "candidate"));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };

        return controller;
    }

    [Fact]
    public async Task GetMyApplications_ReturnsCurrentUsersApplicationsWithStatus()
    {
        var candidateId = Guid.NewGuid();
        var otherCandidateId = Guid.NewGuid();
        var repo = new InMemoryCandidateApplicationRepository();

        repo.SeedApplication(candidateId, ApplicationStatuses.InProgress, "Backend .NET Developer");
        repo.SeedApplication(candidateId, ApplicationStatuses.Submitted, "Fullstack Engineer");
        repo.SeedApplication(otherCandidateId, ApplicationStatuses.Accepted, "Other User Job");

        var controller = CreateControllerWithUser(repo, candidateId);

        var actionResult = await controller.GetMyApplications();

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<CandidateApplicationSummaryResponse>>(okResult.Value);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, a => a.JobTitle == "Backend .NET Developer" && a.Status == ApplicationStatuses.InProgress);
        Assert.Contains(items, a => a.JobTitle == "Fullstack Engineer" && a.Status == ApplicationStatuses.Submitted);
    }

    [Fact]
    public async Task GetMyApplications_WithValidStatusFilter_ReturnsFilteredApplications()
    {
        var candidateId = Guid.NewGuid();
        var repo = new InMemoryCandidateApplicationRepository();

        repo.SeedApplication(candidateId, ApplicationStatuses.InProgress, "Backend .NET Developer");
        repo.SeedApplication(candidateId, ApplicationStatuses.Rejected, "Frontend Developer");

        var controller = CreateControllerWithUser(repo, candidateId);

        var actionResult = await controller.GetMyApplications(status: "in_progress");

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<CandidateApplicationSummaryResponse>>(okResult.Value);
        var single = Assert.Single(items);
        Assert.Equal(ApplicationStatuses.InProgress, single.Status);
    }

    [Fact]
    public async Task GetMyApplications_WithInvalidStatusFilter_ReturnsBadRequest()
    {
        var candidateId = Guid.NewGuid();
        var repo = new InMemoryCandidateApplicationRepository();
        var controller = CreateControllerWithUser(repo, candidateId);

        var actionResult = await controller.GetMyApplications(status: "non_existent_status");

        Assert.IsType<BadRequestObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task GetMyApplicationById_OwnedApplication_ReturnsDetailWithStatusAndStages()
    {
        var candidateId = Guid.NewGuid();
        var repo = new InMemoryCandidateApplicationRepository();
        var seededId = repo.SeedApplication(candidateId, ApplicationStatuses.InProgress, "C# Developer");

        var controller = CreateControllerWithUser(repo, candidateId);

        var actionResult = await controller.GetMyApplicationById(seededId);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var detail = Assert.IsType<CandidateApplicationDetailResponse>(okResult.Value);
        Assert.Equal(seededId, detail.Id);
        Assert.Equal(ApplicationStatuses.InProgress, detail.Status);
        Assert.Equal("C# Developer", detail.JobTitle);
        Assert.NotEmpty(detail.Stages);
        Assert.NotEmpty(detail.AttachedDocuments);
    }

    [Fact]
    public async Task GetMyApplicationById_OtherUsersApplication_ReturnsNotFound()
    {
        var candidateId = Guid.NewGuid();
        var otherCandidateId = Guid.NewGuid();
        var repo = new InMemoryCandidateApplicationRepository();
        var otherAppId = repo.SeedApplication(otherCandidateId, ApplicationStatuses.Submitted, "Secret Job");

        var controller = CreateControllerWithUser(repo, candidateId);

        var actionResult = await controller.GetMyApplicationById(otherAppId);

        Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    }

    private sealed class InMemoryCandidateApplicationRepository : ICandidateApplicationRepository
    {
        private readonly List<(Guid CandidateId, CandidateApplicationDetailResponse Detail)> _items = [];

        public Guid SeedApplication(Guid candidateId, string status, string jobTitle)
        {
            var appId = Guid.NewGuid();
            var jobOfferId = Guid.NewGuid();
            var companyId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var detail = new CandidateApplicationDetailResponse(
                Id: appId,
                JobOfferId: jobOfferId,
                JobTitle: jobTitle,
                CompanyId: companyId,
                CompanyName: "Acme Corp",
                Location: "Prague",
                WorkMode: "hybrid",
                EmploymentType: "full_time",
                Status: status,
                CoverMessage: "I am very interested in this position.",
                ProfileSnapshot: new ApplicationProfileSnapshot(
                    FirstName: "Jan",
                    LastName: "Novák",
                    Email: "jan.novak@example.com",
                    Phone: "+420123456789",
                    Headline: ".NET Developer",
                    Summary: "Experienced backend engineer.",
                    City: "Prague",
                    CountryCode: "CZ",
                    Skills: ["C#", ".NET", "PostgreSQL"],
                    Experiences: [],
                    Education: []),
                AttachedDocuments:
                [
                    new ApplicationAttachedDocumentResponse(
                        Guid.NewGuid(), "cv", "cv.pdf", "application/pdf", 12345, now)
                ],
                Stages:
                [
                    new CandidateApplicationStageProgressResponse(
                        Guid.NewGuid(), "HR Screening", "screening", 1, null, "in_progress", now, null, now)
                ],
                ConversationId: Guid.NewGuid(),
                SubmittedAt: now,
                UpdatedAt: now);

            _items.Add((candidateId, detail));
            return appId;
        }

        public Task<IReadOnlyList<CandidateApplicationSummaryResponse>> GetCandidateApplicationsAsync(
            Guid candidateId,
            string? statusFilter = null,
            CancellationToken cancellationToken = default)
        {
            var query = _items.Where(x => x.CandidateId == candidateId);
            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(x => x.Detail.Status == statusFilter);
            }

            var list = query
                .Select(x =>
                {
                    var d = x.Detail;
                    var firstStage = d.Stages.FirstOrDefault();
                    return new CandidateApplicationSummaryResponse(
                        d.Id,
                        d.JobOfferId,
                        d.JobTitle,
                        d.CompanyId,
                        d.CompanyName,
                        d.Location,
                        d.WorkMode,
                        d.EmploymentType,
                        d.Status,
                        firstStage?.Name,
                        firstStage?.Kind,
                        firstStage?.Status,
                        d.SubmittedAt,
                        d.UpdatedAt);
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<CandidateApplicationSummaryResponse>>(list);
        }

        public Task<CandidateApplicationDetailResponse?> GetCandidateApplicationByIdAsync(
            Guid candidateId,
            Guid applicationId,
            CancellationToken cancellationToken = default)
        {
            var match = _items.FirstOrDefault(x => x.CandidateId == candidateId && x.Detail.Id == applicationId);
            return Task.FromResult<CandidateApplicationDetailResponse?>(match.Detail);
        }
    }
}