using Npgsql;
using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

public sealed class CompanyMembershipService(NpgsqlDataSource database)
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

    public async Task<IReadOnlyList<CompanyMembershipResponse>> ListAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(MembershipQuery + " ORDER BY c.name, c.id");
        command.Parameters.AddWithValue(userId);
        var memberships = new List<CompanyMembershipResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            memberships.Add(ReadMembership(reader));

        return memberships;
    }

    public async Task<CompanyMembershipResponse?> FindAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(MembershipQuery + " AND c.id = $2");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(companyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMembership(reader) : null;
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
}
