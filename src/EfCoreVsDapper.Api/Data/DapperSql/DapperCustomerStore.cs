using Dapper;
using EfCoreVsDapper.Api.Contracts;

namespace EfCoreVsDapper.Api.Data.DapperSql;

/// <summary>
/// The Dapper implementation: every query is hand-written SQL, and Dapper maps the
/// rows onto C# types. Parameters are always passed as objects, never concatenated
/// into the SQL, so there is no injection risk.
/// </summary>
public class DapperCustomerStore(SqlConnectionFactory connections) : ICustomerStore
{
    public async Task<IReadOnlyList<CustomerSummary>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.Id, c.Name, c.Email,
                   (SELECT COUNT(*) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount
            FROM Customers c
            ORDER BY c.Name, c.Id;
            """;

        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<CustomerSummary>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CustomerDetails?> GetAsync(int id, CancellationToken ct = default)
    {
        // Two SELECTs in one round trip, read back one after the other with QueryMultiple.
        const string sql = """
            SELECT Id, Name, Email, CreatedUtc FROM Customers WHERE Id = @Id;

            SELECT Id, Total, PlacedUtc FROM Orders WHERE CustomerId = @Id ORDER BY PlacedUtc, Id;
            """;

        await using var conn = connections.Create();
        using var results = await conn.QueryMultipleAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));

        var customer = await results.ReadSingleOrDefaultAsync<CustomerRow>();
        if (customer is null)
            return null;

        var orders = (await results.ReadAsync<OrderDto>()).AsList();
        return new CustomerDetails(customer.Id, customer.Name, customer.Email, customer.CreatedUtc, orders);
    }

    public async Task<int> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        const string insertCustomer = """
            INSERT INTO Customers (Name, Email, CreatedUtc)
            OUTPUT INSERTED.Id
            VALUES (@Name, @Email, @CreatedUtc);
            """;
        const string insertOrder = """
            INSERT INTO Orders (CustomerId, Total, PlacedUtc)
            VALUES (@CustomerId, @Total, @PlacedUtc);
            """;

        var now = DateTime.UtcNow;

        await using var conn = connections.Create();
        await conn.OpenAsync(ct);

        // With Dapper the transaction is explicit: the customer and their orders
        // are saved together, or not at all. If anything throws before CommitAsync,
        // disposing the transaction rolls it back.
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                insertCustomer, new { request.Name, request.Email, CreatedUtc = now }, tx, cancellationToken: ct));

            var orders = (request.InitialOrders ?? [])
                .Select(total => new { CustomerId = id, Total = total, PlacedUtc = now })
                .ToList();

            // Passing a list runs the statement once per item, inside the same transaction.
            if (orders.Count > 0)
                await conn.ExecuteAsync(new CommandDefinition(insertOrder, orders, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return id;
        }
        catch (Exception ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            throw new DuplicateEmailException(request.Email, ex);
        }
    }

    public async Task<int?> AddOrderAsync(int customerId, CreateOrderRequest request, CancellationToken ct = default)
    {
        // One statement checks the customer exists and inserts the order.
        // If there's no such customer, nothing is inserted and the result is null.
        const string sql = """
            INSERT INTO Orders (CustomerId, Total, PlacedUtc)
            OUTPUT INSERTED.Id
            SELECT @CustomerId, @Total, @PlacedUtc
            WHERE EXISTS (SELECT 1 FROM Customers WHERE Id = @CustomerId);
            """;

        await using var conn = connections.Create();
        return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            sql, new { CustomerId = customerId, request.Total, PlacedUtc = DateTime.UtcNow }, cancellationToken: ct));
    }

    public async Task<bool> UpdateEmailAsync(int id, string email, CancellationToken ct = default)
    {
        const string sql = "UPDATE Customers SET Email = @Email WHERE Id = @Id;";

        await using var conn = connections.Create();
        try
        {
            return await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Email = email }, cancellationToken: ct)) == 1;
        }
        catch (Exception ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            throw new DuplicateEmailException(email, ex);
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        // The foreign key's ON DELETE CASCADE removes the customer's orders.
        const string sql = "DELETE FROM Customers WHERE Id = @Id;";

        await using var conn = connections.Create();
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<TopCustomer>> TopCustomersAsync(int count, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (@Count)
                   c.Id, c.Name,
                   SUM(o.Total) AS TotalSpent,
                   COUNT(*)     AS OrderCount
            FROM Customers c
            JOIN Orders o ON o.CustomerId = c.Id
            GROUP BY c.Id, c.Name
            ORDER BY TotalSpent DESC, c.Id;
            """;

        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<TopCustomer>(new CommandDefinition(sql, new { Count = count }, cancellationToken: ct));
        return rows.AsList();
    }

    // Dapper maps columns onto this record's constructor by name.
    internal sealed record CustomerRow(int Id, string Name, string Email, DateTime CreatedUtc);
}
