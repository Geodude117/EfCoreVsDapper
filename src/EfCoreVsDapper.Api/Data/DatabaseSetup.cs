using EfCoreVsDapper.Api.Contracts;
using EfCoreVsDapper.Api.Data.EfCore;
using Microsoft.EntityFrameworkCore;

namespace EfCoreVsDapper.Api.Data;

public static class DatabaseSetup
{
    /// <summary>
    /// Creates the database from the EF Core model if it doesn't exist, then adds sample data.
    /// EnsureCreated keeps the demo to one command; a real application would use EF Core
    /// migrations (dotnet ef migrations add / database update) so the schema can evolve.
    /// </summary>
    public static async Task InitialiseAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureCreatedAsync(ct);

        if (await db.Customers.AnyAsync(ct))
            return;

        var store = new EfCustomerStore(db);
        await store.CreateAsync(new CreateCustomerRequest("Ada Lovelace", "ada@example.com", [120.50m, 42.00m]), ct);
        await store.CreateAsync(new CreateCustomerRequest("Alan Turing", "alan@example.com", [310.00m]), ct);
        await store.CreateAsync(new CreateCustomerRequest("Grace Hopper", "grace@example.com", [15.99m, 89.00m, 250.00m]), ct);
        await store.CreateAsync(new CreateCustomerRequest("Linus Torvalds", "linus@example.com"), ct);
    }
}
