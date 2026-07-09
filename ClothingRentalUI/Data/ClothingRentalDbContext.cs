using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Data;

public class ClothingRentalDbContext : DbContext
{
    public ClothingRentalDbContext(DbContextOptions<ClothingRentalDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cấu hình mối quan hệ giữa Order và User
        modelBuilder.Entity<Order>()
            .HasOne(o => o.CreatedByUser)
            .WithMany()
            .HasForeignKey(o => o.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.PenaltyByUser)
            .WithMany()
            .HasForeignKey(o => o.PenaltyByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.ClosedByUser)
            .WithMany()
            .HasForeignKey(o => o.ClosedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cấu hình khoá ngoại trong OrderDetail
        modelBuilder.Entity<OrderDetail>()
            .HasOne(od => od.Order)
            .WithMany(o => o.OrderDetails)
            .HasForeignKey(od => od.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderDetail>()
            .HasOne(od => od.Product)
            .WithMany()
            .HasForeignKey(od => od.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
            
        modelBuilder.Entity<Product>()
            .HasOne(p => p.PriceList)
            .WithMany()
            .HasForeignKey(p => p.PriceListId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
