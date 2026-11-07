using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portal.Api.Applications;
using Portal.Contracts.Applications;
using Portal.Contracts.Auth;
using Xunit;

namespace Portal.Api.Tests;

public sealed class ApplicationsControllerTests
{
    private static ApplicationsController CreateController(
        IApplicationSubmissionService service,
        Guid userId)
    {
        var controller = new ApplicationsController(service);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Role, UserRoles.Candidate)
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };

        return controller;
    }

    private static SubmittedApplicationResult CreateDummyResult(
        Guid appId,
        Guid jobOfferId,
        Guid candidateId,
        string? coverMessage,
        IReadOnlyList<Guid> docIds)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ApplicationProfileSnapshot(
            FirstName: "Jan",
            LastName: "Novák",
            Email: "jan.novak@example.com",
            Phone: "+420123456789",
            Headline: ".NET Developer",
            Summary: "Backend engineer",
            City: "Praha",
            CountryCode: "CZ",
            Skills: ["C#", "PostgreSQL"],
            Experiences: [],
            Education: []);

        return new SubmittedApplicationResult(
            Id: appId,
            JobOfferId: jobOfferId,
            CompanyId: Guid.NewGuid(),
            CandidateId: candidateId,
            Status: ApplicationStatuses.Submitted,
            CoverMessage: coverMessage,
            ProfileSnapshot: snapshot,
            AttachedDocumentIds: docIds,
            SubmittedAt: now,
            UpdatedAt: now);
    }

    [Fact]
    public async Task Submit_ValidRequest_ReturnsCreatedWithSnapshotAndSelectedDocuments()
    {
        var candidateId = Guid.NewGuid();
        var jobOfferId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var expectedAppId = Guid.NewGuid();

        var service = new StubApplicationSubmissionService((cId, jId, cover, docs) =>
            Task.FromResult(CreateDummyResult(expectedAppId, jId, cId, cover, docs ?? [])));

        var controller = CreateController(service, candidateId);
        var request = new SubmitApplicationWithJobIdRequest(
            JobOfferId: jobOfferId,
            CoverMessage: "Mám velký zájem o tuto pozici.",
            DocumentIds: [docId]);

        var actionResult = await controller.Submit(request);

        var created = Assert.IsType<CreatedResult>(actionResult.Result);
        Assert.Equal($"/api/applications/{expectedAppId}", created.Location);
        var payload = Assert.IsType<SubmittedApplicationResult>(created.Value);
        Assert.Equal(expectedAppId, payload.Id);
        Assert.Equal(jobOfferId, payload.JobOfferId);
        Assert.Equal(candidateId, payload.CandidateId);
        Assert.Equal(ApplicationStatuses.Submitted, payload.Status);
        Assert.Equal("Jan", payload.ProfileSnapshot.FirstName);
        Assert.Single(payload.AttachedDocumentIds, docId);
    }

    [Fact]
    public async Task SubmitForJobOffer_AlreadyApplied_ReturnsConflict()
    {
        var candidateId = Guid.NewGuid();
        var service = new StubApplicationSubmissionService((_, _, _, _) =>
            throw new ApplicationSubmissionException(
                ApplicationSubmissionErrorKind.AlreadyApplied,
                "Already applied."));

        var controller = CreateController(service, candidateId);

        var actionResult = await controller.SubmitForJobOffer(
            Guid.NewGuid(),
            new SubmitJobApplicationBodyRequest("Cover letter", null));

        Assert.IsType<ConflictObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task Submit_ClosedOrMissingJobOffer_ReturnsNotFound()
    {
        var candidateId = Guid.NewGuid();
        var service = new StubApplicationSubmissionService((_, _, _, _) =>
            throw new ApplicationSubmissionException(
                ApplicationSubmissionErrorKind.JobOfferNotFoundOrNotOpen,
                "Job offer is not open."));

        var controller = CreateController(service, candidateId);

        var actionResult = await controller.Submit(
            new SubmitApplicationWithJobIdRequest(Guid.NewGuid()));

        Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task Submit_InvalidDocumentSelection_ReturnsBadRequest()
    {
        var candidateId = Guid.NewGuid();
        var service = new StubApplicationSubmissionService((_, _, _, _) =>
            throw new ApplicationSubmissionException(
                ApplicationSubmissionErrorKind.InvalidDocumentSelection,
                "Document does not belong to user."));

        var controller = CreateController(service, candidateId);

        var actionResult = await controller.Submit(
            new SubmitApplicationWithJobIdRequest(Guid.NewGuid(), null, [Guid.NewGuid()]));

        Assert.IsType<BadRequestObjectResult>(actionResult.Result);
    }

    private sealed class StubApplicationSubmissionService : IApplicationSubmissionService
    {
        private readonly Func<Guid, Guid, string?, IReadOnlyList<Guid>?, Task<SubmittedApplicationResult>> _handler;

        public StubApplicationSubmissionService(
            Func<Guid, Guid, string?, IReadOnlyList<Guid>?, Task<SubmittedApplicationResult>> handler)
        {
            _handler = handler;
        }

        public Task<SubmittedApplicationResult> SubmitApplicationAsync(
            Guid candidateId,
            Guid jobOfferId,
            string? coverMessage,
            IReadOnlyList<Guid>? documentIds,
            CancellationToken cancellationToken = default) =>
            _handler(candidateId, jobOfferId, coverMessage, documentIds);
    }
}