using EfCoreVsDapper.Api.Data;
using EfCoreVsDapper.Api.Data.DapperSql;
using EfCoreVsDapper.Api.Data.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EfCoreVsDapper.Tests;

/// <summary>
/// Starts a real SQL Server in Docker once for the whole test run, creates the schema
/// from the EF Core model, and hands out either store on demand.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "EfCoreVsDapperTests"
        }.ConnectionString;

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public ICustomerStore CreateStore(string kind) => kind switch
    {
        "ef" => new EfCustomerStore(CreateDbContext()),
        "dapper" => new DapperCustomerStore(new SqlConnectionFactory(ConnectionString)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Expected 'ef' or 'dapper'.")
    };

    /// <summary>Empties both tables so each test starts from a clean slate.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Orders; DELETE FROM Customers;");
    }
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
