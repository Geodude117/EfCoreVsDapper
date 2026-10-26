using EfCoreVsDapper.Api.Contracts;
using EfCoreVsDapper.Api.Data;

namespace EfCoreVsDapper.Tests;

/// <summary>
/// One set of behaviour tests, run once against EF Core and once against Dapper.
/// If both implementations pass, they're interchangeable from the API's point of view.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CustomerStoreTests(SqlServerFixture fixture) : IAsyncLifetime
{
    public static TheoryData<string> Stores => ["ef", "dapper"];

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory, MemberData(nameof(Stores))]
    public async Task Create_then_get_returns_customer_with_orders(string kind)
    {
        var store = fixture.CreateStore(kind);

        var id = await store.CreateAsync(new CreateCustomerRequest("Ada Lovelace", "ada@example.com", [120.50m, 42.00m]));
        var customer = await store.GetAsync(id);

        Assert.NotNull(customer);
        Assert.Equal("Ada Lovelace", customer.Name);
        Assert.Equal("ada@example.com", customer.Email);
        Assert.Equal(new[] { 120.50m, 42.00m }, customer.Orders.Select(o => o.Total));
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Get_returns_null_for_missing_customer(string kind)
    {
        var store = fixture.CreateStore(kind);

        Assert.Null(await store.GetAsync(999_999));
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task List_returns_customers_sorted_by_name_with_order_counts(string kind)
    {
        var store = fixture.CreateStore(kind);
        await store.CreateAsync(new CreateCustomerRequest("Grace", "grace@example.com", [1m, 2m, 3m]));
        await store.CreateAsync(new CreateCustomerRequest("Alan", "alan@example.com"));

        var list = await store.ListAsync();

        Assert.Equal(new[] { "Alan", "Grace" }, list.Select(c => c.Name));
        Assert.Equal(new[] { 0, 3 }, list.Select(c => c.OrderCount));
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Duplicate_email_throws_and_leaves_nothing_behind(string kind)
    {
        var store = fixture.CreateStore(kind);
        await store.CreateAsync(new CreateCustomerRequest("First", "same@example.com"));

        await Assert.ThrowsAsync<DuplicateEmailException>(() =>
            store.CreateAsync(new CreateCustomerRequest("Second", "same@example.com", [10m, 20m])));

        // The failed create must not have left orphaned orders: the transaction rolled back.
        var list = await fixture.CreateStore(kind).ListAsync();
        var only = Assert.Single(list);
        Assert.Equal("First", only.Name);
        Assert.Equal(0, only.OrderCount);
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Add_order_to_existing_customer(string kind)
    {
        var store = fixture.CreateStore(kind);
        var id = await store.CreateAsync(new CreateCustomerRequest("Alan", "alan@example.com"));

        var orderId = await store.AddOrderAsync(id, new CreateOrderRequest(49.95m));

        Assert.NotNull(orderId);
        var customer = await store.GetAsync(id);
        Assert.Equal(49.95m, Assert.Single(customer!.Orders).Total);
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Add_order_to_missing_customer_returns_null(string kind)
    {
        var store = fixture.CreateStore(kind);

        Assert.Null(await store.AddOrderAsync(999_999, new CreateOrderRequest(10m)));
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Update_email_changes_it(string kind)
    {
        var store = fixture.CreateStore(kind);
        var id = await store.CreateAsync(new CreateCustomerRequest("Ada", "ada@example.com"));

        Assert.True(await store.UpdateEmailAsync(id, "ada.lovelace@example.com"));
        Assert.Equal("ada.lovelace@example.com", (await store.GetAsync(id))!.Email);
        Assert.False(await store.UpdateEmailAsync(999_999, "nobody@example.com"));
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Update_email_to_one_in_use_throws(string kind)
    {
        var store = fixture.CreateStore(kind);
        await store.CreateAsync(new CreateCustomerRequest("Ada", "ada@example.com"));
        var alan = await store.CreateAsync(new CreateCustomerRequest("Alan", "alan@example.com"));

        await Assert.ThrowsAsync<DuplicateEmailException>(() => store.UpdateEmailAsync(alan, "ada@example.com"));
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Delete_removes_customer_and_their_orders(string kind)
    {
        var store = fixture.CreateStore(kind);
        var id = await store.CreateAsync(new CreateCustomerRequest("Grace", "grace@example.com", [5m, 6m]));

        Assert.True(await store.DeleteAsync(id));
        Assert.Null(await store.GetAsync(id));
        Assert.False(await store.DeleteAsync(id));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, db.Orders.Count());
    }

    [Theory, MemberData(nameof(Stores))]
    public async Task Top_customers_are_ranked_by_total_spent(string kind)
    {
        var store = fixture.CreateStore(kind);
        await store.CreateAsync(new CreateCustomerRequest("Small", "small@example.com", [10m]));
        await store.CreateAsync(new CreateCustomerRequest("Big", "big@example.com", [100m, 250m]));
        await store.CreateAsync(new CreateCustomerRequest("Middle", "middle@example.com", [80m, 20m, 50m]));
        await store.CreateAsync(new CreateCustomerRequest("None", "none@example.com"));

        var top = await store.TopCustomersAsync(2);

        Assert.Equal(new[] { "Big", "Middle" }, top.Select(t => t.Name));
        Assert.Equal(new[] { 350m, 150m }, top.Select(t => t.TotalSpent));
        Assert.Equal(new[] { 2, 3 }, top.Select(t => t.OrderCount));
    }

    [Fact]
    public async Task Both_stores_return_identical_results_for_the_same_data()
    {
        var ef = fixture.CreateStore("ef");
        var dapper = fixture.CreateStore("dapper");
        var id = await ef.CreateAsync(new CreateCustomerRequest("Ada", "ada@example.com", [1.5m, 2.25m]));
        await dapper.CreateAsync(new CreateCustomerRequest("Alan", "alan@example.com", [3m]));

        Assert.Equal(await ef.ListAsync(), await dapper.ListAsync());
        Assert.Equal(await ef.TopCustomersAsync(5), await dapper.TopCustomersAsync(5));

        var fromEf = (await ef.GetAsync(id))!;
        var fromDapper = (await dapper.GetAsync(id))!;
        Assert.Equal((fromEf.Id, fromEf.Name, fromEf.Email, fromEf.CreatedUtc),
                     (fromDapper.Id, fromDapper.Name, fromDapper.Email, fromDapper.CreatedUtc));
        Assert.Equal(fromEf.Orders, fromDapper.Orders);
    }
}
