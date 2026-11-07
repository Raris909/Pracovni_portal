using System.Text.Json;
using Npgsql;
using Portal.Contracts.Applications;

namespace Portal.Api.Applications;

public sealed class CompanyApplicationRepository : ICompanyApplicationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public CompanyApplicationRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<CompanyAccessStatus> CheckCompanyAccessAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                EXISTS (SELECT 1 FROM portal.companies WHERE id = @CompanyId) AS company_exists,
                EXISTS (
                    SELECT 1 FROM portal.company_members
                    WHERE company_id = @CompanyId
                      AND user_id = @UserId
                      AND is_active = true
                ) AS is_active_member;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CompanyId", companyId);
        cmd.Parameters.AddWithValue("UserId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var companyExists = reader.GetBoolean(0);
        if (!companyExists)
        {
            return CompanyAccessStatus.CompanyNotFound;
        }

        var isActiveMember = reader.GetBoolean(1);
        return isActiveMember
            ? CompanyAccessStatus.Authorized
            : CompanyAccessStatus.Forbidden;
    }

    public async Task<IReadOnlyList<CompanyApplicationSummaryResponse>> GetCompanyApplicationsAsync(
        Guid companyId,
        Guid? jobOfferId = null,
        string? status = null,
        Guid? assignedMemberId = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                a.id,
                a.job_offer_id,
                j.title AS job_title,
                a.company_id,
                a.candidate_id,
                COALESCE(a.profile_snapshot->>'firstName', a.profile_snapshot->>'FirstName', u.first_name) AS first_name,
                COALESCE(a.profile_snapshot->>'lastName', a.profile_snapshot->>'LastName', u.last_name) AS last_name,
                COALESCE(a.profile_snapshot->>'email', a.profile_snapshot->>'Email', u.email) AS email,
                a.assigned_member_id,
                a.status,
                (SELECT COUNT(*)::int FROM portal.application_documents ad WHERE ad.application_id = a.id) AS docs_count,
                a.submitted_at,
                a.updated_at
            FROM portal.applications a
            JOIN portal.job_offers j ON j.id = a.job_offer_id AND j.company_id = a.company_id
            JOIN portal.users u ON u.id = a.candidate_id
            WHERE a.company_id = @CompanyId
              AND (@JobOfferId IS NULL OR a.job_offer_id = @JobOfferId)
              AND (@Status IS NULL OR a.status = @Status)
              AND (@AssignedMemberId IS NULL OR a.assigned_member_id = @AssignedMemberId)
            ORDER BY a.submitted_at DESC;
            """;

        var list = new List<CompanyApplicationSummaryResponse>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CompanyId", companyId);
        cmd.Parameters.AddWithValue("JobOfferId", (object?)jobOfferId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("AssignedMemberId", (object?)assignedMemberId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new CompanyApplicationSummaryResponse(
                Id: reader.GetGuid(0),
                JobOfferId: reader.GetGuid(1),
                JobTitle: reader.GetString(2),
                CompanyId: reader.GetGuid(3),
                CandidateId: reader.GetGuid(4),
                CandidateFirstName: reader.GetString(5),
                CandidateLastName: reader.GetString(6),
                CandidateEmail: reader.GetString(7),
                AssignedMemberId: reader.IsDBNull(8) ? null : reader.GetGuid(8),
                Status: reader.GetString(9),
                AttachedDocumentsCount: reader.GetInt32(10),
                SubmittedAt: reader.GetFieldValue<DateTimeOffset>(11),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(12)));
        }

        return list;
    }

    public async Task<CompanyApplicationDetailResponse?> GetCompanyApplicationByIdAsync(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await LoadDetailInternalAsync(connection, companyId, applicationId, cancellationToken);
    }

    public async Task<CompanyApplicationDetailResponse?> UpdateApplicationStatusAsync(
        Guid companyId,
        Guid applicationId,
        string status,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE portal.applications
            SET status = @Status
            WHERE id = @ApplicationId AND company_id = @CompanyId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using (var cmd = new NpgsqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("Status", status);
            cmd.Parameters.AddWithValue("ApplicationId", applicationId);
            cmd.Parameters.AddWithValue("CompanyId", companyId);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (rows == 0)
            {
                return null;
            }
        }

        return await LoadDetailInternalAsync(connection, companyId, applicationId, cancellationToken);
    }

    public async Task<(bool FoundApplication, bool ValidMember, CompanyApplicationDetailResponse? Updated)> AssignMemberAsync(
        Guid companyId,
        Guid applicationId,
        Guid? assignedMemberId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string checkAppSql = """
            SELECT 1 FROM portal.applications
            WHERE id = @ApplicationId AND company_id = @CompanyId;
            """;

        await using (var checkCmd = new NpgsqlCommand(checkAppSql, connection))
        {
            checkCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            checkCmd.Parameters.AddWithValue("CompanyId", companyId);
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (exists is null)
            {
                return (false, false, null);
            }
        }

        if (assignedMemberId.HasValue)
        {
            const string checkMemberSql = """
                SELECT 1 FROM portal.company_members
                WHERE id = @MemberId AND company_id = @CompanyId AND is_active = true;
                """;

            await using var memberCmd = new NpgsqlCommand(checkMemberSql, connection);
            memberCmd.Parameters.AddWithValue("MemberId", assignedMemberId.Value);
            memberCmd.Parameters.AddWithValue("CompanyId", companyId);
            var memberExists = await memberCmd.ExecuteScalarAsync(cancellationToken);
            if (memberExists is null)
            {
                return (true, false, null);
            }
        }

        const string updateSql = """
            UPDATE portal.applications
            SET assigned_member_id = @AssignedMemberId
            WHERE id = @ApplicationId AND company_id = @CompanyId;
            """;

        await using (var updateCmd = new NpgsqlCommand(updateSql, connection))
        {
            updateCmd.Parameters.AddWithValue("AssignedMemberId", (object?)assignedMemberId ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            updateCmd.Parameters.AddWithValue("CompanyId", companyId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var detail = await LoadDetailInternalAsync(connection, companyId, applicationId, cancellationToken);
        return (true, true, detail);
    }

    public async Task<IReadOnlyList<ApplicationAttachedDocumentResponse>?> GetApplicationDocumentsAsync(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string existsSql = """
            SELECT 1 FROM portal.applications
            WHERE id = @ApplicationId AND company_id = @CompanyId;
            """;

        await using (var checkCmd = new NpgsqlCommand(existsSql, connection))
        {
            checkCmd.Parameters.AddWithValue("ApplicationId", applicationId);
            checkCmd.Parameters.AddWithValue("CompanyId", companyId);
            if (await checkCmd.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }
        }

        return await LoadAttachedDocumentsInternalAsync(connection, companyId, applicationId, cancellationToken);
    }

    public async Task<CompanyApplicationDocumentFileRecord?> GetApplicationDocumentFileAsync(
        Guid companyId,
        Guid applicationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                ad.application_id,
                d.id,
                d.candidate_id,
                d.kind,
                d.original_filename,
                d.storage_key,
                d.mime_type,
                d.size_bytes,
                d.sha256,
                ad.attached_at
            FROM portal.applications a
            JOIN portal.application_documents ad
              ON ad.application_id = a.id AND ad.candidate_id = a.candidate_id
            JOIN portal.candidate_documents d
              ON d.id = ad.document_id AND d.candidate_id = ad.candidate_id
            WHERE a.company_id = @CompanyId
              AND a.id = @ApplicationId
              AND d.id = @DocumentId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CompanyId", companyId);
        cmd.Parameters.AddWithValue("ApplicationId", applicationId);
        cmd.Parameters.AddWithValue("DocumentId", documentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CompanyApplicationDocumentFileRecord(
            ApplicationId: reader.GetGuid(0),
            DocumentId: reader.GetGuid(1),
            CandidateId: reader.GetGuid(2),
            Kind: reader.GetString(3),
            OriginalFilename: reader.GetString(4),
            StorageKey: reader.GetString(5),
            MimeType: reader.GetString(6),
            SizeBytes: reader.GetInt64(7),
            Sha256: reader.GetString(8),
            AttachedAt: reader.GetFieldValue<DateTimeOffset>(9));
    }

    private static async Task<CompanyApplicationDetailResponse?> LoadDetailInternalAsync(
        NpgsqlConnection connection,
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        const string mainSql = """
            SELECT
                a.id,
                a.job_offer_id,
                j.title AS job_title,
                a.company_id,
                a.candidate_id,
                a.assigned_member_id,
                a.status,
                a.cover_message,
                a.profile_snapshot::text,
                conv.id AS conversation_id,
                a.submitted_at,
                a.updated_at
            FROM portal.applications a
            JOIN portal.job_offers j ON j.id = a.job_offer_id AND j.company_id = a.company_id
            LEFT JOIN portal.conversations conv ON conv.application_id = a.id
            WHERE a.id = @ApplicationId AND a.company_id = @CompanyId;
            """;

        Guid id, jobOfferId, candidateId;
        Guid? assignedMemberId, conversationId;
        string jobTitle, status, snapshotJson;
        string? coverMessage;
        DateTimeOffset submittedAt, updatedAt;

        await using (var cmd = new NpgsqlCommand(mainSql, connection))
        {
            cmd.Parameters.AddWithValue("ApplicationId", applicationId);
            cmd.Parameters.AddWithValue("CompanyId", companyId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetGuid(0);
            jobOfferId = reader.GetGuid(1);
            jobTitle = reader.GetString(2);
            candidateId = reader.GetGuid(4);
            assignedMemberId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
            status = reader.GetString(6);
            coverMessage = reader.IsDBNull(7) ? null : reader.GetString(7);
            snapshotJson = reader.GetString(8);
            conversationId = reader.IsDBNull(9) ? null : reader.GetGuid(9);
            submittedAt = reader.GetFieldValue<DateTimeOffset>(10);
            updatedAt = reader.GetFieldValue<DateTimeOffset>(11);
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

        var documents = await LoadAttachedDocumentsInternalAsync(connection, companyId, applicationId, cancellationToken);

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

        return new CompanyApplicationDetailResponse(
            Id: id,
            JobOfferId: jobOfferId,
            JobTitle: jobTitle,
            CompanyId: companyId,
            CandidateId: candidateId,
            AssignedMemberId: assignedMemberId,
            Status: status,
            CoverMessage: coverMessage,
            ProfileSnapshot: profileSnapshot,
            AttachedDocuments: documents,
            Stages: stages,
            ConversationId: conversationId,
            SubmittedAt: submittedAt,
            UpdatedAt: updatedAt);
    }

    private static async Task<IReadOnlyList<ApplicationAttachedDocumentResponse>> LoadAttachedDocumentsInternalAsync(
        NpgsqlConnection connection,
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        const string docsSql = """
            SELECT
                d.id,
                d.kind,
                d.original_filename,
                d.mime_type,
                d.size_bytes,
                ad.attached_at
            FROM portal.applications a
            JOIN portal.application_documents ad
              ON ad.application_id = a.id AND ad.candidate_id = a.candidate_id
            JOIN portal.candidate_documents d
              ON d.id = ad.document_id AND d.candidate_id = ad.candidate_id
            WHERE a.id = @ApplicationId AND a.company_id = @CompanyId
            ORDER BY ad.attached_at ASC;
            """;

        var documents = new List<ApplicationAttachedDocumentResponse>();
        await using var docsCmd = new NpgsqlCommand(docsSql, connection);
        docsCmd.Parameters.AddWithValue("ApplicationId", applicationId);
        docsCmd.Parameters.AddWithValue("CompanyId", companyId);

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

        return documents;
    }
}