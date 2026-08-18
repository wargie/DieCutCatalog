using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DieCutCatalog.PostgreSql.Tests;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("diecut_catalog_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public CatalogDbContext CreateDbContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString ?? _container.GetConnectionString())
            .Options;
        return new CatalogDbContext(options);
    }

    public async Task<string> CreateIsolatedDatabaseConnectionStringAsync()
    {
        var database = $"upgrade_{Guid.NewGuid():N}";
        var administratorConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "postgres"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(administratorConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{database}\";";
        await command.ExecuteNonQueryAsync();

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database
        };
        return connectionString.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
