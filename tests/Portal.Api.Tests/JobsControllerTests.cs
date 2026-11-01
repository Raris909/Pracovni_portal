using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Jobs;
using Portal.Contracts.Jobs;

namespace Portal.Api.Tests;

public class JobsControllerTests
{
    private readonly Mock<IJobOfferRepository> _repositoryMock = new();
    private readonly JobsController _controller;

    public JobsControllerTests()
    {
        _controller = new JobsController(_repositoryMock.Object);
    }

    [Fact]
    public async Task List_ReturnsOkWithJobs_WhenPaginationIsValid()
    {
        var expectedJobs = new List<JobOfferResponse>
        {
            new(Guid.NewGuid(), "C# Vývojář", "Test s.r.o.", "Praha", WorkModes.Hybrid)
        };

        _repositoryMock
            .Setup(r => r.GetPublishedJobsAsync(20, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedJobs);

        var result = await _controller.List(CancellationToken.None, offset: 0, limit: 20);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(expectedJobs, okResult.Value);
    }

    [Theory]
    [InlineData(-1, 20)] 
    [InlineData(0, 0)]   
    [InlineData(0, 101)] 
    public async Task List_ReturnsBadRequest_WhenPaginationIsInvalid(int offset, int limit)
    {
        var result = await _controller.List(CancellationToken.None, offset, limit);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, problemResult.StatusCode);

        _repositoryMock.Verify(r => r.GetPublishedJobsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
