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
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

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

        // Cấu hình bảng trung gian UserPermission (Many-to-Many)
        modelBuilder.Entity<UserPermission>()
            .HasKey(up => new { up.UserId, up.PermissionId });

        modelBuilder.Entity<UserPermission>()
            .HasOne(up => up.User)
            .WithMany(u => u.UserPermissions)
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserPermission>()
            .HasOne(up => up.Permission)
            .WithMany(p => p.UserPermissions)
            .HasForeignKey(up => up.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cấu hình bảng Menu tự đệ quy và liên kết Quyền
        modelBuilder.Entity<Menu>()
            .HasOne(m => m.Parent)
            .WithMany(m => m.SubMenus)
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Menu>()
            .HasOne(m => m.RequiredPermission)
            .WithMany()
            .HasForeignKey(m => m.RequiredPermissionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
