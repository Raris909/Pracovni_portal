using Npgsql;
using NpgsqlTypes;
using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

public sealed class CompanyRepository(NpgsqlDataSource database) : ICompanyRepository
{
    private const string MembershipQuery = """
        SELECT c.id, c.name, c.registration_number, c.country_code, c.description,
               c.website_url, c.address, c.created_at, c.updated_at,
               m.id, m.user_id, u.email, u.first_name, u.last_name, m.role,
               m.is_active, m.created_at, m.updated_at
        FROM portal.companies c
        JOIN portal.company_members m ON m.company_id = c.id
        JOIN portal.users u ON u.id = m.user_id
        JOIN portal.roles r ON r.id = u.role_id
        WHERE m.user_id = $1 AND m.is_active AND u.is_active AND r.name = 'company'
        """;

    private const string MemberColumns = """
        m.id, m.company_id, m.user_id, u.email, u.first_name, u.last_name,
        m.role, m.is_active, m.created_at, m.updated_at
        """;

    public async Task<IReadOnlyList<CompanyMembershipResponse>> ListUserCompaniesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(MembershipQuery + " ORDER BY c.name, c.id");
        command.Parameters.AddWithValue(userId);
        var memberships = new List<CompanyMembershipResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            memberships.Add(ReadMembership(reader));

