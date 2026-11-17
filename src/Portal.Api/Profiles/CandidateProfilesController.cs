using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Contracts.Auth;
using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

[ApiController]
[Authorize(Policy = UserRoles.Candidate)]
[Route("api/profile")]
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
[Route("api/profile/experiences")]
public sealed class CandidateExperiencesController(ICandidateProfileRepository profiles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CandidateExperienceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CandidateExperienceResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        return Ok(await profiles.GetExperiencesAsync(userId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CandidateExperienceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CandidateExperienceResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var experience = await profiles.GetExperienceAsync(userId, id, cancellationToken);
        if (experience is null)
            return NotFound();

        return experience;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CandidateExperienceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidateExperienceResponse>> Add(
        CandidateExperienceRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var experience = await profiles.AddExperienceAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = experience.Id }, experience);
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
[Route("api/profile/education")]
public sealed class CandidateEducationController(ICandidateProfileRepository profiles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CandidateEducationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CandidateEducationResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        return Ok(await profiles.GetEducationAsync(userId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CandidateEducationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CandidateEducationResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var education = await profiles.GetEducationByIdAsync(userId, id, cancellationToken);
        if (education is null)
            return NotFound();

        return education;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CandidateEducationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidateEducationResponse>> Add(
        CandidateEducationRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue("sub")!);
        var education = await profiles.AddEducationAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = education.Id }, education);
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
