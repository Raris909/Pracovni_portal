using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Applications;
using Portal.Contracts.Auth;

namespace Portal.Api.Applications;

[ApiController]
[Authorize(Roles = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/applications")]
public sealed class CompanyApplicationsController : ControllerBase
{
    private readonly ICompanyApplicationRepository _repository;

    public CompanyApplicationsController(ICompanyApplicationRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyApplicationSummaryResponse>>> GetApplications(
        Guid companyId,
        [FromQuery] Guid? jobOfferId = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? assignedMemberId = null,
        CancellationToken cancellationToken = default)
    {
        var authError = await AuthorizeCompanyMemberAsync(companyId, cancellationToken);
        if (authError is not null)
        {
            return authError;
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = status.Trim().ToLowerInvariant();
            if (!ApplicationStatuses.All.Contains(normalizedStatus))
            {
                return BadRequest(new
                {
                    message = $"Invalid status filter '{status}'. Allowed values: {string.Join(", ", ApplicationStatuses.All)}."
                });
            }
        }

        var applications = await _repository.GetCompanyApplicationsAsync(
            companyId,
            jobOfferId,
            normalizedStatus,
            assignedMemberId,
            cancellationToken);

        return Ok(applications);
    }

    [HttpGet("~/api/companies/{companyId:guid}/jobs/{jobOfferId:guid}/applications")]
    public Task<ActionResult<IReadOnlyList<CompanyApplicationSummaryResponse>>> GetJobOfferApplications(
        Guid companyId,
        Guid jobOfferId,
        [FromQuery] string? status = null,
        [FromQuery] Guid? assignedMemberId = null,
        CancellationToken cancellationToken = default) =>
        GetApplications(companyId, jobOfferId, status, assignedMemberId, cancellationToken);

    [HttpGet("{applicationId:guid}")]
    public async Task<ActionResult<CompanyApplicationDetailResponse>> GetApplicationById(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var authError = await AuthorizeCompanyMemberAsync(companyId, cancellationToken);
        if (authError is not null)
        {
            return authError;
        }

        var detail = await _repository.GetCompanyApplicationByIdAsync(companyId, applicationId, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { message = "Application not found." });
        }

        return Ok(detail);
    }

    [HttpPut("{applicationId:guid}/status")]
    public async Task<ActionResult<CompanyApplicationDetailResponse>> UpdateStatus(
        Guid companyId,
        Guid applicationId,
        [FromBody] UpdateCompanyApplicationStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var authError = await AuthorizeCompanyMemberAsync(companyId, cancellationToken);
        if (authError is not null)
        {
            return authError;
        }

        var normalizedStatus = request.Status.Trim().ToLowerInvariant();
        var updated = await _repository.UpdateApplicationStatusAsync(
            companyId,
            applicationId,
            normalizedStatus,
            cancellationToken);

        if (updated is null)
        {
            return NotFound(new { message = "Application not found." });
        }

        return Ok(updated);
    }

    [HttpPut("{applicationId:guid}/assignee")]
    public async Task<ActionResult<CompanyApplicationDetailResponse>> AssignMember(
        Guid companyId,
        Guid applicationId,
        [FromBody] AssignApplicationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var authError = await AuthorizeCompanyMemberAsync(companyId, cancellationToken);
        if (authError is not null)
        {
            return authError;
        }

        var (foundApp, validMember, updated) = await _repository.AssignMemberAsync(
            companyId,
            applicationId,
            request.AssignedMemberId,
            cancellationToken);

        if (!foundApp)
        {
            return NotFound(new { message = "Application not found." });
        }

        if (!validMember)
        {
            return BadRequest(new { message = "Assigned member does not exist or is not an active member of this company." });
        }

        return Ok(updated!);
    }

    [HttpGet("{applicationId:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<ApplicationAttachedDocumentResponse>>> GetApplicationDocuments(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var authError = await AuthorizeCompanyMemberAsync(companyId, cancellationToken);
        if (authError is not null)
        {
            return authError;
        }

        var documents = await _repository.GetApplicationDocumentsAsync(companyId, applicationId, cancellationToken);
        if (documents is null)
        {
            return NotFound(new { message = "Application not found." });
        }

        return Ok(documents);
    }

    [HttpGet("{applicationId:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> DownloadApplicationDocument(
        Guid companyId,
        Guid applicationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var authError = await AuthorizeCompanyMemberAsync(companyId, cancellationToken);
        if (authError is not null)
        {
            return authError;
        }

        var docRecord = await _repository.GetApplicationDocumentFileAsync(
            companyId,
            applicationId,
            documentId,
            cancellationToken);

        if (docRecord is null)
        {
            return NotFound(new { message = "Attached document not found for this application." });
        }

        var storageRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "App_Data", "private-documents"));
        var relativePath = docRecord.StorageKey.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(storageRoot, relativePath));

        if (fullPath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(fullPath))
        {
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            return File(stream, docRecord.MimeType, docRecord.OriginalFilename);
        }

        return Ok(new ApplicationAttachedDocumentResponse(
            docRecord.DocumentId,
            docRecord.Kind,
            docRecord.OriginalFilename,
            docRecord.MimeType,
            docRecord.SizeBytes,
            docRecord.AttachedAt));
    }

    private async Task<ActionResult?> AuthorizeCompanyMemberAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var access = await _repository.CheckCompanyAccessAsync(companyId, userId, cancellationToken);
        return access switch
        {
            CompanyAccessStatus.Authorized => null,
            CompanyAccessStatus.CompanyNotFound => NotFound(new { message = "Company not found." }),
            _ => Forbid()
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}