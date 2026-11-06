using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Profiles;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.Api.Tests;

public sealed class CandidateProfilesControllerTests
{
    private readonly Mock<ICandidateProfileRepository> _repositoryMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    private CandidateProfilesController CreateController()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", _userId.ToString()),
            new Claim("role", UserRoles.Candidate)
        ], "TestAuth"));

        return new CandidateProfilesController(_repositoryMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public async Task Get_ReturnsProfile_WhenProfileExists()
    {
        var expectedProfile = new CandidateProfileResponse(
            _userId, "test@test.cz", "Jan", "Novak", null, null, null, null, null, [], [], [], null, DateTimeOffset.Now, DateTimeOffset.Now);

        _repositoryMock
            .Setup(r => r.GetProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedProfile);

        var controller = CreateController();

        var result = await controller.Get(CancellationToken.None);

        Assert.Equal(expectedProfile, result.Value);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenProfileDoesNotExist()
    {
        _repositoryMock
            .Setup(r => r.GetProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CandidateProfileResponse?)null);

        var controller = CreateController();

        var result = await controller.Get(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ReturnsUpdatedProfile_WhenSuccessful()
    {
        var request = new UpdateCandidateProfileRequest { FirstName = "Petr", LastName = "Novy" };
        var updatedProfile = new CandidateProfileResponse(
            _userId, "test@test.cz", "Petr", "Novy", null, null, null, null, null, [], [], [], null, DateTimeOffset.Now, DateTimeOffset.Now);

        _repositoryMock
            .Setup(r => r.UpdateProfileAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedProfile);

        var controller = CreateController();

        var result = await controller.Update(request, CancellationToken.None);

        Assert.Equal(updatedProfile, result.Value);
    }
}
