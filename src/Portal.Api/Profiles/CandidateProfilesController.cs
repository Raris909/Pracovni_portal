using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

[ApiController]
[Authorize(Policy = UserRoles.Candidate)]
[Route("api/profiles/me")]
public sealed class CandidateProfilesController(ICandidateProfileRepository profiles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CandidateProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CandidateProfileResponse>> Get(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var profile = await profiles.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
            return NotFound();

        return profile;
    }

    [HttpPut]
    [ProducesResponseType(typeof(CandidateProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidateProfileResponse>> Update(
        UpdateCandidateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var profile = await profiles.UpdateProfileAsync(userId, request, cancellationToken);
        if (profile is null)
            return NotFound();

        return profile;
    }
}

[ApiController]
[Authorize(Policy = UserRoles.Candidate)]
[Route("api/profiles/me/experiences")]
public sealed class CandidateExperiencesController(ICandidateProfileRepository profiles) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CandidateExperienceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidateExperienceResponse>> Add(
        CandidateExperienceRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var experience = await profiles.AddExperienceAsync(userId, request, cancellationToken);
        return CreatedAtAction(null, experience);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CandidateExperienceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidateExperienceResponse>> Update(
        Guid id, CandidateExperienceRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var experience = await profiles.UpdateExperienceAsync(userId, id, request, cancellationToken);
        if (experience is null)
            return NotFound();

        return experience;
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        if (!await profiles.RemoveExperienceAsync(userId, id, cancellationToken))
            return NotFound();

        return NoContent();
    }
}

[ApiController]
[Authorize(Policy = UserRoles.Candidate)]
[Route("api/profiles/me/education")]
public sealed class CandidateEducationController(ICandidateProfileRepository profiles) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CandidateEducationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidateEducationResponse>> Add(
        CandidateEducationRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var education = await profiles.AddEducationAsync(userId, request, cancellationToken);
        return CreatedAtAction(null, education);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CandidateEducationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidateEducationResponse>> Update(
        Guid id, CandidateEducationRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var education = await profiles.UpdateEducationAsync(userId, id, request, cancellationToken);
        if (education is null)
            return NotFound();

        return education;
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        if (!await profiles.RemoveEducationAsync(userId, id, cancellationToken))
            return NotFound();

        return NoContent();
    }
}
