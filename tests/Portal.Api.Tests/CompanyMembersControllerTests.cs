using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Companies;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.Api.Tests;

public sealed class CompanyMembersControllerTests
{
    private readonly Mock<ICompanyRepository> _repositoryMock = new();
    private readonly CompanyMembershipService _membershipService;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _memberId = Guid.NewGuid();

    public CompanyMembersControllerTests()
    {
        _membershipService = new CompanyMembershipService(_repositoryMock.Object);
    }

    private CompanyMembersController CreateController()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", _userId.ToString()),
            new Claim("role", UserRoles.Company)
        ], "TestAuth"));

        return new CompanyMembersController(_membershipService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public async Task Add_ReturnsCreatedAtAction_WhenSuccessful()
    {
        var request = new AddCompanyMemberRequest { Email = "recruiter@test.cz", Role = CompanyMemberRoles.Recruiter };
        var createdMember = new CompanyMemberResponse(_memberId, _companyId, Guid.NewGuid(), request.Email, "Petr", "Novotny", CompanyMemberRoles.Recruiter, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        _repositoryMock
            .Setup(r => r.AddMemberAsync(_companyId, _userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((createdMember, MemberManagementStatus.Success));

        var controller = CreateController();

        var result = await controller.Add(_companyId, request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(createdMember, createdResult.Value);
    }

    [Fact]
    public async Task Add_ReturnsForbidden_WhenUserIsNotOwnerOrAdmin()
    {
        var request = new AddCompanyMemberRequest { Email = "recruiter@test.cz", Role = CompanyMemberRoles.Recruiter };

        _repositoryMock
            .Setup(r => r.AddMemberAsync(_companyId, _userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, MemberManagementStatus.Forbidden));

        var controller = CreateController();

        var result = await controller.Add(_companyId, request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, problemResult.StatusCode);
    }

    [Fact]
    public async Task Add_ReturnsConflict_WhenUserAlreadyIsMember()
    {
        var request = new AddCompanyMemberRequest { Email = "recruiter@test.cz", Role = CompanyMemberRoles.Recruiter };

        _repositoryMock
            .Setup(r => r.AddMemberAsync(_companyId, _userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, MemberManagementStatus.AlreadyMemberOrOwnerConflict));

        var controller = CreateController();

        var result = await controller.Add(_companyId, request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsConflict_WhenTargetIsOwner()
    {
        var request = new UpdateCompanyMemberRequest { Role = CompanyMemberRoles.Admin };

        _repositoryMock
            .Setup(r => r.UpdateMemberRoleAsync(_companyId, _userId, _memberId, request.Role, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, MemberManagementStatus.CannotModifyOwner));

        var controller = CreateController();

        var result = await controller.Update(_companyId, _memberId, request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);
    }

    [Fact]
    public async Task Remove_ReturnsNoContent_WhenSuccessful()
    {
        _repositoryMock
            .Setup(r => r.RemoveMemberAsync(_companyId, _userId, _memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MemberManagementStatus.Success);

        var controller = CreateController();

        var result = await controller.Remove(_companyId, _memberId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Remove_ReturnsNotFound_WhenMemberNotFound()
    {
        _repositoryMock
            .Setup(r => r.RemoveMemberAsync(_companyId, _userId, _memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MemberManagementStatus.MemberNotFound);

        var controller = CreateController();

        var result = await controller.Remove(_companyId, _memberId, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
    }
}
