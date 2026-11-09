using Npgsql;
using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

public sealed class CandidateDocumentRepository : ICandidateDocumentRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public CandidateDocumentRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<CandidateDocumentRecord> InsertDocumentAsync(
        Guid candidateId,
        string kind,
        string originalFilename,
        string storageKey,
        string mimeType,
        long sizeBytes,
        string sha256,
        bool setAsCurrentCv,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 1. Ensure candidate_profiles row exists (required by FK on candidate_documents)
        const string ensureProfileSql = """
            INSERT INTO portal.candidate_profiles (user_id)
            VALUES (@CandidateId)
            ON CONFLICT (user_id) DO NOTHING;
            """;

        await using (var profileCmd = new NpgsqlCommand(ensureProfileSql, connection, transaction))
        {
            profileCmd.Parameters.AddWithValue("CandidateId", candidateId);
            await profileCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Insert immutable document metadata
        const string insertDocSql = """
            INSERT INTO portal.candidate_documents (
                candidate_id, kind, original_filename, storage_key, mime_type, size_bytes, sha256
            )
            VALUES (
                @CandidateId, @Kind, @OriginalFilename, @StorageKey, @MimeType, @SizeBytes, @Sha256
            )
            RETURNING id, created_at;
            """;

        Guid documentId;
        DateTimeOffset createdAt;

        await using (var insertCmd = new NpgsqlCommand(insertDocSql, connection, transaction))
        {
            insertCmd.Parameters.AddWithValue("CandidateId", candidateId);
            insertCmd.Parameters.AddWithValue("Kind", kind);
            insertCmd.Parameters.AddWithValue("OriginalFilename", originalFilename);
            insertCmd.Parameters.AddWithValue("StorageKey", storageKey);
            insertCmd.Parameters.AddWithValue("MimeType", mimeType);
            insertCmd.Parameters.AddWithValue("SizeBytes", sizeBytes);
            insertCmd.Parameters.AddWithValue("Sha256", sha256);

            await using var reader = await insertCmd.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            documentId = reader.GetGuid(0);
            createdAt = reader.GetFieldValue<DateTimeOffset>(1);
        }

        // 3. Optionally set as current_cv_id on candidate_profiles
        var isCurrentCv = false;
        if (setAsCurrentCv && kind == CandidateDocumentKinds.Cv)
        {
            const string updateCurrentCvSql = """
                UPDATE portal.candidate_profiles
                SET current_cv_id = @DocumentId,
                    current_cv_kind = 'cv'
                WHERE user_id = @CandidateId;
                """;

            await using var updateCmd = new NpgsqlCommand(updateCurrentCvSql, connection, transaction);
            updateCmd.Parameters.AddWithValue("DocumentId", documentId);
            updateCmd.Parameters.AddWithValue("CandidateId", candidateId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            isCurrentCv = true;
        }

        await transaction.CommitAsync(cancellationToken);

        return new CandidateDocumentRecord(
            documentId,
            candidateId,
            kind,
            originalFilename,
            storageKey,
            mimeType,
            sizeBytes,
            sha256,
            isCurrentCv,
            createdAt);
    }

    public async Task<IReadOnlyList<CandidateDocumentResponse>> GetCandidateDocumentsAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                d.id,
                d.candidate_id,
                d.kind,
                d.original_filename,
                d.mime_type,
                d.size_bytes,
                d.sha256,
                (p.current_cv_id = d.id) AS is_current_cv,
                d.created_at
            FROM portal.candidate_documents d
            JOIN portal.candidate_profiles p ON p.user_id = d.candidate_id
            WHERE d.candidate_id = @CandidateId
            ORDER BY d.created_at DESC;
            """;

        var list = new List<CandidateDocumentResponse>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new CandidateDocumentResponse(
                Id: reader.GetGuid(0),
                CandidateId: reader.GetGuid(1),
                Kind: reader.GetString(2),
                OriginalFilename: reader.GetString(3),
                MimeType: reader.GetString(4),
                SizeBytes: reader.GetInt64(5),
                Sha256: reader.GetString(6),
                IsCurrentCv: !reader.IsDBNull(7) && reader.GetBoolean(7),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return list;
    }

    public async Task<CandidateDocumentRecord?> GetDocumentByIdAsync(
        Guid candidateId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                d.id,
                d.candidate_id,
                d.kind,
                d.original_filename,
                d.storage_key,
                d.mime_type,
                d.size_bytes,
                d.sha256,
                (p.current_cv_id = d.id) AS is_current_cv,
                d.created_at
            FROM portal.candidate_documents d
            JOIN portal.candidate_profiles p ON p.user_id = d.candidate_id
            WHERE d.candidate_id = @CandidateId AND d.id = @DocumentId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);
        cmd.Parameters.AddWithValue("DocumentId", documentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadRecord(reader);
    }

    public async Task<CandidateDocumentRecord?> GetCurrentCvAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                d.id,
                d.candidate_id,
                d.kind,
                d.original_filename,
                d.storage_key,
                d.mime_type,
                d.size_bytes,
                d.sha256,
                true AS is_current_cv,
                d.created_at
            FROM portal.candidate_profiles p
            JOIN portal.candidate_documents d
              ON d.id = p.current_cv_id AND d.candidate_id = p.user_id
            WHERE p.user_id = @CandidateId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadRecord(reader);
    }

    public async Task<bool> SetCurrentCvAsync(
        Guid candidateId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE portal.candidate_profiles p
            SET current_cv_id = d.id,
                current_cv_kind = 'cv'
            FROM portal.candidate_documents d
            WHERE p.user_id = @CandidateId
              AND d.id = @DocumentId
              AND d.candidate_id = @CandidateId
              AND d.kind = 'cv';
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("CandidateId", candidateId);
        cmd.Parameters.AddWithValue("DocumentId", documentId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    private static CandidateDocumentRecord ReadRecord(NpgsqlDataReader reader) =>
        new(
            Id: reader.GetGuid(0),
            CandidateId: reader.GetGuid(1),
            Kind: reader.GetString(2),
            OriginalFilename: reader.GetString(3),
            StorageKey: reader.GetString(4),
            MimeType: reader.GetString(5),
            SizeBytes: reader.GetInt64(6),
            Sha256: reader.GetString(7),
            IsCurrentCv: !reader.IsDBNull(8) && reader.GetBoolean(8),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(9));
}