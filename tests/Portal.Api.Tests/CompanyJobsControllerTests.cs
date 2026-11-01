using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Portal.Api.Companies;
using Portal.Api.Jobs;
using Portal.Contracts.Jobs;

namespace Portal.Api.Tests;

public class CompanyJobsControllerTests
{
    private readonly Mock<IJobOfferRepository> _repositoryMock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _jobId = Guid.NewGuid();

    private CompanyJobsController CreateController()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", _userId.ToString())
        ], "TestAuth"));

        var controller = new CompanyJobsController(_repositoryMock.Object, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
        return controller;
    }

    [Fact]
    public async Task Update_ReturnsOk_WhenRepositoryReturnsSuccess()
    {
        var request = new JobOfferRequest { Title = "Nový název", WorkMode = WorkModes.Remote, EmploymentType = EmploymentTypes.FullTime };
        var updatedJob = new JobOfferManagementResponse(_jobId, _companyId, _userId, "Nový název", "Desc", "Req", null, WorkModes.Remote, EmploymentTypes.FullTime, null, null, null, null, JobOfferStatuses.Draft, null, null, DateTimeOffset.Now, DateTimeOffset.Now);

        _repositoryMock
            .Setup(r => r.UpdateJobAsync(_companyId, _jobId, _userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((updatedJob, JobUpdateStatus.Success));

        var controller = CreateController();

        var result = await controller.Update(_companyId, _jobId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(updatedJob, okResult.Value);
    }

    [Fact]
    public async Task Update_ReturnsConflict_WhenJobIsNotInDraftStatus()
    {
        var request = new JobOfferRequest { Title = "Pokus o úpravu vydané nabídky" };

        _repositoryMock
            .Setup(r => r.UpdateJobAsync(_companyId, _jobId, _userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, JobUpdateStatus.NotDraft));

        var controller = CreateController();

        var result = await controller.Update(_companyId, _jobId, request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, problemResult.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenJobDoesNotExist()
    {
        var request = new JobOfferRequest { Title = "Neexistující" };

        _repositoryMock
            .Setup(r => r.UpdateJobAsync(_companyId, _jobId, _userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, JobUpdateStatus.OfferNotFound));

        var controller = CreateController();

        var result = await controller.Update(_companyId, _jobId, request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, problemResult.StatusCode);
    }
}
