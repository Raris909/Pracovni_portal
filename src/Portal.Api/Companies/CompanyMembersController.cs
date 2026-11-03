using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies/{companyId:guid}/members")]
public sealed class CompanyMembersController(CompanyMembershipService memberships) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyMemberResponse>>> List(
        Guid companyId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var members = await memberships.GetMembersAsync(userId, companyId, cancellationToken);
        if (members is null)
            return CompanyNotFound();

        return Ok(members);
    }

    [HttpPost]
    public async Task<ActionResult<CompanyMemberResponse>> Add(
        Guid companyId, AddCompanyMemberRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (member, status) = await memberships.AddMemberAsync(companyId, userId, request, cancellationToken);

        return status switch
        {
            MemberManagementStatus.CompanyNotFound => CompanyNotFound(),
            MemberManagementStatus.Forbidden => ForbiddenManagement(),
            MemberManagementStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Aktivní firemní účet s tímto e-mailem nebyl nalezen."),
            MemberManagementStatus.AlreadyMemberOrOwnerConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Uživatel je již členem firmy nebo má chráněné členství vlastníka."),
            MemberManagementStatus.Success => CreatedAtAction(nameof(List), new { companyId }, member!),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    [HttpPatch("{memberId:guid}")]
    public async Task<ActionResult<CompanyMemberResponse>> Update(
        Guid companyId, Guid memberId, UpdateCompanyMemberRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (member, status) = await memberships.UpdateMemberRoleAsync(companyId, userId, memberId, request, cancellationToken);

        return status switch
        {
            MemberManagementStatus.CompanyNotFound => CompanyNotFound(),
            MemberManagementStatus.Forbidden => ForbiddenManagement(),
            MemberManagementStatus.MemberNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Člen firmy nebyl nalezen."),
            MemberManagementStatus.CannotModifyOwner => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Členství vlastníka firmy nelze změnit ani odebrat."),
            MemberManagementStatus.Success => Ok(member!),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    [HttpDelete("{memberId:guid}")]
    public async Task<IActionResult> Remove(Guid companyId, Guid memberId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var status = await memberships.RemoveMemberAsync(companyId, userId, memberId, cancellationToken);

        return status switch
        {
            MemberManagementStatus.CompanyNotFound => CompanyNotFound(),
            MemberManagementStatus.Forbidden => ForbiddenManagement(),
            MemberManagementStatus.MemberNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Člen firmy nebyl nalezen."),
            MemberManagementStatus.CannotModifyOwner => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Členství vlastníka firmy nelze změnit ani odebrat."),
            MemberManagementStatus.Success => NoContent(),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    private ObjectResult CompanyNotFound() =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Firma nebyla nalezena.");

    private ObjectResult ForbiddenManagement() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nemáte oprávnění spravovat HR tým.");
}
