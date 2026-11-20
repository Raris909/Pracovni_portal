using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Recruitment;

namespace Portal.IntegrationTests;

public sealed class InterviewBookingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private readonly DateTimeOffset start = new(DateTime.UtcNow.Date.AddDays(30).AddHours(10), TimeSpan.Zero);

    [Fact]
    public async Task CandidateCanDiscoverBookCancelAndRebookWhileKeepingHistory()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var available = Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        Assert.Equal(context.SlotId, available.Id);
        Assert.True(available.IsActive);
        Assert.False(available.IsBooked);
        Assert.Empty((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!);

        var response = await client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(context.SlotId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{context.Path}/bookings", response.Headers.Location?.AbsolutePath);
        var booking = (await response.Content.ReadFromJsonAsync<InterviewBookingResponse>())!;
        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(context.SlotId, booking.SlotId);
        Assert.Equal(context.ApplicationId, booking.ApplicationId);
        Assert.Equal(context.StageId, booking.StageId);
        Assert.Equal(context.JobId, booking.JobOfferId);
        Assert.Equal(context.CompanyId, booking.CompanyId);
        Assert.Equal("confirmed", booking.Status);
        Assert.Equal(start, booking.StartsAt);
        Assert.Equal(start.AddHours(1), booking.EndsAt);
        Assert.Equal("Praha", booking.Location);
        Assert.Equal("https://meet.example.com/interview", booking.MeetingUrl);
        Assert.Null(booking.CancelledAt);
        Assert.NotEqual(default, booking.BookedAt);
        Assert.Equal(booking.BookedAt, booking.UpdatedAt);
        Assert.Equal(booking, Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>(response.Headers.Location))!));
        Assert.Empty((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(context.SlotId))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{context.Path}/bookings/{booking.Id}")).StatusCode);
        var cancelled = Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!);
        Assert.Equal(booking with { Status = "cancelled", CancelledAt = cancelled.CancelledAt, UpdatedAt = cancelled.UpdatedAt }, cancelled);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.True(cancelled.CancelledAt >= cancelled.BookedAt);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{context.Path}/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(cancelled, Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!));
        Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        var rebooked = await BookAsync(client, context, context.SlotId);
        Assert.NotEqual(cancelled.Id, rebooked.Id);
        Assert.Equal(new[] { cancelled, rebooked }, await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"));
        Assert.Equal("submitted", await fixture.QueryAsync("SELECT status FROM portal.applications WHERE id = $1", context.ApplicationId));
        Assert.Equal("pending", await fixture.QueryAsync("SELECT status FROM portal.application_stages WHERE application_id = $1", context.ApplicationId));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public async Task ClosedApplicationsRejectBookingsButAllowCancellingFutureReservations(string status)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var booking = await BookAsync(client, context, context.SlotId);
        await fixture.QueryAsync("UPDATE portal.applications SET status = $2 WHERE id = $1", context.ApplicationId, status);
        Assert.Empty((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(context.SlotId))).StatusCode);
        Assert.Equal(booking, Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{context.Path}/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(status, await fixture.QueryAsync("SELECT status FROM portal.applications WHERE id = $1", context.ApplicationId));
    }

    [Theory]
    [InlineData("passed")]
    [InlineData("failed")]
    [InlineData("skipped")]
    public async Task CompletedStagesRejectNewBookings(string status)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        await fixture.QueryAsync("""
            UPDATE portal.application_stages SET status = $2, started_at = now(), completed_at = now()
            WHERE application_id = $1
            """, context.ApplicationId, status);
        Assert.Empty((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(context.SlotId))).StatusCode);
    }

    [Fact]
    public async Task InProgressApplicationAndStageCanBookAfterTheOfferIsClosed()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        await fixture.QueryAsync("UPDATE portal.applications SET status = 'in_progress' WHERE id = $1", context.ApplicationId);
        await fixture.QueryAsync("UPDATE portal.application_stages SET status = 'in_progress', started_at = now() WHERE application_id = $1", context.ApplicationId);
        await fixture.QueryAsync("UPDATE portal.job_offers SET status = 'closed' WHERE id = $1", context.JobId);
        Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        await BookAsync(client, context, context.SlotId);
    }

    [Theory]
    [InlineData("UPDATE portal.interview_slots SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.interview_slots SET starts_at = now() - interval '2 hours', ends_at = now() - interval '1 hour' WHERE id = $1")]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE id = (SELECT interviewer_member_id FROM portal.interview_slots WHERE id = $1)")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = (SELECT m.user_id FROM portal.company_members m JOIN portal.interview_slots s ON s.interviewer_member_id = m.id WHERE s.id = $1)")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = (SELECT m.user_id FROM portal.company_members m JOIN portal.interview_slots s ON s.interviewer_member_id = m.id WHERE s.id = $1)")]
    public async Task UnavailableSlotsAreHiddenAndCannotBeBooked(string sql)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        await fixture.QueryAsync(sql, context.SlotId);
        Assert.Empty((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(context.SlotId))).StatusCode);
        Assert.Equal(0L, await fixture.QueryAsync("SELECT count(*) FROM portal.interview_bookings WHERE application_id = $1", context.ApplicationId));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("no_show")]
    [InlineData("confirmed")]
    public async Task FinalOrPastBookingsCannotBeCancelled(string status)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var booking = await BookAsync(client, context, context.SlotId);
        await fixture.QueryAsync("UPDATE portal.interview_bookings SET status = $2 WHERE id = $1", booking.Id, status);
        if (status == "confirmed")
            await fixture.QueryAsync("""
                UPDATE portal.interview_slots SET starts_at = now() - interval '2 hours', ends_at = now() - interval '1 hour' WHERE id = $1
                """, context.SlotId);
        var before = Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{context.Path}/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(before, Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!));
    }

    [Fact]
    public async Task CandidateCannotHoldTwoBookingsForOneApplicationStage()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var secondSlot = await CreateSlotAsync(context, start.AddHours(2));
        var booking = await BookAsync(client, context, context.SlotId);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(secondSlot))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        Assert.Equal(booking, Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!));
    }

    [Fact]
    public async Task OverlappingCandidateInterviewsAcrossCompaniesAreRejectedButAdjacentOnesAreAllowed()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var second = await CreateScenarioAsync(client, first.CandidateId);
        await BookAsync(client, first, first.SlotId);
        Assert.Empty((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{second.Path}/slots"))!);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{second.Path}/bookings", new CreateInterviewBookingRequest(second.SlotId))).StatusCode);
        var adjacent = await CreateSlotAsync(second, start.AddHours(1));
        Assert.Equal(adjacent, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{second.Path}/slots"))!).Id);
        await BookAsync(client, second, adjacent);
    }

    [Fact]
    public async Task ResourcesAreScopedToTheCandidateApplicationAndAssignedStage()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var booking = await BookAsync(client, first, first.SlotId);
        var second = await CreateScenarioAsync(client, first.CandidateId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"{second.Path}/bookings", new CreateInterviewBookingRequest(first.SlotId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{second.Path}/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{first.Path}/bookings/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"{second.Path}/bookings", new CreateInterviewBookingRequest(Guid.NewGuid()))).StatusCode);
        foreach (var path in new[]
        {
            $"/api/applications/{Guid.NewGuid()}/stages/{first.StageId}",
            $"/api/applications/{first.ApplicationId}/stages/{second.StageId}"
        })
            await AssertDeniedAsync(client, path, first.SlotId, booking.Id, HttpStatusCode.NotFound);

        var unassignedStage = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position) VALUES ($1, 'Unassigned', 'interview', 2) RETURNING id
            """, first.JobId))!;
        await AssertDeniedAsync(client, $"/api/applications/{first.ApplicationId}/stages/{unassignedStage}", first.SlotId, booking.Id, HttpStatusCode.NotFound);
        var screening = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position) VALUES ($1, 'Screening', 'screening', 3) RETURNING id
            """, first.JobId))!;
        await AssignStageAsync(first.ApplicationId, screening, first.JobId, first.CompanyId);
        await AssertDeniedAsync(client, $"/api/applications/{first.ApplicationId}/stages/{screening}", first.SlotId, booking.Id, HttpStatusCode.NotFound);

        using var outsider = fixture.CreateClient();
        await RegisterAsync(outsider);
        await AssertDeniedAsync(outsider, first.Path, first.SlotId, booking.Id, HttpStatusCode.NotFound);
        Assert.Equal(booking, Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{first.Path}/bookings"))!));
    }

    [Theory]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'company') WHERE id = $1")]
    public async Task RevokedCandidateAccessIsRejectedWithAnExistingJwt(string sql)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var booking = await BookAsync(client, context, context.SlotId);
        await fixture.QueryAsync(sql, context.CandidateId);
        await AssertDeniedAsync(client, context.Path, context.SlotId, booking.Id, HttpStatusCode.NotFound);
        Assert.Equal("confirmed", await fixture.QueryAsync("SELECT status FROM portal.interview_bookings WHERE id = $1", booking.Id));
    }

    [Fact]
    public async Task AnonymousAndCompanyRequestsAreRejected()
    {
        using var candidate = fixture.CreateClient();
        var context = await CreateScenarioAsync(candidate);
        using var client = fixture.CreateClient();
        await AssertDeniedAsync(client, context.Path, context.SlotId, Guid.NewGuid(), HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", context.HrToken);
        await AssertDeniedAsync(client, context.Path, context.SlotId, Guid.NewGuid(), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InvalidIdsAreRejectedAndClientCannotForgeBookingMetadata()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        foreach (var body in new object[] { new CreateInterviewBookingRequest(Guid.Empty), new { }, new { slotId = "invalid" } })
        {
            var invalid = await client.PostAsJsonAsync($"{context.Path}/bookings", body);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
        }
        var fakeId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"{context.Path}/bookings", new
        {
            slotId = context.SlotId, id = fakeId, applicationId = Guid.NewGuid(), stageId = Guid.NewGuid(),
            companyId = Guid.NewGuid(), jobOfferId = Guid.NewGuid(), status = "completed",
            bookedAt = "2000-01-01T00:00:00Z", cancelledAt = "2000-01-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<InterviewBookingResponse>())!;
        Assert.NotEqual(fakeId, booking.Id);
        Assert.Equal(context.ApplicationId, booking.ApplicationId);
        Assert.Equal(context.StageId, booking.StageId);
        Assert.Equal(context.CompanyId, booking.CompanyId);
        Assert.Equal(context.JobId, booking.JobOfferId);
        Assert.Equal("confirmed", booking.Status);
        Assert.Null(booking.CancelledAt);
    }

    [Fact]
    public async Task TwoCandidatesCompetingForOneSlotHaveOneWinner()
    {
        using var first = fixture.CreateClient();
        var context = await CreateScenarioAsync(first);
        using var second = fixture.CreateClient();
        var secondUser = await RegisterAsync(second);
        var secondApp = await CreateApplicationAsync(secondUser.Id, context.JobId, context.CompanyId, context.StageId);
        var secondContext = context with { CandidateId = secondUser.Id, ApplicationId = secondApp };
        var request = new CreateInterviewBookingRequest(context.SlotId);
        var responses = await Task.WhenAll(first.PostAsJsonAsync($"{context.Path}/bookings", request),
            second.PostAsJsonAsync($"{secondContext.Path}/bookings", request));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.interview_bookings WHERE slot_id = $1 AND status <> 'cancelled'", context.SlotId));
    }

    [Fact]
    public async Task SameCandidateCannotReserveTwoSlotsOfOneStageConcurrently()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var secondSlot = await CreateSlotAsync(context, start.AddHours(2));
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(context.SlotId)),
            client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(secondSlot)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!);
    }

    [Fact]
    public async Task OverlappingCandidateBookingsAcrossApplicationsHaveOneWinner()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var second = await CreateScenarioAsync(client, first.CandidateId);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"{first.Path}/bookings", new CreateInterviewBookingRequest(first.SlotId)),
            client.PostAsJsonAsync($"{second.Path}/bookings", new CreateInterviewBookingRequest(second.SlotId)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConcurrentCancellationsAreIdempotentAndReleaseTheSlot()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var booking = await BookAsync(client, context, context.SlotId);
        var responses = await Task.WhenAll(client.DeleteAsync($"{context.Path}/bookings/{booking.Id}"),
            client.DeleteAsync($"{context.Path}/bookings/{booking.Id}"));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>($"{context.Path}/slots"))!);
        Assert.Equal("cancelled", Assert.Single((await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!).Status);
    }

    [Fact]
    public async Task BookingAndHrDeactivationCannotLeaveABookingOnAnInactiveSlot()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        using var hr = fixture.CreateClient();
        hr.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", context.HrToken);
        var bookingTask = client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(context.SlotId));
        var deactivateTask = hr.DeleteAsync($"{context.HrPath}/{context.SlotId}");
        await Task.WhenAll(bookingTask, deactivateTask);
        var bookings = (await client.GetFromJsonAsync<InterviewBookingResponse[]>($"{context.Path}/bookings"))!;
        if ((await bookingTask).StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(HttpStatusCode.Conflict, (await deactivateTask).StatusCode);
            Assert.Single(bookings);
            Assert.Equal(true, await fixture.QueryAsync("SELECT is_active FROM portal.interview_slots WHERE id = $1", context.SlotId));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, (await bookingTask).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await deactivateTask).StatusCode);
            Assert.Empty(bookings);
        }
    }

    private sealed record TestContext(
        Guid CandidateId, Guid CompanyId, Guid MemberId, Guid JobId, Guid StageId, Guid ApplicationId, Guid SlotId, string HrToken)
    {
        public string Path => $"/api/applications/{ApplicationId}/stages/{StageId}";
        public string HrPath => $"/api/companies/{CompanyId}/jobs/{JobId}/stages/{StageId}/slots";
    }

    private async Task<TestContext> CreateScenarioAsync(HttpClient client, Guid? candidateId = null)
    {
        var currentCandidateId = candidateId ?? (await RegisterAsync(client)).Id;
        using var hr = fixture.CreateClient();
        var hrUser = await RegisterAsync(hr, UserRoles.Company);
        var companyId = (Guid)(await fixture.QueryAsync("INSERT INTO portal.companies (name, country_code) VALUES ('Test company', 'CZ') RETURNING id"))!;
        var memberId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, 'owner') RETURNING id
            """, companyId, hrUser.Id))!;
        var jobId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.job_offers (company_id, created_by_member_id, title, description, work_mode, employment_type)
            VALUES ($1, $2, 'Developer', 'Description', 'remote', 'full_time') RETURNING id
            """, companyId, memberId))!;
        var stageId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position) VALUES ($1, 'Interview', 'interview', 1) RETURNING id
            """, jobId))!;
        var appId = await CreateApplicationAsync(currentCandidateId, jobId, companyId, stageId);
        var initial = new TestContext(currentCandidateId, companyId, memberId, jobId, stageId, appId, Guid.Empty,
            hr.DefaultRequestHeaders.Authorization!.Parameter!);
        return initial with { SlotId = await CreateSlotAsync(initial, start) };
    }

    private async Task<Guid> CreateSlotAsync(TestContext context, DateTimeOffset beginsAt) =>
        (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.interview_slots (job_offer_id, company_id, stage_id, interviewer_member_id, starts_at, ends_at, location, meeting_url)
            VALUES ($1, $2, $3, $4, $5, $6, 'Praha', 'https://meet.example.com/interview') RETURNING id
            """, context.JobId, context.CompanyId, context.StageId, context.MemberId, beginsAt, beginsAt.AddHours(1)))!;

    private async Task<Guid> CreateApplicationAsync(Guid candidateId, Guid jobId, Guid companyId, Guid stageId)
    {
        var appId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.applications (job_offer_id, company_id, candidate_id, profile_snapshot)
            VALUES ($1, $2, $3, '{}') RETURNING id
            """, jobId, companyId, candidateId))!;
        await AssignStageAsync(appId, stageId, jobId, companyId);
        return appId;
    }

    private async Task AssignStageAsync(Guid appId, Guid stageId, Guid jobId, Guid companyId) =>
        await fixture.QueryAsync("""
            INSERT INTO portal.application_stages (application_id, stage_id, job_offer_id, company_id) VALUES ($1, $2, $3, $4)
            """, appId, stageId, jobId, companyId);

    private static async Task<InterviewBookingResponse> BookAsync(HttpClient client, TestContext context, Guid slotId)
    {
        var response = await client.PostAsJsonAsync($"{context.Path}/bookings", new CreateInterviewBookingRequest(slotId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InterviewBookingResponse>())!;
    }

    private static async Task AssertDeniedAsync(HttpClient client, string path, Guid slotId, Guid bookingId, HttpStatusCode status)
    {
        Assert.Equal(status, (await client.GetAsync($"{path}/slots")).StatusCode);
        Assert.Equal(status, (await client.GetAsync($"{path}/bookings")).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync($"{path}/bookings", new CreateInterviewBookingRequest(slotId))).StatusCode);
        Assert.Equal(status, (await client.DeleteAsync($"{path}/bookings/{bookingId}")).StatusCode);
    }

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role = UserRoles.Candidate)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Valid-password-123",
            ConfirmPassword = "Valid-password-123", FirstName = "Test", LastName = "User", Role = role
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth.User;
    }
}
