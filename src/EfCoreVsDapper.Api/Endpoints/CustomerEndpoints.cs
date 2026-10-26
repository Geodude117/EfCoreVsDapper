using EfCoreVsDapper.Api.Contracts;
using EfCoreVsDapper.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EfCoreVsDapper.Api.Endpoints;

public static class CustomerEndpoints
{
    /// <summary>
    /// Maps the same set of endpoints for a given store, e.g. /ef/customers and /dapper/customers.
    /// The handlers only know about <see cref="ICustomerStore"/>, so they're identical for both.
    /// </summary>
    public static RouteGroupBuilder MapCustomers<TStore>(this IEndpointRouteBuilder app, string prefix)
        where TStore : class, ICustomerStore
    {
        var group = app.MapGroup($"/{prefix}/customers").WithTags(prefix);

        group.MapGet("/", async (TStore store, CancellationToken ct) =>
            TypedResults.Ok(await store.ListAsync(ct)));

        group.MapGet("/{id:int}", async Task<Results<Ok<CustomerDetails>, NotFound>> (int id, TStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } customer ? TypedResults.Ok(customer) : TypedResults.NotFound());

        group.MapGet("/top", async (TStore store, CancellationToken ct, int count = 5) =>
            TypedResults.Ok(await store.TopCustomersAsync(Math.Clamp(count, 1, 100), ct)));

        group.MapPost("/", async Task<Results<Created<IdResponse>, ValidationProblem, Conflict<string>>> (
            CreateCustomerRequest request, TStore store, CancellationToken ct) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            try
            {
                var id = await store.CreateAsync(request, ct);
                return TypedResults.Created($"/{prefix}/customers/{id}", new IdResponse(id));
            }
            catch (DuplicateEmailException ex)
            {
                return TypedResults.Conflict(ex.Message);
            }
        });

        group.MapPost("/{id:int}/orders", async Task<Results<Created<IdResponse>, ValidationProblem, NotFound>> (
            int id, CreateOrderRequest request, TStore store, CancellationToken ct) =>
        {
            if (request.Total <= 0)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["total"] = ["Total must be greater than zero."] });

            return await store.AddOrderAsync(id, request, ct) is { } orderId
                ? TypedResults.Created($"/{prefix}/customers/{id}", new IdResponse(orderId))
                : TypedResults.NotFound();
        });

        group.MapPut("/{id:int}/email", async Task<Results<NoContent, ValidationProblem, NotFound, Conflict<string>>> (
            int id, UpdateEmailRequest request, TStore store, CancellationToken ct) =>
        {
            if (!LooksLikeEmail(request.Email))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["A valid email is required."] });

            try
            {
                return await store.UpdateEmailAsync(id, request.Email, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
            }
            catch (DuplicateEmailException ex)
            {
                return TypedResults.Conflict(ex.Message);
            }
        });

        group.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (int id, TStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound());

        return group;
    }

    public record IdResponse(int Id);

    private static Dictionary<string, string[]> Validate(CreateCustomerRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            errors["name"] = ["Name is required and must be 100 characters or fewer."];
        if (!LooksLikeEmail(request.Email))
            errors["email"] = ["A valid email is required."];
        if (request.InitialOrders?.Any(t => t <= 0) == true)
            errors["initialOrders"] = ["Every order total must be greater than zero."];
        return errors;
    }

    private static bool LooksLikeEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 200 && email.Contains('@') && !email.StartsWith('@') && !email.EndsWith('@');
}
