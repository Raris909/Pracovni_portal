using System.Text.Json;
using Npgsql;
using Portal.Contracts.Applications;

namespace Portal.Api.Applications;

public sealed class CandidateApplicationRepository : ICandidateApplicationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public CandidateApplicationRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<CandidateApplicationSummaryResponse>> GetCandidateApplicationsAsync(
        Guid candidateId,
        string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                a.id,
                a.job_offer_id,
                j.title AS job_title,
                a.company_id,
                c.name AS company_name,
                j.location,
                j.work_mode,
                j.employment_type,
                a.status,
                cs.name AS current_stage_name,
                cs.kind AS current_stage_kind,
                cs.status AS current_stage_status,
                a.submitted_at,
                a.updated_at
            FROM portal.applications a
            JOIN portal.job_offers j ON j.id = a.job_offer_id AND j.company_id = a.company_id
            JOIN portal.companies c ON c.id = a.company_id
            LEFT JOIN LATERAL (
                SELECT
                    rs.name,
                    rs.kind,
                    ast.status
                FROM portal.application_stages ast
                JOIN portal.recruitment_stages rs
                  ON rs.id = ast.stage_id AND rs.job_offer_id = ast.job_offer_id
                WHERE ast.application_id = a.id
                ORDER BY
                    CASE ast.status
                        WHEN 'in_progress' THEN 1
                        WHEN 'pending' THEN 2
                        ELSE 3
                    END,
                    rs.position ASC
                LIMIT 1
            ) cs ON true
            WHERE a.candidate_id = @CandidateId
              AND (@StatusFilter IS NULL OR a.status = @StatusFilter)
            ORDER BY a.submitted_at DESC;
            """;

        var results = new List<CandidateApplicationSummaryResponse>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);
        cmd.Parameters.AddWithValue("StatusFilter", (object?)statusFilter ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CandidateApplicationSummaryResponse(
                Id: reader.GetGuid(0),
                JobOfferId: reader.GetGuid(1),
                JobTitle: reader.GetString(2),
                CompanyId: reader.GetGuid(3),
                CompanyName: reader.GetString(4),
                Location: reader.IsDBNull(5) ? null : reader.GetString(5),
                WorkMode: reader.GetString(6),
                EmploymentType: reader.GetString(7),
                Status: reader.GetString(8),
                CurrentStageName: reader.IsDBNull(9) ? null : reader.GetString(9),
                CurrentStageKind: reader.IsDBNull(10) ? null : reader.GetString(10),
                CurrentStageStatus: reader.IsDBNull(11) ? null : reader.GetString(11),
                SubmittedAt: reader.GetFieldValue<DateTimeOffset>(12),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(13)));
        }

        return results;
    }

    public async Task<CandidateApplicationDetailResponse?> GetCandidateApplicationByIdAsync(
        Guid candidateId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        const string mainSql = """
            SELECT
                a.id,
                a.job_offer_id,
                j.title AS job_title,
                a.company_id,
                c.name AS company_name,
                j.location,
                j.work_mode,
                j.employment_type,
                a.status,
                a.cover_message,
                a.profile_snapshot::text,
                conv.id AS conversation_id,
                a.submitted_at,
                a.updated_at
            FROM portal.applications a
            JOIN portal.job_offers j ON j.id = a.job_offer_id AND j.company_id = a.company_id
            JOIN portal.companies c ON c.id = a.company_id
            LEFT JOIN portal.conversations conv ON conv.application_id = a.id
            WHERE a.id = @ApplicationId AND a.candidate_id = @CandidateId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        Guid id, jobOfferId, companyId;
        string jobTitle, companyName, workMode, employmentType, status, snapshotJson;
        string? location, coverMessage;
        Guid? conversationId;
        DateTimeOffset submittedAt, updatedAt;

        await using (var cmd = new NpgsqlCommand(mainSql, connection))
        {
            cmd.Parameters.AddWithValue("ApplicationId", applicationId);
            cmd.Parameters.AddWithValue("CandidateId", candidateId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetGuid(0);
            jobOfferId = reader.GetGuid(1);
            jobTitle = reader.GetString(2);
            companyId = reader.GetGuid(3);
            companyName = reader.GetString(4);
            location = reader.IsDBNull(5) ? null : reader.GetString(5);
            workMode = reader.GetString(6);
            employmentType = reader.GetString(7);
            status = reader.GetString(8);
            coverMessage = reader.IsDBNull(9) ? null : reader.GetString(9);
            snapshotJson = reader.GetString(10);
            conversationId = reader.IsDBNull(11) ? null : reader.GetGuid(11);
            submittedAt = reader.GetFieldValue<DateTimeOffset>(12);
            updatedAt = reader.GetFieldValue<DateTimeOffset>(13);
        }

        var profileSnapshot = JsonSerializer.Deserialize<ApplicationProfileSnapshot>(snapshotJson, JsonOptions)
            ?? new ApplicationProfileSnapshot(
                FirstName: string.Empty,
                LastName: string.Empty,
                Email: string.Empty,
                Phone: null,
                Headline: null,
                Summary: null,
                City: null,
                CountryCode: null,
                Skills: [],
                Experiences: [],
                Education: []);

        // 2. Load attached documents
        const string docsSql = """
            SELECT
                d.id,
                d.kind,
                d.original_filename,
                d.mime_type,
                d.size_bytes,
                ad.attached_at
            FROM portal.application_documents ad
            JOIN portal.candidate_documents d
              ON d.id = ad.document_id AND d.candidate_id = ad.candidate_id
            WHERE ad.application_id = @ApplicationId AND ad.candidate_id = @CandidateId
            ORDER BY ad.attached_at ASC;
            """;

        var documents = new List<ApplicationAttachedDocumentResponse>();
        await using (var docsCmd = new NpgsqlCommand(docsSql, connection))
        {
            docsCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            docsCmd.Parameters.AddWithValue("CandidateId", candidateId);

            await using var reader = await docsCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                documents.Add(new ApplicationAttachedDocumentResponse(
                    DocumentId: reader.GetGuid(0),
                    Kind: reader.GetString(1),
                    OriginalFilename: reader.GetString(2),
                    MimeType: reader.GetString(3),
                    SizeBytes: reader.GetInt64(4),
                    AttachedAt: reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        // 3. Load recruitment stages and the candidate's stage statuses
        const string stagesSql = """
            SELECT
                rs.id,
                rs.name,
                rs.kind,
                rs.position,
                rs.description,
                COALESCE(ast.status, 'pending') AS status,
                ast.started_at,
                ast.completed_at,
                ast.updated_at
            FROM portal.recruitment_stages rs
            LEFT JOIN portal.application_stages ast
              ON ast.stage_id = rs.id AND ast.application_id = @ApplicationId
            WHERE rs.job_offer_id = @JobOfferId
            ORDER BY rs.position ASC;
            """;

        var stages = new List<CandidateApplicationStageProgressResponse>();
        await using (var stagesCmd = new NpgsqlCommand(stagesSql, connection))
        {
            stagesCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            stagesCmd.Parameters.AddWithValue("JobOfferId", jobOfferId);

            await using var reader = await stagesCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                stages.Add(new CandidateApplicationStageProgressResponse(
                    StageId: reader.GetGuid(0),
                    Name: reader.GetString(1),
                    Kind: reader.GetString(2),
                    Position: reader.GetInt32(3),
                    Description: reader.IsDBNull(4) ? null : reader.GetString(4),
                    Status: reader.GetString(5),
                    StartedAt: reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                    CompletedAt: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    UpdatedAt: reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8)));
            }
        }

        return new CandidateApplicationDetailResponse(
            Id: id,
            JobOfferId: jobOfferId,
            JobTitle: jobTitle,
            CompanyId: companyId,
            CompanyName: companyName,
            Location: location,
            WorkMode: workMode,
            EmploymentType: employmentType,
            Status: status,
            CoverMessage: coverMessage,
            ProfileSnapshot: profileSnapshot,
            AttachedDocuments: documents,
            Stages: stages,
            ConversationId: conversationId,
            SubmittedAt: submittedAt,
            UpdatedAt: updatedAt);
    }
}