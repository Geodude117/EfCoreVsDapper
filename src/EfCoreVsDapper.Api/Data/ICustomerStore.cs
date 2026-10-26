using EfCoreVsDapper.Api.Contracts;

namespace EfCoreVsDapper.Api.Data;

/// <summary>
/// Everything the API needs from the database. There are two implementations:
/// <see cref="EfCore.EfCustomerStore"/> (EF Core) and <see cref="DapperSql.DapperCustomerStore"/> (Dapper),
/// and the same test suite runs against both.
/// </summary>
public interface ICustomerStore
{
    Task<IReadOnlyList<CustomerSummary>> ListAsync(CancellationToken ct = default);

    Task<CustomerDetails?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Creates a customer and any initial orders atomically. Throws <see cref="DuplicateEmailException"/> if the email is taken.</summary>
    Task<int> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);

    /// <summary>Returns the new order's id, or null if the customer doesn't exist.</summary>
    Task<int?> AddOrderAsync(int customerId, CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>Returns false if the customer doesn't exist. Throws <see cref="DuplicateEmailException"/> if the email is taken.</summary>
    Task<bool> UpdateEmailAsync(int id, string email, CancellationToken ct = default);

    /// <summary>Deletes the customer and their orders. Returns false if the customer doesn't exist.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>The customers who have spent the most, highest first.</summary>
    Task<IReadOnlyList<TopCustomer>> TopCustomersAsync(int count, CancellationToken ct = default);
}

public class DuplicateEmailException(string email, Exception inner)
    : Exception($"A customer with the email '{email}' already exists.", inner);
