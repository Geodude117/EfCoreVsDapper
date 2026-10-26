namespace EfCoreVsDapper.Api.Domain;

/// <summary>A customer. EF Core maps this class to the Customers table; Dapper reads and writes the same table with SQL.</summary>
public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedUtc { get; set; }

    public List<Order> Orders { get; set; } = [];
}

/// <summary>An order placed by a customer.</summary>
public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Total { get; set; }
    public DateTime PlacedUtc { get; set; }

    public Customer? Customer { get; set; }
}
