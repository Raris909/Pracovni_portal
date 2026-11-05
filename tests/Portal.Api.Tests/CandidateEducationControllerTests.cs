using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Profiles;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.Api.Tests;

public sealed class CandidateEducationControllerTests
{
    private readonly Mock<ICandidateProfileRepository> _repositoryMock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _educationId = Guid.NewGuid();

    private CandidateEducationController CreateController()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", _userId.ToString()),
            new Claim("role", UserRoles.Candidate)
        ], "TestAuth"));

        return new CandidateEducationController(_repositoryMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public async Task Add_ReturnsCreatedEducation()
    {
        var request = new CandidateEducationRequest { School = "UK", StartDate = new DateOnly(2020, 1, 1) };
        var expectedResponse = new CandidateEducationResponse(_educationId, "UK", null, null, new DateOnly(2020, 1, 1), null);

        _repositoryMock
            .Setup(r => r.AddEducationAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        var result = await controller.Add(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(expectedResponse, createdResult.Value);
    }

    [Fact]
    public async Task Update_ReturnsUpdatedEducation()
    {
        var request = new CandidateEducationRequest { School = "UK", StartDate = new DateOnly(2020, 1, 1) };
        var expectedResponse = new CandidateEducationResponse(_educationId, "UK", null, null, new DateOnly(2020, 1, 1), null);

        _repositoryMock
            .Setup(r => r.UpdateEducationAsync(_userId, _educationId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        var result = await controller.Update(_educationId, request, CancellationToken.None);

        Assert.Equal(expectedResponse, result.Value);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenEducationDoesNotExist()
    {
        var request = new CandidateEducationRequest { School = "UK", StartDate = new DateOnly(2020, 1, 1) };

        _repositoryMock
            .Setup(r => r.UpdateEducationAsync(_userId, _educationId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CandidateEducationResponse?)null);

        var controller = CreateController();

        var result = await controller.Update(_educationId, request, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Remove_ReturnsNoContent_WhenSuccessful()
    {
        _repositoryMock
            .Setup(r => r.RemoveEducationAsync(_userId, _educationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController();

        var result = await controller.Remove(_educationId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Remove_ReturnsNotFound_WhenEducationDoesNotExist()
    {
        _repositoryMock
            .Setup(r => r.RemoveEducationAsync(_userId, _educationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController();

        var result = await controller.Remove(_educationId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
