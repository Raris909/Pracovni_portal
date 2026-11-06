using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Profiles;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.Api.Tests;

public sealed class CandidateExperiencesControllerTests
{
    private readonly Mock<ICandidateProfileRepository> _repositoryMock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _experienceId = Guid.NewGuid();

    private CandidateExperiencesController CreateController()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", _userId.ToString()),
            new Claim("role", UserRoles.Candidate)
        ], "TestAuth"));

        return new CandidateExperiencesController(_repositoryMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public async Task Add_ReturnsCreatedExperience()
    {
        var request = new CandidateExperienceRequest { Employer = "A", Position = "B" };
        var expectedResponse = new CandidateExperienceResponse(_experienceId, "A", "B", null, new DateOnly(2020, 1, 1), null);

        _repositoryMock
            .Setup(r => r.AddExperienceAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        var result = await controller.Add(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(expectedResponse, createdResult.Value);
    }

    [Fact]
    public async Task Update_ReturnsUpdatedExperience()
    {
        var request = new CandidateExperienceRequest { Employer = "C", Position = "D" };
        var expectedResponse = new CandidateExperienceResponse(_experienceId, "C", "D", null, new DateOnly(2020, 1, 1), null);

        _repositoryMock
            .Setup(r => r.UpdateExperienceAsync(_userId, _experienceId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        var result = await controller.Update(_experienceId, request, CancellationToken.None);

        Assert.Equal(expectedResponse, result.Value);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenExperienceDoesNotExist()
    {
        var request = new CandidateExperienceRequest { Employer = "C", Position = "D" };

        _repositoryMock
            .Setup(r => r.UpdateExperienceAsync(_userId, _experienceId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CandidateExperienceResponse?)null);

        var controller = CreateController();

        var result = await controller.Update(_experienceId, request, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Remove_ReturnsNoContent_WhenSuccessful()
    {
        _repositoryMock
            .Setup(r => r.RemoveExperienceAsync(_userId, _experienceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController();

        var result = await controller.Remove(_experienceId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Remove_ReturnsNotFound_WhenExperienceDoesNotExist()
    {
        _repositoryMock
            .Setup(r => r.RemoveExperienceAsync(_userId, _experienceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController();

        var result = await controller.Remove(_experienceId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
