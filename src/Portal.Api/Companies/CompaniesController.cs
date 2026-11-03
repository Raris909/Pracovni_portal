using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

[ApiController]
[Authorize(Policy = UserRoles.Company)]
[Route("api/companies")]
public sealed class CompaniesController(CompanyMembershipService memberships) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CompanyMembershipResponse>> Create(
        CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var (membership, status) = await memberships.CreateCompanyAsync(userId, request, cancellationToken);

        return status switch
        {
            CreateCompanyStatus.UserNotFound => Forbid(),
            CreateCompanyStatus.DuplicateRegistrationNumber => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Firma s tímto registračním číslem již v dané zemi existuje."),
            CreateCompanyStatus.Success => CreatedAtAction(
                nameof(Get),
                new { companyId = membership!.Company.Id },
                membership),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Neočekávaná chyba serveru.")
        };
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyMembershipResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        return Ok(await memberships.ListAsync(userId, cancellationToken));
    }

    [HttpGet("{companyId:guid}")]
    public async Task<ActionResult<CompanyMembershipResponse>> Get(Guid companyId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var membership = await memberships.FindAsync(userId, companyId, cancellationToken);
        if (membership is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Firma nebyla nalezena.");

        return membership;
    }
}
