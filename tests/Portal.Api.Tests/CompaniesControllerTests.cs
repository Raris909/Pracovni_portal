using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Companies;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.Api.Tests;

public sealed class CompaniesControllerTests
{
    private readonly Mock<ICompanyRepository> _repositoryMock = new();
    private readonly CompanyMembershipService _membershipService;
    private readonly Guid _userId = Guid.NewGuid();

    public CompaniesControllerTests()
    {
        _membershipService = new CompanyMembershipService(_repositoryMock.Object);
    }

    private CompaniesController CreateController()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", _userId.ToString()),
            new Claim("role", UserRoles.Company)
        ], "TestAuth"));

        return new CompaniesController(_membershipService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenSuccessful()
    {
        var request = new CreateCompanyRequest { Name = "Test Company", CountryCode = "CZ" };
        var company = new CompanyResponse(Guid.NewGuid(), "Test Company", null, "CZ", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var membership = new CompanyMemberResponse(Guid.NewGuid(), company.Id, _userId, "test@test.cz", "Jan", "Novak", CompanyMemberRoles.Owner, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var expectedResponse = new CompanyMembershipResponse(company, membership);

        _repositoryMock
            .Setup(r => r.CreateCompanyAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((expectedResponse, CreateCompanyStatus.Success));

        var controller = CreateController();

        var result = await controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(expectedResponse, createdResult.Value);
        Assert.Equal(nameof(CompaniesController.Get), createdResult.ActionName);
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenRegistrationNumberAlreadyExists()
    {
        var request = new CreateCompanyRequest { Name = "Test Company", CountryCode = "CZ", RegistrationNumber = "12345678" };

        _repositoryMock
            .Setup(r => r.CreateCompanyAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, CreateCompanyStatus.DuplicateRegistrationNumber));

        var controller = CreateController();

        var result = await controller.Create(request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsForbid_WhenUserAccountIsNotFoundOrInvalid()
    {
        var request = new CreateCompanyRequest { Name = "Test Company", CountryCode = "CZ" };

        _repositoryMock
            .Setup(r => r.CreateCompanyAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, CreateCompanyStatus.UserNotFound));

        var controller = CreateController();

        var result = await controller.Create(request, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenCompanyNotFoundForUser()
    {
        var companyId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.FindUserCompanyAsync(_userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompanyMembershipResponse?)null);

        var controller = CreateController();

        var result = await controller.Get(companyId, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
    }
}
