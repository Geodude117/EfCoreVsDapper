using EfCoreVsDapper.Api.Contracts;
using EfCoreVsDapper.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EfCoreVsDapper.Api.Data.EfCore;

/// <summary>
/// The EF Core implementation: queries are written in LINQ and EF Core generates the SQL.
/// Turn on Information logging for "Microsoft.EntityFrameworkCore.Database.Command"
/// (already on in appsettings.Development.json) to see the SQL it produces.
/// </summary>
public class EfCustomerStore(AppDbContext db) : ICustomerStore
{
    public async Task<IReadOnlyList<CustomerSummary>> ListAsync(CancellationToken ct = default) =>
        // Projecting straight into the response shape means EF Core selects only these
        // columns and translates Orders.Count() into a subquery. Nothing is tracked.
        await db.Customers
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CustomerSummary(c.Id, c.Name, c.Email, c.Orders.Count()))
            .ToListAsync(ct);

    public async Task<CustomerDetails?> GetAsync(int id, CancellationToken ct = default) =>
        // A nested collection projection: one query returns the customer and their orders.
        await db.Customers
            .Where(c => c.Id == id)
            .Select(c => new CustomerDetails(
                c.Id, c.Name, c.Email, c.CreatedUtc,
                c.Orders
                    .OrderBy(o => o.PlacedUtc).ThenBy(o => o.Id)
                    .Select(o => new OrderDto(o.Id, o.Total, o.PlacedUtc))
                    .ToList()))
            .SingleOrDefaultAsync(ct);

    public async Task<int> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            Name = request.Name,
            Email = request.Email,
            CreatedUtc = now,
            Orders = (request.InitialOrders ?? [])
                .Select(total => new Order { Total = total, PlacedUtc = now })
                .ToList()
        };

        db.Customers.Add(customer);
        try
        {
            // SaveChanges wraps every INSERT (customer and orders) in one transaction,
            // and fills in the generated ids, including each order's CustomerId.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            throw new DuplicateEmailException(request.Email, ex);
        }
        return customer.Id;
    }

    public async Task<int?> AddOrderAsync(int customerId, CreateOrderRequest request, CancellationToken ct = default)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            return null;

        var order = new Order { CustomerId = customerId, Total = request.Total, PlacedUtc = DateTime.UtcNow };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order.Id;
    }

    public async Task<bool> UpdateEmailAsync(int id, string email, CancellationToken ct = default)
    {
        try
        {
            // ExecuteUpdate sends a single UPDATE without loading the entity first.
            var rows = await db.Customers
                .Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Email, email), ct);
            return rows == 1;
        }
        catch (Exception ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            throw new DuplicateEmailException(email, ex);
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
        // A single DELETE; the database's cascade rule removes the orders.
        await db.Customers.Where(c => c.Id == id).ExecuteDeleteAsync(ct) == 1;

    public async Task<IReadOnlyList<TopCustomer>> TopCustomersAsync(int count, CancellationToken ct = default) =>
        await db.Customers
            .Where(c => c.Orders.Any())
            .Select(c => new
            {
                c.Id,
                c.Name,
                TotalSpent = c.Orders.Sum(o => o.Total),
                OrderCount = c.Orders.Count()
            })
            .OrderByDescending(x => x.TotalSpent).ThenBy(x => x.Id)
            .Take(count)
            .Select(x => new TopCustomer(x.Id, x.Name, x.TotalSpent, x.OrderCount))
            .ToListAsync(ct);
}
