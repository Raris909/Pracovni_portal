using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Applications;
using Portal.Contracts.Applications;
using Portal.Contracts.Auth;
using Xunit;

namespace Portal.Api.Tests;

public sealed class CompanyApplicationsControllerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ICompanyApplicationRepository> _appRepoMock = new();

    private CompanyApplicationsController CreateController(Guid? authenticatedUserId = null)
    {
        var controller = new CompanyApplicationsController(_appRepoMock.Object);

        var claims = new List<Claim>();
        var effectiveUserId = authenticatedUserId ?? _userId;
        if (effectiveUserId != Guid.Empty)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, effectiveUserId.ToString()));
            claims.Add(new Claim(ClaimTypes.Role, UserRoles.Company));
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

    private void SetupAuthorizedMember()
    {
        _appRepoMock
            .Setup(r => r.CheckCompanyAccessAsync(_companyId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CompanyAccessStatus.Authorized);
    }

    private static CompanyApplicationDetailResponse CreateSampleDetail(
        Guid appId,
        Guid companyId,
        string status = ApplicationStatuses.Submitted,
        Guid? assignedMemberId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CompanyApplicationDetailResponse(
            Id: appId,
            JobOfferId: Guid.NewGuid(),
            JobTitle: "Senior C# Developer",
            CompanyId: companyId,
            CandidateId: Guid.NewGuid(),
            AssignedMemberId: assignedMemberId,
            Status: status,
            CoverMessage: "I am excited to apply.",
            ProfileSnapshot: new ApplicationProfileSnapshot(
                FirstName: "Eva",
                LastName: "Svobodová",
                Email: "eva@example.com",
                Phone: "+420777888999",
                Headline: ".NET Backend Engineer",
                Summary: "5 years of experience.",
                City: "Praha",
                CountryCode: "CZ",
                Skills: ["C#", "PostgreSQL", "Docker"],
                Experiences: [],
                Education: []),
            AttachedDocuments:
            [
                new ApplicationAttachedDocumentResponse(
                    Guid.NewGuid(), "cv", "eva_cv.pdf", "application/pdf", 20480, now)
            ],
            Stages: [],
            ConversationId: Guid.NewGuid(),
            SubmittedAt: now,
            UpdatedAt: now);
    }

    [Fact]
    public async Task GetApplications_AuthorizedMember_ReturnsCompanyApplications()
    {
        SetupAuthorizedMember();
        var appId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expected = new List<CompanyApplicationSummaryResponse>
        {
            new(
                appId,
                Guid.NewGuid(),
                "Senior C# Developer",
                _companyId,
                Guid.NewGuid(),
                "Eva",
                "Svobodová",
                "eva@example.com",
                null,
                ApplicationStatuses.Submitted,
                1,
                now,
                now)
        };

        _appRepoMock
            .Setup(r => r.GetCompanyApplicationsAsync(_companyId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController();
        var result = await controller.GetApplications(_companyId);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<CompanyApplicationSummaryResponse>>(okResult.Value);
        Assert.Single(items);
        Assert.Equal("Eva", items[0].CandidateFirstName);
    }

    [Fact]
    public async Task GetApplications_NonMemberOfCompany_ReturnsForbid()
    {
        _appRepoMock
            .Setup(r => r.CheckCompanyAccessAsync(_companyId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CompanyAccessStatus.Forbidden);

        var controller = CreateController();
        var result = await controller.GetApplications(_companyId);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task UpdateStatus_ValidStatus_ReturnsUpdatedApplication()
    {
        SetupAuthorizedMember();
        var appId = Guid.NewGuid();
        var updatedDetail = CreateSampleDetail(appId, _companyId, ApplicationStatuses.InProgress);

        _appRepoMock
            .Setup(r => r.UpdateApplicationStatusAsync(_companyId, appId, ApplicationStatuses.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDetail);

        var controller = CreateController();
        var result = await controller.UpdateStatus(
            _companyId,
            appId,
            new UpdateCompanyApplicationStatusRequest(ApplicationStatuses.InProgress));

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<CompanyApplicationDetailResponse>(okResult.Value);
        Assert.Equal(ApplicationStatuses.InProgress, detail.Status);
    }

    [Fact]
    public async Task AssignMember_InvalidCompanyMember_ReturnsBadRequest()
    {
        SetupAuthorizedMember();
        var appId = Guid.NewGuid();
        var invalidMemberId = Guid.NewGuid();

        _appRepoMock
            .Setup(r => r.AssignMemberAsync(_companyId, appId, invalidMemberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, false, null));

        var controller = CreateController();
        var result = await controller.AssignMember(
            _companyId,
            appId,
            new AssignApplicationMemberRequest(invalidMemberId));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetApplicationDocuments_AuthorizedMember_ReturnsAttachedDocuments()
    {
        SetupAuthorizedMember();
        var appId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var docs = new List<ApplicationAttachedDocumentResponse>
        {
            new(docId, "cv", "cv.pdf", "application/pdf", 15000, DateTimeOffset.UtcNow)
        };

        _appRepoMock
            .Setup(r => r.GetApplicationDocumentsAsync(_companyId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docs);

        var controller = CreateController();
        var result = await controller.GetApplicationDocuments(_companyId, appId);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedDocs = Assert.IsAssignableFrom<IReadOnlyList<ApplicationAttachedDocumentResponse>>(okResult.Value);
        Assert.Single(returnedDocs);
        Assert.Equal(docId, returnedDocs[0].DocumentId);
    }
}