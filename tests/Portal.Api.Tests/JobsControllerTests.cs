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
            .Setup(r => r.GetPublishedJobsAsync(
                new JobOfferSearchQuery(20, 0, null, null, null, null, null, JobOfferSortOptions.Newest),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedJobs);

        var result = await _controller.List(CancellationToken.None, offset: 0, limit: 20);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(expectedJobs, okResult.Value);
    }

    [Fact]
    public async Task List_NormalizesAndForwardsAllSearchFiltersToRepository()
    {
        var expectedJobs = new List<JobOfferResponse>
        {
            new(Guid.NewGuid(), "Senior .NET Developer", "Acme", "Praha", WorkModes.Remote)
        };

        JobOfferSearchQuery? captured = null;
        _repositoryMock
            .Setup(r => r.GetPublishedJobsAsync(It.IsAny<JobOfferSearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<JobOfferSearchQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(expectedJobs);

        var result = await _controller.List(
            CancellationToken.None,
            offset: 10,
            limit: 25,
            q: "  C# PostgreSQL  ",
            workMode: " REMOTE ",
            employmentType: " Full_Time ",
            location: "  Praha  ",
            salaryMin: 80000m,
            sort: " SALARY_DESC ");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(expectedJobs, okResult.Value);
        Assert.Equal(new JobOfferSearchQuery(
            Limit: 25,
            Offset: 10,
            Query: "C# PostgreSQL",
            WorkMode: WorkModes.Remote,
            EmploymentType: EmploymentTypes.FullTime,
            Location: "Praha",
            SalaryMin: 80000m,
            Sort: JobOfferSortOptions.SalaryDesc), captured);
    }

    [Fact]
    public async Task List_TreatsWhitespaceFiltersAsNullAndUsesDefaultSort()
    {
        JobOfferSearchQuery? captured = null;
        _repositoryMock
            .Setup(r => r.GetPublishedJobsAsync(It.IsAny<JobOfferSearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<JobOfferSearchQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync([]);

        var result = await _controller.List(
            CancellationToken.None,
            q: "   ",
            workMode: " ",
            employmentType: "",
            location: "\t",
            sort: "  ");

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(new JobOfferSearchQuery(20, 0, null, null, null, null, null, JobOfferSortOptions.Newest), captured);
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

        _repositoryMock.Verify(
            r => r.GetPublishedJobsAsync(It.IsAny<JobOfferSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("office", null, null, null)]
    [InlineData(null, "freelance", null, null)]
    [InlineData(null, null, -1, null)]
    [InlineData(null, null, null, "salary_asc")]
    public async Task List_ReturnsBadRequest_WhenFilterOrSortIsInvalid(
        string? workMode, string? employmentType, int? salaryMin, string? sort)
    {
        var result = await _controller.List(
            CancellationToken.None,
            workMode: workMode,
            employmentType: employmentType,
            salaryMin: salaryMin,
            sort: sort);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, problemResult.StatusCode);

        _repositoryMock.Verify(
            r => r.GetPublishedJobsAsync(It.IsAny<JobOfferSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
