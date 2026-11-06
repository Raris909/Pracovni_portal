using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

public sealed class CandidateProfileRepository(NpgsqlDataSource database) : ICandidateProfileRepository
{
    public async Task<CandidateProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        return await ReadProfileInternalAsync(connection, userId, cancellationToken);
    }

    public async Task<CandidateProfileResponse?> UpdateProfileAsync(Guid userId, UpdateCandidateProfileRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var userCmd = new NpgsqlCommand("""
            UPDATE portal.users SET first_name = $1, last_name = $2, phone = $3
            WHERE id = $4
            """, connection, transaction);
        userCmd.Parameters.AddWithValue(request.FirstName.Trim());
        userCmd.Parameters.AddWithValue(request.LastName.Trim());
        userCmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Phone?.Trim() ?? DBNull.Value);
        userCmd.Parameters.AddWithValue(userId);
        await userCmd.ExecuteNonQueryAsync(cancellationToken);

        await using var profileCmd = new NpgsqlCommand("""
            UPDATE portal.candidate_profiles
            SET headline = $1, summary = $2, city = $3, country_code = $4, skills = $5
            WHERE user_id = $6
            """, connection, transaction);
        profileCmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Headline?.Trim() ?? DBNull.Value);
        profileCmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Summary?.Trim() ?? DBNull.Value);
        profileCmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.City?.Trim() ?? DBNull.Value);
        profileCmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.CountryCode?.Trim().ToUpperInvariant() ?? DBNull.Value);
        profileCmd.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, request.Skills.Select(s => s.Trim()).ToArray());
        profileCmd.Parameters.AddWithValue(userId);
        await profileCmd.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await ReadProfileInternalAsync(connection, userId, cancellationToken);
    }

    public async Task<CandidateExperienceResponse> AddExperienceAsync(Guid userId, CandidateExperienceRequest request, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            INSERT INTO portal.candidate_experiences (candidate_id, employer, position, description, start_date, end_date)
            VALUES ($1, $2, $3, $4, $5, $6)
            RETURNING id
            """);

        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(request.Employer.Trim());
        command.Parameters.AddWithValue(request.Position.Trim());
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Description?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(request.StartDate);
        command.Parameters.AddWithValue(NpgsqlDbType.Date, (object?)request.EndDate ?? DBNull.Value);

        var id = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;

        return new CandidateExperienceResponse(id, request.Employer, request.Position, request.Description, request.StartDate, request.EndDate);
    }

    public async Task<CandidateExperienceResponse?> UpdateExperienceAsync(Guid userId, Guid experienceId, CandidateExperienceRequest request, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            UPDATE portal.candidate_experiences
            SET employer = $2, position = $3, description = $4, start_date = $5, end_date = $6
            WHERE id = $1 AND candidate_id = $7
            RETURNING id
            """);

        command.Parameters.AddWithValue(experienceId);
        command.Parameters.AddWithValue(request.Employer.Trim());
        command.Parameters.AddWithValue(request.Position.Trim());
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Description?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(request.StartDate);
        command.Parameters.AddWithValue(NpgsqlDbType.Date, (object?)request.EndDate ?? DBNull.Value);
        command.Parameters.AddWithValue(userId);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        return new CandidateExperienceResponse(experienceId, request.Employer, request.Position, request.Description, request.StartDate, request.EndDate);
    }

    public async Task<bool> RemoveExperienceAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            DELETE FROM portal.candidate_experiences WHERE id = $1 AND candidate_id = $2
            """);
        command.Parameters.AddWithValue(experienceId);
        command.Parameters.AddWithValue(userId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<CandidateEducationResponse> AddEducationAsync(Guid userId, CandidateEducationRequest request, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            INSERT INTO portal.candidate_education (candidate_id, school, field_of_study, degree, start_date, end_date)
            VALUES ($1, $2, $3, $4, $5, $6)
            RETURNING id
            """);

        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(request.School.Trim());
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.FieldOfStudy?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Degree?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(request.StartDate);
        command.Parameters.AddWithValue(NpgsqlDbType.Date, (object?)request.EndDate ?? DBNull.Value);

        var id = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;

        return new CandidateEducationResponse(id, request.School, request.FieldOfStudy, request.Degree, request.StartDate, request.EndDate);
    }

    public async Task<CandidateEducationResponse?> UpdateEducationAsync(Guid userId, Guid educationId, CandidateEducationRequest request, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            UPDATE portal.candidate_education
            SET school = $2, field_of_study = $3, degree = $4, start_date = $5, end_date = $6
            WHERE id = $1 AND candidate_id = $7
            RETURNING id
            """);

        command.Parameters.AddWithValue(educationId);
        command.Parameters.AddWithValue(request.School.Trim());
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.FieldOfStudy?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Degree?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(request.StartDate);
        command.Parameters.AddWithValue(NpgsqlDbType.Date, (object?)request.EndDate ?? DBNull.Value);
        command.Parameters.AddWithValue(userId);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        return new CandidateEducationResponse(educationId, request.School, request.FieldOfStudy, request.Degree, request.StartDate, request.EndDate);
    }

    public async Task<bool> RemoveEducationAsync(Guid userId, Guid educationId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            DELETE FROM portal.candidate_education WHERE id = $1 AND candidate_id = $2
            """);
        command.Parameters.AddWithValue(educationId);
        command.Parameters.AddWithValue(userId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task<CandidateProfileResponse?> ReadProfileInternalAsync(
    NpgsqlConnection connection, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
        SELECT u.id, u.email, u.first_name, u.last_name, u.phone,
               p.headline, p.summary, p.city, p.country_code, p.skills, p.current_cv_id, p.created_at, p.updated_at
        FROM portal.users u
        JOIN portal.candidate_profiles p ON p.user_id = u.id
        WHERE u.id = @userId;

        SELECT id, employer, position, description, start_date, end_date
        FROM portal.candidate_experiences WHERE candidate_id = @userId ORDER BY start_date DESC, id;

        SELECT id, school, field_of_study, degree, start_date, end_date
        FROM portal.candidate_education WHERE candidate_id = @userId ORDER BY start_date DESC, id;
        """, connection);

        command.Parameters.AddWithValue("userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var email = reader.GetString(1);
        var firstName = reader.GetString(2);
        var lastName = reader.GetString(3);
        var phone = reader.IsDBNull(4) ? null : reader.GetString(4);
        var headline = reader.IsDBNull(5) ? null : reader.GetString(5);
        var summary = reader.IsDBNull(6) ? null : reader.GetString(6);
        var city = reader.IsDBNull(7) ? null : reader.GetString(7);
        var countryCode = reader.IsDBNull(8) ? null : reader.GetString(8);
        var skills = reader.GetFieldValue<string[]>(9);
        var cvId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(11);
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(12);

        await reader.NextResultAsync(cancellationToken);
        var experiences = new List<CandidateExperienceResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            experiences.Add(new CandidateExperienceResponse(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5)));
        }

        await reader.NextResultAsync(cancellationToken);
        var education = new List<CandidateEducationResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            education.Add(new CandidateEducationResponse(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5)));
        }

        return new CandidateProfileResponse(userId, email, firstName, lastName, phone,
            headline, summary, city, countryCode, skills, experiences, education, cvId, createdAt, updatedAt);
    }
}
