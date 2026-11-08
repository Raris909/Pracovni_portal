using System.Text.Json;
using Npgsql;
using Portal.Contracts.CvReviews;

namespace Portal.Api.CvReviews;

public sealed class CvReviewRepository : ICvReviewRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CvReviewRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<CandidateCvReviewResponse> CreateCandidateReviewAsync(Guid candidateId, Guid documentId, string provider, string model, string promptVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            INSERT INTO portal.candidate_cv_reviews (candidate_id, document_id, provider, model, prompt_version)
            VALUES (@CandidateId, @DocumentId, @Provider, @Model, @PromptVersion)
            RETURNING id, document_kind, status, score, result::text, error_message, created_at, completed_at, updated_at;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);
        cmd.Parameters.AddWithValue("DocumentId", documentId);
        cmd.Parameters.AddWithValue("Provider", provider);
        cmd.Parameters.AddWithValue("Model", model);
        cmd.Parameters.AddWithValue("PromptVersion", promptVersion);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new CandidateCvReviewResponse(
            reader.GetGuid(0), candidateId, documentId, reader.GetString(1), reader.GetString(2),
            provider, model, promptVersion, reader.IsDBNull(3) ? null : reader.GetInt16(3),
            reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<CandidateCvReviewResultPayload>(reader.GetString(4), JsonOptions),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8));
    }

    public async Task UpdateCandidateReviewAsync(Guid reviewId, string status, short? score, CandidateCvReviewResultPayload? result, string? errorMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            UPDATE portal.candidate_cv_reviews
            SET status = @Status, score = @Score, result = @Result::jsonb, error_message = @ErrorMessage, 
                completed_at = CASE WHEN @Status IN ('succeeded', 'failed') THEN now() ELSE null END
            WHERE id = @Id;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("Id", reviewId);
        cmd.Parameters.AddWithValue("Status", status);
        cmd.Parameters.AddWithValue("Score", (object?)score ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Result", result is null ? DBNull.Value : JsonSerializer.Serialize(result, JsonOptions));
        cmd.Parameters.AddWithValue("ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CandidateCvReviewResponse?> GetCandidateReviewByIdAsync(Guid reviewId, Guid candidateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = "SELECT id, document_id, document_kind, status, provider, model, prompt_version, score, result::text, error_message, created_at, completed_at, updated_at FROM portal.candidate_cv_reviews WHERE id = @Id AND candidate_id = @CandidateId;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("Id", reviewId);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new CandidateCvReviewResponse(
            reader.GetGuid(0), candidateId, reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt16(7),
            reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<CandidateCvReviewResultPayload>(reader.GetString(8), JsonOptions),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11), reader.GetFieldValue<DateTimeOffset>(12));
    }

    public async Task<IReadOnlyList<CandidateCvReviewSummaryResponse>> GetCandidateReviewsAsync(Guid candidateId, CancellationToken cancellationToken = default)
    {
        var list = new List<CandidateCvReviewSummaryResponse>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = "SELECT id, document_id, status, score, created_at, completed_at FROM portal.candidate_cv_reviews WHERE candidate_id = @CandidateId ORDER BY created_at DESC;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new CandidateCvReviewSummaryResponse(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt16(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return list;
    }

    public async Task<ApplicationCvReviewResponse> CreateApplicationReviewAsync(Guid companyId, Guid applicationId, Guid candidateId, Guid documentId, Guid requestedByMemberId, string provider, string model, string promptVersion, JobRequirementsSnapshot jobRequirementsSnapshot, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            INSERT INTO portal.application_cv_reviews (application_id, company_id, candidate_id, document_id, requested_by_member_id, provider, model, prompt_version, job_requirements_snapshot)
            VALUES (@ApplicationId, @CompanyId, @CandidateId, @DocumentId, @RequestedBy, @Provider, @Model, @PromptVersion, @JobReqs::jsonb)
            RETURNING id, document_kind, status, score, result::text, error_message, created_at, completed_at, updated_at;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ApplicationId", applicationId);
        cmd.Parameters.AddWithValue("CompanyId", companyId);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);
        cmd.Parameters.AddWithValue("DocumentId", documentId);
        cmd.Parameters.AddWithValue("RequestedBy", requestedByMemberId);
        cmd.Parameters.AddWithValue("Provider", provider);
        cmd.Parameters.AddWithValue("Model", model);
        cmd.Parameters.AddWithValue("PromptVersion", promptVersion);
        cmd.Parameters.AddWithValue("JobReqs", JsonSerializer.Serialize(jobRequirementsSnapshot, JsonOptions));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new ApplicationCvReviewResponse(
            reader.GetGuid(0), applicationId, companyId, candidateId, documentId, reader.GetString(1),
            requestedByMemberId, reader.GetString(2), provider, model, promptVersion, jobRequirementsSnapshot,
            reader.IsDBNull(3) ? null : reader.GetInt16(3),
            reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<ApplicationCvReviewResultPayload>(reader.GetString(4), JsonOptions),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8));
    }

    public async Task UpdateApplicationReviewAsync(Guid reviewId, string status, short? score, ApplicationCvReviewResultPayload? result, string? errorMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            UPDATE portal.application_cv_reviews
            SET status = @Status, score = @Score, result = @Result::jsonb, error_message = @ErrorMessage, 
                completed_at = CASE WHEN @Status IN ('succeeded', 'failed') THEN now() ELSE null END
            WHERE id = @Id;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("Id", reviewId);
        cmd.Parameters.AddWithValue("Status", status);
        cmd.Parameters.AddWithValue("Score", (object?)score ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Result", result is null ? DBNull.Value : JsonSerializer.Serialize(result, JsonOptions));
        cmd.Parameters.AddWithValue("ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ApplicationCvReviewResponse?> GetApplicationReviewByIdAsync(Guid companyId, Guid applicationId, Guid reviewId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = "SELECT id, candidate_id, document_id, document_kind, requested_by_member_id, status, provider, model, prompt_version, job_requirements_snapshot::text, score, result::text, error_message, created_at, completed_at, updated_at FROM portal.application_cv_reviews WHERE id = @Id AND company_id = @CompanyId AND application_id = @ApplicationId;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("Id", reviewId);
        cmd.Parameters.AddWithValue("CompanyId", companyId);
        cmd.Parameters.AddWithValue("ApplicationId", applicationId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new ApplicationCvReviewResponse(
            reader.GetGuid(0), applicationId, companyId, reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
            JsonSerializer.Deserialize<JobRequirementsSnapshot>(reader.GetString(9), JsonOptions)!,
            reader.IsDBNull(10) ? null : reader.GetInt16(10),
            reader.IsDBNull(11) ? null : JsonSerializer.Deserialize<ApplicationCvReviewResultPayload>(reader.GetString(11), JsonOptions),
            reader.IsDBNull(12) ? null : reader.GetString(12), reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14), reader.GetFieldValue<DateTimeOffset>(15));
    }

    public async Task<IReadOnlyList<ApplicationCvReviewSummaryResponse>> GetApplicationReviewsAsync(Guid companyId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var list = new List<ApplicationCvReviewSummaryResponse>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = "SELECT id, document_id, requested_by_member_id, status, score, created_at, completed_at FROM portal.application_cv_reviews WHERE company_id = @CompanyId AND application_id = @ApplicationId ORDER BY created_at DESC;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CompanyId", companyId);
        cmd.Parameters.AddWithValue("ApplicationId", applicationId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApplicationCvReviewSummaryResponse(
                reader.GetGuid(0), applicationId, reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt16(4),
                reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return list;
    }
}
