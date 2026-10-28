using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Portal.IntegrationTests;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        foreach (var script in new[] { "schema.sql", "seed.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", script));
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Portal"] = database.GetConnectionString(),
                ["Jwt:SigningKey"] = new string('t', 64)
            }));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await database.DisposeAsync();
    }

    public async Task<object?> QueryAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter);
        return await command.ExecuteScalarAsync();
    }
}
