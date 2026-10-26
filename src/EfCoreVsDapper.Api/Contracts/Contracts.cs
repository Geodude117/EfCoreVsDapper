namespace EfCoreVsDapper.Api.Contracts;

// Requests
public record CreateCustomerRequest(string Name, string Email, IReadOnlyList<decimal>? InitialOrders = null);
public record UpdateEmailRequest(string Email);
public record CreateOrderRequest(decimal Total);

// Responses. Both stores return exactly these shapes, so the API looks identical whichever one serves it.
public record CustomerSummary(int Id, string Name, string Email, int OrderCount);
public record OrderDto(int Id, decimal Total, DateTime PlacedUtc);
public record CustomerDetails(int Id, string Name, string Email, DateTime CreatedUtc, IReadOnlyList<OrderDto> Orders);
public record TopCustomer(int Id, string Name, decimal TotalSpent, int OrderCount);