        return memberships;
    }

    public async Task<CompanyMembershipResponse?> FindUserCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(MembershipQuery + " AND c.id = $2");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(companyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMembership(reader) : null;
    }

    public async Task<(CompanyMembershipResponse? Membership, CreateCompanyStatus Status)> CreateCompanyAsync(
        Guid userId, CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var userCommand = new NpgsqlCommand("""
            SELECT u.email, u.first_name, u.last_name
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE u.id = $1 AND u.is_active AND r.name = 'company'
            FOR SHARE OF u
            """, connection, transaction);
        userCommand.Parameters.AddWithValue(userId);
        string email, firstName, lastName;
        await using (var reader = await userCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return (null, CreateCompanyStatus.UserNotFound);

            email = reader.GetString(0);
            firstName = reader.GetString(1);
            lastName = reader.GetString(2);
        }

        await using var companyCommand = new NpgsqlCommand("""
            INSERT INTO portal.companies (name, registration_number, country_code, description, website_url, address)
            VALUES ($1, $2, $3, $4, $5, $6)
            RETURNING id, name, registration_number, country_code, description, website_url, address, created_at, updated_at
            """, connection, transaction);
        companyCommand.Parameters.AddWithValue(request.Name.Trim());
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(request.RegistrationNumber) ? DBNull.Value : request.RegistrationNumber.Trim());
        companyCommand.Parameters.AddWithValue(request.CountryCode);
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Description ?? DBNull.Value);
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.WebsiteUrl ?? DBNull.Value);
        companyCommand.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)request.Address ?? DBNull.Value);

        CompanyResponse company;
        try
        {
            await using var reader = await companyCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            company = new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "companies_country_code_registration_number_key")
        {
            return (null, CreateCompanyStatus.DuplicateRegistrationNumber);
        }

        await using var memberCommand = new NpgsqlCommand("""
            INSERT INTO portal.company_members (company_id, user_id, role)
            VALUES ($1, $2, $3)
            RETURNING id, is_active, created_at, updated_at
            """, connection, transaction);
        memberCommand.Parameters.AddWithValue(company.Id);
        memberCommand.Parameters.AddWithValue(userId);
        memberCommand.Parameters.AddWithValue(CompanyMemberRoles.Owner);

        CompanyMemberResponse membership;
        await using (var reader = await memberCommand.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            membership = new(reader.GetGuid(0), company.Id, userId, email, firstName, lastName,
                CompanyMemberRoles.Owner, reader.GetBoolean(1),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3));
        }

        await transaction.CommitAsync(cancellationToken);
        return (new CompanyMembershipResponse(company, membership), CreateCompanyStatus.Success);
    }

    public async Task<IReadOnlyList<CompanyMemberResponse>> GetMembersAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"""
            SELECT {MemberColumns}
            FROM portal.company_members m JOIN portal.users u ON u.id = m.user_id
            WHERE m.company_id = $1 AND m.is_active
            ORDER BY u.last_name, u.first_name, m.id
            """);
        command.Parameters.AddWithValue(companyId);
        var members = new List<CompanyMemberResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            members.Add(ReadMember(reader));

        return members;
    }

    public async Task<(CompanyMemberResponse? Member, MemberManagementStatus Status)> AddMemberAsync(
        Guid companyId, Guid currentUserId, AddCompanyMemberRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var authStatus = await AuthorizeManagementAsync(connection, transaction, companyId, currentUserId, cancellationToken);
        if (authStatus != MemberManagementStatus.Success)
            return (null, authStatus);

        await using var userCommand = new NpgsqlCommand("""
            SELECT u.id
            FROM portal.users u JOIN portal.roles r ON r.id = u.role_id
            WHERE lower(u.email) = $1 AND u.is_active AND r.name = 'company'
            FOR SHARE OF u
            """, connection, transaction);
        userCommand.Parameters.AddWithValue(request.Email.Trim().ToLowerInvariant());
        var targetUserId = await userCommand.ExecuteScalarAsync(cancellationToken);
        if (targetUserId is null)
            return (null, MemberManagementStatus.UserNotFound);

        await using var command = new NpgsqlCommand($"""
            WITH m AS (
                INSERT INTO portal.company_members (company_id, user_id, role)
                VALUES ($1, $2, $3)
                ON CONFLICT (company_id, user_id) DO UPDATE
                SET role = EXCLUDED.role, is_active = true
                WHERE NOT company_members.is_active AND company_members.role <> 'owner'
                RETURNING *
            )
            SELECT {MemberColumns} FROM m JOIN portal.users u ON u.id = m.user_id
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(targetUserId);
        command.Parameters.AddWithValue(request.Role);

        CompanyMemberResponse member;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return (null, MemberManagementStatus.AlreadyMemberOrOwnerConflict);

            member = ReadMember(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return (member, MemberManagementStatus.Success);
    }

    public async Task<(CompanyMemberResponse? Member, MemberManagementStatus Status)> UpdateMemberRoleAsync(
        Guid companyId, Guid currentUserId, Guid memberId, string newRole, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var authStatus = await AuthorizeManagementAsync(connection, transaction, companyId, currentUserId, cancellationToken);
        if (authStatus != MemberManagementStatus.Success)
            return (null, authStatus);

        var targetStatus = await CheckTargetAsync(connection, transaction, companyId, memberId, cancellationToken);
        if (targetStatus != MemberManagementStatus.Success)
            return (null, targetStatus);

        await using var command = new NpgsqlCommand($"""
            UPDATE portal.company_members m SET role = $3
            FROM portal.users u
            WHERE m.company_id = $1 AND m.id = $2 AND u.id = m.user_id
            RETURNING {MemberColumns}
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId);
        command.Parameters.AddWithValue(newRole);

        CompanyMemberResponse member;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            member = ReadMember(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return (member, MemberManagementStatus.Success);
    }

    public async Task<MemberManagementStatus> RemoveMemberAsync(
        Guid companyId, Guid currentUserId, Guid memberId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var authStatus = await AuthorizeManagementAsync(connection, transaction, companyId, currentUserId, cancellationToken);
        if (authStatus != MemberManagementStatus.Success)
            return authStatus;

        var targetStatus = await CheckTargetAsync(connection, transaction, companyId, memberId, cancellationToken);
        if (targetStatus != MemberManagementStatus.Success)
            return targetStatus;

        await using var command = new NpgsqlCommand("""
            UPDATE portal.company_members SET is_active = false WHERE company_id = $1 AND id = $2
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return MemberManagementStatus.Success;
    }

    private static async Task<MemberManagementStatus> AuthorizeManagementAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid currentUserId, CancellationToken cancellationToken)
    {
        await using var companyCommand = new NpgsqlCommand(
            "SELECT id FROM portal.companies WHERE id = $1 FOR UPDATE", connection, transaction);
        companyCommand.Parameters.AddWithValue(companyId);
        if (await companyCommand.ExecuteScalarAsync(cancellationToken) is null)
            return MemberManagementStatus.CompanyNotFound;

        await using var command = new NpgsqlCommand("""
            SELECT m.role
            FROM portal.company_members m
            JOIN portal.users u ON u.id = m.user_id
            JOIN portal.roles r ON r.id = u.role_id
            WHERE m.company_id = $1 AND m.user_id = $2
              AND m.is_active AND u.is_active AND r.name = 'company'
            FOR SHARE OF m, u
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(currentUserId);
        var role = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (role is null)
            return MemberManagementStatus.CompanyNotFound;

        return role is CompanyMemberRoles.Owner or CompanyMemberRoles.Admin
            ? MemberManagementStatus.Success
            : MemberManagementStatus.Forbidden;
    }

    private static async Task<MemberManagementStatus> CheckTargetAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid memberId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT role FROM portal.company_members
            WHERE company_id = $1 AND id = $2 AND is_active
            FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(memberId);
        var role = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (role is null)
            return MemberManagementStatus.MemberNotFound;

        return role == CompanyMemberRoles.Owner
            ? MemberManagementStatus.CannotModifyOwner
            : MemberManagementStatus.Success;
    }

    private static CompanyMembershipResponse ReadMembership(NpgsqlDataReader reader)
    {
        var company = new CompanyResponse(
            reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8));
        var membership = new CompanyMemberResponse(
            reader.GetGuid(9), company.Id, reader.GetGuid(10), reader.GetString(11), reader.GetString(12),
            reader.GetString(13), reader.GetString(14), reader.GetBoolean(15),
            reader.GetFieldValue<DateTimeOffset>(16), reader.GetFieldValue<DateTimeOffset>(17));
        return new(company, membership);
    }

    private static CompanyMemberResponse ReadMember(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetBoolean(7),
            reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9));
}
