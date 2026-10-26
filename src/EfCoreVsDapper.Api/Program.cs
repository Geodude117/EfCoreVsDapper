using EfCoreVsDapper.Api.Data;
using EfCoreVsDapper.Api.Data.DapperSql;
using EfCoreVsDapper.Api.Data.EfCore;
using EfCoreVsDapper.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

// EF Core: a DbContext per request, configured for SQL Server.
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<EfCustomerStore>();

// Dapper: just a way to open connections. There's no model or change tracker to configure.
builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<DapperCustomerStore>();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

await DatabaseSetup.InitialiseAsync(app.Services);

app.MapGet("/", () => Results.Text(
    """
    EF Core vs Dapper — the same API, served two ways.

    EF Core:  GET /ef/customers
    Dapper:   GET /dapper/customers

    See EfCoreVsDapper.http for every endpoint.
    """));

app.MapCustomers<EfCustomerStore>("ef");
app.MapCustomers<DapperCustomerStore>("dapper");

app.Run();
