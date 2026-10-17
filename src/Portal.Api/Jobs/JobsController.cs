using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Portal.Contracts.Jobs;

namespace Portal.Api.Jobs;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(NpgsqlDataSource database) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobOfferResponse>>> List(
        CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 100)
            return Problem(statusCode: 400, title: "Offset musí být nezáporný a limit musí být 1 až 100.");

        await using var command = database.CreateCommand("""
            SELECT j.id, j.title, c.name, j.location, j.work_mode
            FROM portal.job_offers j JOIN portal.companies c ON c.id = j.company_id
            WHERE j.status = 'published'
              AND (j.application_deadline IS NULL OR j.application_deadline > now())
            ORDER BY j.published_at DESC, j.id
            LIMIT $1 OFFSET $2
            """);
        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(offset);
        var jobs = new List<JobOfferResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
        }

        return jobs;
    }
}
