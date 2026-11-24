using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Auth;

namespace Portal.Api.Auth;

public sealed class UserRepository(NpgsqlDataSource database) : IUserRepository
{
    public async Task<RegisterUserStatus> CreateUserAsync(
        UserResponse user,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = new NpgsqlCommand("""
            INSERT INTO portal.users (id, role_id, email, password_hash, first_name, last_name)
            VALUES ($1, (SELECT id FROM portal.roles WHERE name = $2), $3, $4, $5, $6)
            """, connection, transaction);
        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(user.Role);
        command.Parameters.AddWithValue(user.Email);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(user.FirstName);
        command.Parameters.AddWithValue(user.LastName);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "users_email_unique")
        {
            return RegisterUserStatus.DuplicateEmail;
        }

        if (user.Role == UserRoles.Candidate)
        {
            await using var profile = new NpgsqlCommand(
                "INSERT INTO portal.candidate_profiles (user_id) VALUES ($1)", connection, transaction);
            profile.Parameters.AddWithValue(user.Id);
            await profile.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return RegisterUserStatus.Success;
    }

    public async Task<(UserResponse? User, string? PasswordHash)> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT u.id, u.email, u.first_name, u.last_name, r.name, u.phone, u.password_hash
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE lower(u.email) = $1 AND u.is_active
            """);
        command.Parameters.AddWithValue(email.Trim().ToLowerInvariant());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (null, null);

        var user = ReadUser(reader);
        var passwordHash = reader.GetString(6);
        return (user, passwordHash);
    }

    public async Task<UserResponse?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT u.id, u.email, u.first_name, u.last_name, r.name, u.phone
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE u.id = $1 AND u.is_active
            """);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadUser(reader);
    }

    public async Task<(UserResponse? User, string? PasswordHash)> FindWithPasswordHashByIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            SELECT u.id, u.email, u.first_name, u.last_name, r.name, u.phone, u.password_hash
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE u.id = $1 AND u.is_active
            """);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (null, null);

        var user = ReadUser(reader);
        var passwordHash = reader.GetString(6);
        return (user, passwordHash);
    }

    public async Task<UserResponse?> UpdateUserAsync(
        Guid userId,
        string firstName,
        string lastName,
        string? phone,
        CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand("""
            UPDATE portal.users u
            SET first_name = $2, last_name = $3, phone = $4
            FROM portal.roles r
            WHERE u.id = $1 AND u.is_active AND r.id = u.role_id
            RETURNING u.id, u.email, u.first_name, u.last_name, r.name, u.phone
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(firstName);
        command.Parameters.AddWithValue(lastName);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)phone ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadUser(reader);
    }

    public async Task UpdatePasswordHashAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken)
    {
        await using var update = database.CreateCommand(
            "UPDATE portal.users SET password_hash = $1 WHERE id = $2 AND is_active");
        update.Parameters.AddWithValue(newPasswordHash);
        update.Parameters.AddWithValue(userId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeactivateUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var userCommand = new NpgsqlCommand("""
            UPDATE portal.users
            SET is_active = false
            WHERE id = $1 AND is_active
            """, connection, transaction);
        userCommand.Parameters.AddWithValue(userId);

        if (await userCommand.ExecuteNonQueryAsync(cancellationToken) == 0)
            return false;

        await using var memberCommand = new NpgsqlCommand("""
            UPDATE portal.company_members
            SET is_active = false
            WHERE user_id = $1 AND is_active
            """, connection, transaction);
        memberCommand.Parameters.AddWithValue(userId);
        await memberCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static UserResponse ReadUser(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
}
