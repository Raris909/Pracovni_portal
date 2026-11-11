using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Applications;

namespace Portal.Api.Applications;

public sealed class ApplicationSubmissionService : IApplicationSubmissionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public ApplicationSubmissionService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<SubmittedApplicationResult> SubmitApplicationAsync(
        Guid candidateId,
        Guid jobOfferId,
        string? coverMessage,
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 1. Ensure candidate_profiles row exists for this user
        const string ensureProfileSql = """
            INSERT INTO portal.candidate_profiles (user_id)
            SELECT id FROM portal.users WHERE id = @CandidateId AND is_active = true
            ON CONFLICT (user_id) DO NOTHING;
            """;

        await using (var ensureCmd = new NpgsqlCommand(ensureProfileSql, connection, transaction))
        {
            ensureCmd.Parameters.AddWithValue("CandidateId", candidateId);
            await ensureCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Load candidate profile + user info + current_cv_id
        const string profileSql = """
            SELECT
                u.first_name,
                u.last_name,
                u.email,
                u.phone,
                p.headline,
                p.summary,
                p.city,
                p.country_code,
                p.skills,
                p.current_cv_id
            FROM portal.users u
            JOIN portal.candidate_profiles p ON p.user_id = u.id
            WHERE u.id = @CandidateId AND u.is_active = true;
            """;

        string firstName, lastName, email;
        string? phone, headline, summary, city, countryCode;
        string[] skills;
        Guid? currentCvId;

        await using (var profileCmd = new NpgsqlCommand(profileSql, connection, transaction))
        {
            profileCmd.Parameters.AddWithValue("CandidateId", candidateId);
            await using var reader = await profileCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new ApplicationSubmissionException(
                    ApplicationSubmissionErrorKind.CandidateProfileNotFound,
                    "Candidate profile was not found.");
            }

            firstName = reader.GetString(0);
            lastName = reader.GetString(1);
            email = reader.GetString(2);
            phone = reader.IsDBNull(3) ? null : reader.GetString(3);
            headline = reader.IsDBNull(4) ? null : reader.GetString(4);
            summary = reader.IsDBNull(5) ? null : reader.GetString(5);
            city = reader.IsDBNull(6) ? null : reader.GetString(6);
            countryCode = reader.IsDBNull(7) ? null : reader.GetString(7);
            skills = reader.GetFieldValue<string[]>(8);
            currentCvId = reader.IsDBNull(9) ? null : reader.GetGuid(9);
        }

        // 3. Load experiences for profile_snapshot
        const string expSql = """
            SELECT employer, position, description, start_date, end_date
            FROM portal.candidate_experiences
            WHERE candidate_id = @CandidateId
            ORDER BY start_date DESC;
            """;

        var experiences = new List<ApplicationExperienceSnapshot>();
        await using (var expCmd = new NpgsqlCommand(expSql, connection, transaction))
        {
            expCmd.Parameters.AddWithValue("CandidateId", candidateId);
            await using var reader = await expCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                experiences.Add(new ApplicationExperienceSnapshot(
                    Employer: reader.GetString(0),
                    Position: reader.GetString(1),
                    Description: reader.IsDBNull(2) ? null : reader.GetString(2),
                    StartDate: reader.GetFieldValue<DateOnly>(3),
                    EndDate: reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4)));
            }
        }

        // 4. Load education for profile_snapshot
        const string eduSql = """
            SELECT school, field_of_study, degree, start_date, end_date
            FROM portal.candidate_education
            WHERE candidate_id = @CandidateId
            ORDER BY start_date DESC;
            """;

        var education = new List<ApplicationEducationSnapshot>();
        await using (var eduCmd = new NpgsqlCommand(eduSql, connection, transaction))
        {
            eduCmd.Parameters.AddWithValue("CandidateId", candidateId);
            await using var reader = await eduCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                education.Add(new ApplicationEducationSnapshot(
                    School: reader.GetString(0),
                    FieldOfStudy: reader.IsDBNull(1) ? null : reader.GetString(1),
                    Degree: reader.IsDBNull(2) ? null : reader.GetString(2),
                    StartDate: reader.GetFieldValue<DateOnly>(3),
                    EndDate: reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4)));
            }
        }

        var snapshot = new ApplicationProfileSnapshot(
            FirstName: firstName,
            LastName: lastName,
            Email: email,
            Phone: phone,
            Headline: headline,
            Summary: summary,
            City: city,
            CountryCode: countryCode,
            Skills: skills,
            Experiences: experiences,
            Education: education);

        // Determine documents to attach: explicit DocumentIds if provided, otherwise default to current_cv_id if set
        var docsToAttach = new HashSet<Guid>();
        if (documentIds is { Count: > 0 })
        {
            foreach (var docId in documentIds.Where(id => id != Guid.Empty))
            {
                docsToAttach.Add(docId);
            }
        }
        else if (currentCvId.HasValue)
        {
            docsToAttach.Add(currentCvId.Value);
        }

        // 5. Verify job offer exists, is 'published', and deadline has not expired
        const string jobCheckSql = """
            SELECT company_id, status, application_deadline
            FROM portal.job_offers
            WHERE id = @JobOfferId;
            """;

        Guid companyId;
        await using (var jobCmd = new NpgsqlCommand(jobCheckSql, connection, transaction))
        {
            jobCmd.Parameters.AddWithValue("JobOfferId", jobOfferId);
            await using var reader = await jobCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new ApplicationSubmissionException(
                    ApplicationSubmissionErrorKind.JobOfferNotFoundOrNotOpen,
                    "Job offer was not found.");
            }

            companyId = reader.GetGuid(0);
            var status = reader.GetString(1);
            var deadline = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2);

            if (status != "published" || (deadline.HasValue && deadline.Value < DateTimeOffset.UtcNow))
            {
                throw new ApplicationSubmissionException(
                    ApplicationSubmissionErrorKind.JobOfferNotFoundOrNotOpen,
                    "Job offer is not open for applications.");
            }
        }

        // 6. Verify all selected documents belong to this candidate
        if (docsToAttach.Count > 0)
        {
            const string verifyDocsSql = """
                SELECT COUNT(*)
                FROM portal.candidate_documents
                WHERE candidate_id = @CandidateId
                  AND id = ANY(@DocIds);
                """;

            await using var docCheckCmd = new NpgsqlCommand(verifyDocsSql, connection, transaction);
            docCheckCmd.Parameters.AddWithValue("CandidateId", candidateId);
            docCheckCmd.Parameters.Add(new NpgsqlParameter("DocIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = docsToAttach.ToArray()
            });

            var validCount = (long)(await docCheckCmd.ExecuteScalarAsync(cancellationToken))!;
            if (validCount != docsToAttach.Count)
            {
                throw new ApplicationSubmissionException(
                    ApplicationSubmissionErrorKind.InvalidDocumentSelection,
                    "One or more selected documents do not exist or do not belong to the current user.");
            }
        }

        // 7. Insert application into portal.applications
        const string insertAppSql = """
            INSERT INTO portal.applications (
                job_offer_id, company_id, candidate_id, status, cover_message, profile_snapshot
            )
            VALUES (
                @JobOfferId, @CompanyId, @CandidateId, 'submitted', @CoverMessage, @ProfileSnapshot
            )
            RETURNING id, submitted_at, updated_at;
            """;

        Guid applicationId;
        DateTimeOffset submittedAt;
        DateTimeOffset updatedAt;
        var trimmedCover = string.IsNullOrWhiteSpace(coverMessage) ? null : coverMessage.Trim();

        try
        {
            await using var appCmd = new NpgsqlCommand(insertAppSql, connection, transaction);
            appCmd.Parameters.AddWithValue("JobOfferId", jobOfferId);
            appCmd.Parameters.AddWithValue("CompanyId", companyId);
            appCmd.Parameters.AddWithValue("CandidateId", candidateId);
            appCmd.Parameters.AddWithValue("CoverMessage", (object?)trimmedCover ?? DBNull.Value);
            appCmd.Parameters.Add(new NpgsqlParameter("ProfileSnapshot", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(snapshot, JsonOptions)
            });

            await using var reader = await appCmd.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            applicationId = reader.GetGuid(0);
            submittedAt = reader.GetFieldValue<DateTimeOffset>(1);
            updatedAt = reader.GetFieldValue<DateTimeOffset>(2);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApplicationSubmissionException(
                ApplicationSubmissionErrorKind.AlreadyApplied,
                "You have already submitted an application for this job offer.");
        }

        // 8. Attach selected documents in portal.application_documents
        if (docsToAttach.Count > 0)
        {
            const string attachDocsSql = """
                INSERT INTO portal.application_documents (application_id, document_id, candidate_id)
                SELECT @ApplicationId, unnest(@DocIds), @CandidateId;
                """;

            await using var attachCmd = new NpgsqlCommand(attachDocsSql, connection, transaction);
            attachCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            attachCmd.Parameters.AddWithValue("CandidateId", candidateId);
            attachCmd.Parameters.Add(new NpgsqlParameter("DocIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = docsToAttach.ToArray()
            });

            await attachCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 9. Initialize application_stages from recruitment_stages
        const string initStagesSql = """
            INSERT INTO portal.application_stages (application_id, stage_id, job_offer_id, company_id, status)
            SELECT @ApplicationId, rs.id, @JobOfferId, @CompanyId, 'pending'
            FROM portal.recruitment_stages rs
            WHERE rs.job_offer_id = @JobOfferId
            ORDER BY rs.position ASC;
            """;

        await using (var stagesCmd = new NpgsqlCommand(initStagesSql, connection, transaction))
        {
            stagesCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            stagesCmd.Parameters.AddWithValue("JobOfferId", jobOfferId);
            stagesCmd.Parameters.AddWithValue("CompanyId", companyId);
            await stagesCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 10. Create conversation row for chat
        const string createConvSql = """
            INSERT INTO portal.conversations (application_id, company_id, candidate_id)
            VALUES (@ApplicationId, @CompanyId, @CandidateId);
            """;

        await using (var convCmd = new NpgsqlCommand(createConvSql, connection, transaction))
        {
            convCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            convCmd.Parameters.AddWithValue("CompanyId", companyId);
            convCmd.Parameters.AddWithValue("CandidateId", candidateId);
            await convCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new SubmittedApplicationResult(
            Id: applicationId,
            JobOfferId: jobOfferId,
            CompanyId: companyId,
            CandidateId: candidateId,
            Status: ApplicationStatuses.Submitted,
            CoverMessage: trimmedCover,
            ProfileSnapshot: snapshot,
            AttachedDocumentIds: docsToAttach.ToList(),
            SubmittedAt: submittedAt,
            UpdatedAt: updatedAt);
    }
}