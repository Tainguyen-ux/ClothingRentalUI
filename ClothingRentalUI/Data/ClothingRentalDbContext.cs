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
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<StockHistory> StockHistories => Set<StockHistory>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<SaleOrder> SaleOrders => Set<SaleOrder>();
    public DbSet<SaleOrderDetail> SaleOrderDetails => Set<SaleOrderDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Customer → Orders
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order → Voucher
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Voucher)
            .WithMany()
            .HasForeignKey(o => o.VoucherId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cấu hình mối quan hệ giữa Order và User
        modelBuilder.Entity<Order>()
            .HasOne(o => o.CreatedByUser)
            .WithMany()
            .HasForeignKey(o => o.CreatedByUserId)
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

        // Transaction → Order
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Order)
            .WithMany(o => o.Transactions)
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Transaction → SaleOrder
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.SaleOrder)
            .WithMany(so => so.Transactions)
            .HasForeignKey(t => t.SaleOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Customer → SaleOrder
        modelBuilder.Entity<SaleOrder>()
            .HasOne(so => so.Customer)
            .WithMany()
            .HasForeignKey(so => so.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // SaleOrder → Voucher
        modelBuilder.Entity<SaleOrder>()
            .HasOne(so => so.Voucher)
            .WithMany()
            .HasForeignKey(so => so.VoucherId)
            .OnDelete(DeleteBehavior.SetNull);

        // SaleOrder → User (Creator)
        modelBuilder.Entity<SaleOrder>()
            .HasOne(so => so.CreatedByUser)
            .WithMany()
            .HasForeignKey(so => so.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // SaleOrderDetail → SaleOrder
        modelBuilder.Entity<SaleOrderDetail>()
            .HasOne(sod => sod.SaleOrder)
            .WithMany(so => so.SaleOrderDetails)
            .HasForeignKey(sod => sod.SaleOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // SaleOrderDetail → Product
        modelBuilder.Entity<SaleOrderDetail>()
            .HasOne(sod => sod.Product)
            .WithMany()
            .HasForeignKey(sod => sod.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
            
        modelBuilder.Entity<Product>()
            .HasOne(p => p.PriceList)
            .WithMany()
            .HasForeignKey(p => p.PriceListId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StockHistory>()
            .HasOne(sh => sh.Product)
            .WithMany(p => p.StockHistories)
            .HasForeignKey(sh => sh.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

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

        // Customer PhoneNumber unique index
        modelBuilder.Entity<Customer>()
            .HasIndex(c => c.PhoneNumber)
            .IsUnique();
    }
}
