using EfCoreVsDapper.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EfCoreVsDapper.Api.Data.EfCore;

/// <summary>
/// The EF Core model. It also defines the database schema that Dapper uses,
/// so both approaches always work against exactly the same tables.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Customer>(c =>
        {
            c.ToTable("Customers");
            c.Property(x => x.Name).HasMaxLength(100).IsRequired();
            c.Property(x => x.Email).HasMaxLength(200).IsRequired();
            c.HasIndex(x => x.Email).IsUnique();
            c.Property(x => x.CreatedUtc).HasColumnType("datetime2");

            c.HasMany(x => x.Orders)
             .WithOne(o => o.Customer)
             .HasForeignKey(o => o.CustomerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Order>(o =>
        {
            o.ToTable("Orders");
            o.Property(x => x.Total).HasPrecision(18, 2);
            o.Property(x => x.PlacedUtc).HasColumnType("datetime2");
            o.HasIndex(x => x.CustomerId);
        });
    }
}
