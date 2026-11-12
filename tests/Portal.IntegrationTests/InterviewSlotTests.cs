using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Portal.Contracts.Auth;
using Portal.Contracts.Companies;
using Portal.Contracts.Recruitment;

namespace Portal.IntegrationTests;

public sealed class InterviewSlotTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2027-01-15T10:00:00Z");

    [Theory]
    [InlineData(CompanyMemberRoles.Owner)]
    [InlineData(CompanyMemberRoles.Admin)]
    [InlineData(CompanyMemberRoles.Recruiter)]
    public async Task MembersCanManageSlotsAndDeactivateWithoutDeletingHistory(string role)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client, role);
        Assert.Empty((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!);
        var request = Request(context.MemberId) with
        {
            StartsAt = Start.ToOffset(TimeSpan.FromHours(2)),
            EndsAt = Start.AddHours(1).ToOffset(TimeSpan.FromHours(2)),
            Location = "Praha", MeetingUrl = "https://meet.example.com/interview"
        };
        var response = await client.PostAsJsonAsync(context.Path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(context.Path, response.Headers.Location?.AbsolutePath);
        var slot = (await response.Content.ReadFromJsonAsync<InterviewSlotResponse>())!;
        Assert.NotEqual(Guid.Empty, slot.Id);
        Assert.Equal(context.CompanyId, slot.CompanyId);
        Assert.Equal(context.JobId, slot.JobOfferId);
        Assert.Equal(context.StageId, slot.StageId);
        Assert.Equal("interview", slot.StageKind);
        Assert.Equal(context.MemberId, slot.InterviewerMemberId);
        Assert.Equal(Start, slot.StartsAt);
        Assert.Equal(TimeSpan.Zero, slot.StartsAt.Offset);
        Assert.Equal(Start.AddHours(1), slot.EndsAt);
        Assert.Equal(request.Location, slot.Location);
        Assert.Equal(request.MeetingUrl, slot.MeetingUrl);
        Assert.True(slot.IsActive);
        Assert.False(slot.IsBooked);
        Assert.NotEqual(default, slot.CreatedAt);
        Assert.Equal(slot.CreatedAt, slot.UpdatedAt);
        Assert.Equal(slot, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(response.Headers.Location))!));

        request = Request(context.MemberId) with { StartsAt = Start.AddHours(2), EndsAt = Start.AddHours(3) };
        response = await client.PutAsJsonAsync($"{context.Path}/{slot.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<InterviewSlotResponse>())!;
        Assert.Equal(slot with
        {
            StartsAt = request.StartsAt, EndsAt = request.EndsAt, Location = null, MeetingUrl = null, UpdatedAt = updated.UpdatedAt
        }, updated);
        response = await client.PutAsJsonAsync($"{context.Path}/{slot.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(updated, await response.Content.ReadFromJsonAsync<InterviewSlotResponse>());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{context.Path}/{slot.Id}")).StatusCode);
        var inactive = Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!);
        Assert.Equal(updated with { IsActive = false, UpdatedAt = inactive.UpdatedAt }, inactive);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{context.Path}/{slot.Id}")).StatusCode);
        Assert.Equal(inactive, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!));
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.interview_slots WHERE id = $1", slot.Id));
        response = await client.PutAsJsonAsync($"{context.Path}/{slot.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<InterviewSlotResponse>())!.IsActive);
    }

    [Theory]
    [InlineData(-30, 30)]
    [InlineData(0, 60)]
    [InlineData(15, 45)]
    [InlineData(-30, 90)]
    [InlineData(30, 90)]
    public async Task OverlappingIntervalsAreRejectedWithoutChangingData(int startMinutes, int endMinutes)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var original = await CreateAsync(client, context.Path, Request(context.MemberId));
        var request = Request(context.MemberId) with
        {
            StartsAt = Start.AddMinutes(startMinutes), EndsAt = Start.AddMinutes(endMinutes)
        };
        var response = await client.PostAsJsonAsync(context.Path, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(original, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!));
    }

    [Fact]
    public async Task AdjacentSlotsOtherInterviewersAndInactiveSlotsAreAllowed()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var first = await CreateAsync(client, context.Path, Request(context.MemberId));
        await CreateAsync(client, context.Path, Request(context.MemberId) with { StartsAt = Start.AddHours(1), EndsAt = Start.AddHours(2) });
        await CreateAsync(client, context.Path, Request(context.MemberId) with { StartsAt = Start.AddHours(-1), EndsAt = Start });
        var inactive = await CreateAsync(client, context.Path, Request(context.MemberId) with { IsActive = false });
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync($"{context.Path}/{inactive.Id}", Request(context.MemberId))).StatusCode);
        var otherMember = await AddInterviewerAsync(context.CompanyId);
        var other = await CreateAsync(client, context.Path, Request(otherMember));
        Assert.Equal(otherMember, other.InterviewerMemberId);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync($"{context.Path}/{other.Id}", Request(context.MemberId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync($"{context.Path}/{first.Id}", Request(context.MemberId) with { EndsAt = Start.AddHours(2) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{context.Path}/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"{context.Path}/{inactive.Id}", Request(context.MemberId))).StatusCode);
        var slots = (await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!;
        Assert.Equal(slots.OrderBy(slot => slot.StartsAt).ThenBy(slot => slot.Id), slots);
        Assert.Equal(5, slots.Length);
    }

    [Fact]
    public async Task SameAccountCannotOverlapAcrossOffersAndCompanies()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        await CreateAsync(client, first.Path, Request(first.MemberId));
        var anotherOffer = await CreateOfferAsync(first.CompanyId, first.MemberId);
        var path = $"/api/companies/{first.CompanyId}/jobs/{anotherOffer.JobId}/stages/{anotherOffer.StageId}/slots";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, Request(first.MemberId))).StatusCode);
        var second = await CreateScenarioAsync(client, userId: first.UserId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(second.Path, Request(second.MemberId))).StatusCode);
        var available = await CreateAsync(client, second.Path,
            Request(second.MemberId) with { StartsAt = Start.AddHours(2), EndsAt = Start.AddHours(3) });
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync($"{second.Path}/{available.Id}", Request(second.MemberId))).StatusCode);
        Assert.Equal(available, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(second.Path))!));
    }

    [Theory]
    [InlineData("confirmed", true)]
    [InlineData("completed", true)]
    [InlineData("no_show", true)]
    [InlineData("cancelled", false)]
    public async Task ExistingBookingsProtectTheScheduleAndKeepTheirHistory(string bookingStatus, bool booked)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var slot = await CreateAsync(client, context.Path, Request(context.MemberId));
        var bookingId = await CreateBookingAsync(context, slot.Id, bookingStatus);
        var current = Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!);
        Assert.Equal(slot with { IsBooked = booked }, current);
        var request = Request(context.MemberId) with { StartsAt = Start.AddHours(2), EndsAt = Start.AddHours(3) };
        Assert.Equal(booked ? HttpStatusCode.Conflict : HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"{context.Path}/{slot.Id}", request)).StatusCode);
        Assert.Equal(booked ? HttpStatusCode.Conflict : HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{context.Path}/{slot.Id}")).StatusCode);
        if (booked)
            Assert.Equal(current, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!));
        Assert.Equal(bookingStatus, await fixture.QueryAsync("SELECT status FROM portal.interview_bookings WHERE id = $1", bookingId));
    }

    [Fact]
    public async Task InvalidDatesIdsAndUrlsAreRejected()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var slot = await CreateAsync(client, context.Path, Request(context.MemberId));
        UpsertInterviewSlotRequest[] requests =
        [
            Request(Guid.Empty), Request(context.MemberId) with { StartsAt = default },
            Request(context.MemberId) with { EndsAt = default }, Request(context.MemberId) with { EndsAt = Start },
            Request(context.MemberId) with { EndsAt = Start.AddMinutes(-1) },
            Request(context.MemberId) with { EndsAt = Start.AddTicks(1) },
            Request(context.MemberId) with { MeetingUrl = "javascript:alert(1)" },
            Request(context.MemberId) with { MeetingUrl = "relative/path" },
            Request(context.MemberId) with { MeetingUrl = "ftp://example.com/meeting" }
        ];
        foreach (var request in requests)
        {
            var response = await client.PostAsJsonAsync(context.Path, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{context.Path}/{slot.Id}", request)).StatusCode);
        }
        foreach (var body in new object[]
        {
            new { interviewerMemberId = context.MemberId, endsAt = Start.AddHours(1) },
            new { interviewerMemberId = context.MemberId, startsAt = "invalid", endsAt = Start.AddHours(1) }
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(context.Path, body)).StatusCode);
        Assert.Equal(slot, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!));
    }

    [Theory]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = (SELECT user_id FROM portal.company_members WHERE id = $1)")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = (SELECT user_id FROM portal.company_members WHERE id = $1)")]
    public async Task InvalidInterviewersCannotReceiveNewOrUpdatedSlots(string sql)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var memberId = await AddInterviewerAsync(context.CompanyId);
        var slot = await CreateAsync(client, context.Path, Request(memberId));
        await fixture.QueryAsync(sql, memberId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(context.Path, Request(memberId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"{context.Path}/{slot.Id}", Request(memberId))).StatusCode);
        Assert.Equal(slot, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(context.Path))!));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{context.Path}/{slot.Id}")).StatusCode);
    }

    [Fact]
    public async Task PathsAndInterviewersAreScopedToTheCompanyOfferAndInterviewStage()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var slot = await CreateAsync(client, first.Path, Request(first.MemberId));
        var second = await CreateScenarioAsync(client, userId: first.UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(first.Path, Request(second.MemberId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(first.Path, Request(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{second.Path}/{slot.Id}", Request(second.MemberId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{second.Path}/{slot.Id}")).StatusCode);
        var otherOffer = await CreateOfferAsync(first.CompanyId, first.MemberId);
        foreach (var path in new[]
        {
            $"/api/companies/{second.CompanyId}/jobs/{first.JobId}/stages/{first.StageId}/slots",
            $"/api/companies/{first.CompanyId}/jobs/{otherOffer.JobId}/stages/{first.StageId}/slots",
            $"/api/companies/{first.CompanyId}/jobs/{Guid.NewGuid()}/stages/{first.StageId}/slots",
            $"/api/companies/{first.CompanyId}/jobs/{first.JobId}/stages/{Guid.NewGuid()}/slots"
        })
            await AssertDeniedAsync(client, path, slot.Id, first.MemberId, HttpStatusCode.NotFound);
        using var outsider = fixture.CreateClient();
        await RegisterAsync(outsider);
        await AssertDeniedAsync(outsider, first.Path, slot.Id, first.MemberId, HttpStatusCode.NotFound);
        await fixture.QueryAsync("UPDATE portal.recruitment_stages SET kind = 'screening' WHERE id = $1", otherOffer.StageId);
        var notInterview = $"/api/companies/{first.CompanyId}/jobs/{otherOffer.JobId}/stages/{otherOffer.StageId}/slots";
        await AssertDeniedAsync(client, notInterview, Guid.NewGuid(), first.MemberId, HttpStatusCode.NotFound);
        Assert.Equal(slot, Assert.Single((await client.GetFromJsonAsync<InterviewSlotResponse[]>(first.Path))!));
    }

    [Theory]
    [InlineData("UPDATE portal.company_members SET is_active = false WHERE user_id = $1")]
    [InlineData("UPDATE portal.users SET is_active = false WHERE id = $1")]
    [InlineData("UPDATE portal.users SET role_id = (SELECT id FROM portal.roles WHERE name = 'candidate') WHERE id = $1")]
    public async Task RevokedAccessIsRejectedWithExistingJwt(string sql)
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var slot = await CreateAsync(client, context.Path, Request(context.MemberId));
        await fixture.QueryAsync(sql, context.UserId);
        await AssertDeniedAsync(client, context.Path, slot.Id, context.MemberId, HttpStatusCode.NotFound);
        Assert.Equal(true, await fixture.QueryAsync("SELECT is_active FROM portal.interview_slots WHERE id = $1", slot.Id));
        Assert.Equal(1L, await fixture.QueryAsync("SELECT count(*) FROM portal.interview_slots WHERE job_offer_id = $1", context.JobId));
    }

    [Fact]
    public async Task AnonymousAndCandidateRequestsAreRejected()
    {
        using var owner = fixture.CreateClient();
        var context = await CreateScenarioAsync(owner);
        var slot = await CreateAsync(owner, context.Path, Request(context.MemberId));
        using var client = fixture.CreateClient();
        await AssertDeniedAsync(client, context.Path, slot.Id, context.MemberId, HttpStatusCode.Unauthorized);
        await RegisterAsync(client, UserRoles.Candidate);
        await AssertDeniedAsync(client, context.Path, slot.Id, context.MemberId, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ServerControlsSlotIdentityScopeAndBookingState()
    {
        using var client = fixture.CreateClient();
        var context = await CreateScenarioAsync(client);
        var fakeId = Guid.NewGuid();
        var request = new
        {
            interviewerMemberId = context.MemberId, startsAt = Start, endsAt = Start.AddHours(1),
            id = fakeId, companyId = Guid.NewGuid(), jobOfferId = Guid.NewGuid(), stageId = Guid.NewGuid(),
            stageKind = "test", isBooked = true, createdAt = "2000-01-01T00:00:00Z"
        };
        var response = await client.PostAsJsonAsync(context.Path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var slot = (await response.Content.ReadFromJsonAsync<InterviewSlotResponse>())!;
        Assert.NotEqual(fakeId, slot.Id);
        Assert.Equal(context.CompanyId, slot.CompanyId);
        Assert.Equal(context.JobId, slot.JobOfferId);
        Assert.Equal(context.StageId, slot.StageId);
        Assert.Equal("interview", slot.StageKind);
        Assert.False(slot.IsBooked);
        Assert.True(slot.IsActive);
        Assert.NotEqual(DateTimeOffset.Parse(request.createdAt), slot.CreatedAt);
    }

    [Fact]
    public async Task ConcurrentOverlappingCreatesAcrossCompaniesHaveOneWinner()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var second = await CreateScenarioAsync(client, userId: first.UserId);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(first.Path, Request(first.MemberId)),
            client.PostAsJsonAsync(second.Path, Request(second.MemberId)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1L, await fixture.QueryAsync("""
            SELECT count(*) FROM portal.interview_slots s JOIN portal.company_members m ON m.id = s.interviewer_member_id
            WHERE m.user_id = $1
            """, first.UserId));
    }

    [Fact]
    public async Task ConcurrentOverlappingUpdatesAcrossCompaniesHaveOneWinner()
    {
        using var client = fixture.CreateClient();
        var first = await CreateScenarioAsync(client);
        var second = await CreateScenarioAsync(client, userId: first.UserId);
        var one = await CreateAsync(client, first.Path, Request(first.MemberId));
        var two = await CreateAsync(client, second.Path, Request(second.MemberId) with { StartsAt = Start.AddHours(2), EndsAt = Start.AddHours(3) });
        var request = Request(first.MemberId) with { StartsAt = Start.AddHours(4), EndsAt = Start.AddHours(5) };
        var responses = await Task.WhenAll(client.PutAsJsonAsync($"{first.Path}/{one.Id}", request),
            client.PutAsJsonAsync($"{second.Path}/{two.Id}", request with { InterviewerMemberId = second.MemberId }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    private static UpsertInterviewSlotRequest Request(Guid memberId) => new(memberId, Start, Start.AddHours(1));

    private static async Task AssertDeniedAsync(HttpClient client, string path, Guid slotId, Guid memberId, HttpStatusCode status)
    {
        var request = Request(memberId);
        Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync($"{path}/{slotId}", request)).StatusCode);
        Assert.Equal(status, (await client.DeleteAsync($"{path}/{slotId}")).StatusCode);
    }

    private sealed record TestContext(Guid UserId, Guid CompanyId, Guid MemberId, Guid JobId, Guid StageId)
    {
        public string Path => $"/api/companies/{CompanyId}/jobs/{JobId}/stages/{StageId}/slots";
    }

    private async Task<TestContext> CreateScenarioAsync(HttpClient client, string role = CompanyMemberRoles.Owner, Guid? userId = null)
    {
        var currentUserId = userId ?? (await RegisterAsync(client)).Id;
        var companyId = (Guid)(await fixture.QueryAsync("INSERT INTO portal.companies (name, country_code) VALUES ('Test company', 'CZ') RETURNING id"))!;
        var memberId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, $3) RETURNING id
            """, companyId, currentUserId, role))!;
        var (jobId, stageId) = await CreateOfferAsync(companyId, memberId);
        return new(currentUserId, companyId, memberId, jobId, stageId);
    }

    private async Task<(Guid JobId, Guid StageId)> CreateOfferAsync(Guid companyId, Guid memberId)
    {
        var jobId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.job_offers (company_id, created_by_member_id, title, description, work_mode, employment_type)
            VALUES ($1, $2, 'Developer', 'Description', 'remote', 'full_time') RETURNING id
            """, companyId, memberId))!;
        var stageId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.recruitment_stages (job_offer_id, name, kind, position) VALUES ($1, 'Interview', 'interview', 1) RETURNING id
            """, jobId))!;
        return (jobId, stageId);
    }

    private async Task<Guid> AddInterviewerAsync(Guid companyId)
    {
        using var client = fixture.CreateClient();
        var user = await RegisterAsync(client);
        return (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.company_members (company_id, user_id, role) VALUES ($1, $2, 'recruiter') RETURNING id
            """, companyId, user.Id))!;
    }

    private async Task<Guid> CreateBookingAsync(TestContext context, Guid slotId, string status)
    {
        using var candidate = fixture.CreateClient();
        var user = await RegisterAsync(candidate, UserRoles.Candidate);
        var applicationId = (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.applications (job_offer_id, company_id, candidate_id, profile_snapshot)
            VALUES ($1, $2, $3, '{}') RETURNING id
            """, context.JobId, context.CompanyId, user.Id))!;
        await fixture.QueryAsync("""
            INSERT INTO portal.application_stages (application_id, stage_id, job_offer_id, company_id) VALUES ($1, $2, $3, $4)
            """, applicationId, context.StageId, context.JobId, context.CompanyId);
        return (Guid)(await fixture.QueryAsync("""
            INSERT INTO portal.interview_bookings (slot_id, application_id, stage_id, job_offer_id, company_id, status, cancelled_at)
            VALUES ($1, $2, $3, $4, $5, $6, CASE WHEN $6 = 'cancelled' THEN now() ELSE NULL END) RETURNING id
            """, slotId, applicationId, context.StageId, context.JobId, context.CompanyId, status))!;
    }

    private static async Task<InterviewSlotResponse> CreateAsync(HttpClient client, string path, UpsertInterviewSlotRequest request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InterviewSlotResponse>())!;
    }

    private static async Task<UserResponse> RegisterAsync(HttpClient client, string role = UserRoles.Company)
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
