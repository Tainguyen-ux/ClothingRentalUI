using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Đăng ký HttpContextAccessor và Session phục vụ việc lưu trữ thông tin đăng nhập trên Server
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2); // Session sống trong 2 tiếng
    options.Cookie.HttpOnly = true;             // Chặn JavaScript đọc cookie để tránh bị ăn cắp session (XSS)
    options.Cookie.IsEssential = true;           // Đảm bảo Cookie hoạt động kể cả khi người dùng từ chối tracking
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Phù hợp với cả HTTP local và HTTPS production
    options.Cookie.SameSite = SameSiteMode.Strict; // Chống CSRF hiệu quả bằng cách cấm gửi cookie từ nguồn ngoài
});

// Cấu hình Database PostgreSQL
builder.Services.AddDbContext<ClothingRentalDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Đăng ký các dịch vụ xử lý nghiệp vụ Monolith
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IClothesService, ClothesService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Self-healing: Tạo bảng mới và cột mới nếu chưa tồn tại (tránh lỗi với DB đã có sẵn không có migration history)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClothingRentalDbContext>();
    try
    {
        await db.Database.ExecuteSqlRawAsync(@"
            -- Bảng Customers
            CREATE TABLE IF NOT EXISTS ""Customers"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""FullName"" VARCHAR(150) NOT NULL,
                ""PhoneNumber"" VARCHAR(15) NOT NULL,
                ""IdentityCard"" VARCHAR(20),
                ""Address"" VARCHAR(250),
                ""Status"" VARCHAR(20) NOT NULL DEFAULT 'Active',
                ""Notes"" TEXT,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Customers_PhoneNumber"" ON ""Customers"" (""PhoneNumber"");

            -- Bảng Transactions
            CREATE TABLE IF NOT EXISTS ""Transactions"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""OrderId"" INTEGER NOT NULL REFERENCES ""Orders""(""Id"") ON DELETE CASCADE,
                ""Type"" VARCHAR(30) NOT NULL,
                ""PaymentMethod"" VARCHAR(20) NOT NULL DEFAULT 'CASH',
                ""Amount"" DECIMAL NOT NULL DEFAULT 0,
                ""TransactionDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""PerformedBy"" VARCHAR(100) NOT NULL,
                ""ReferenceCode"" VARCHAR(100),
                ""Notes"" VARCHAR(250)
            );

            -- Thêm cột mới vào Orders nếu chưa có
            -- Bỏ ràng buộc NOT NULL trên tất cả cột cũ (legacy) để tránh conflict khi insert đơn mới
            DO $$
            DECLARE col RECORD;
            BEGIN
                FOR col IN
                    SELECT column_name FROM information_schema.columns
                    WHERE table_name = 'Orders' AND is_nullable = 'NO' AND column_name != 'Id'
                LOOP
                    EXECUTE format('ALTER TABLE ""Orders"" ALTER COLUMN %I DROP NOT NULL', col.column_name);
                END LOOP;
            END $$;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""CustomerId"" INTEGER REFERENCES ""Customers""(""Id"");
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""Code"" VARCHAR(50) NOT NULL DEFAULT '';
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""RentDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW();
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""DueDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW();
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""ActualReturnDate"" TIMESTAMP WITH TIME ZONE;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""TotalPrice"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""TotalDeposit"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""TotalPenalty"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""FinalAmount"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""VoucherId"" INTEGER REFERENCES ""Vouchers""(""Id"");
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""DiscountAmount"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""DepositStatus"" VARCHAR(20) NOT NULL DEFAULT 'None';
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""AttachmentUrl"" TEXT;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""Notes"" TEXT;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""CreatedByUserId"" INTEGER;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""ClosedByUserId"" INTEGER;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""IsIdCardReceived"" BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW();

            -- Bỏ ràng buộc NOT NULL trên tất cả cột cũ OrderDetails
            DO $$
            DECLARE col RECORD;
            BEGIN
                FOR col IN
                    SELECT column_name FROM information_schema.columns
                    WHERE table_name = 'OrderDetails' AND is_nullable = 'NO' AND column_name != 'Id'
                LOOP
                    EXECUTE format('ALTER TABLE ""OrderDetails"" ALTER COLUMN %I DROP NOT NULL', col.column_name);
                END LOOP;
            END $$;
            -- Thêm cột mới vào OrderDetails nếu chưa có
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""RentPrice"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""Deposit"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""RentDays"" INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""ExtendedDays"" INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""PenaltyFee"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""PenaltyReason"" TEXT;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""IsReturned"" BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""ReturnDate"" TIMESTAMP WITH TIME ZONE;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""IsGift"" BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""ParentProductId"" INTEGER REFERENCES ""Products""(""Id"") ON DELETE SET NULL;



            -- Bảng Vouchers (Mã giảm giá)
            CREATE TABLE IF NOT EXISTS ""Vouchers"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Code"" VARCHAR(50) NOT NULL,
                ""Name"" VARCHAR(200) NOT NULL,
                ""DiscountType"" VARCHAR(20) NOT NULL DEFAULT 'FIXED',
                ""DiscountValue"" DECIMAL(18,2) NOT NULL DEFAULT 0,
                ""MaxDiscountAmount"" DECIMAL(18,2),
                ""MinOrderAmount"" DECIMAL(18,2) NOT NULL DEFAULT 0,
                ""MaxUsageCount"" INTEGER,
                ""UsedCount"" INTEGER NOT NULL DEFAULT 0,
                ""StartDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""EndDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""IsActive"" BOOLEAN NOT NULL DEFAULT TRUE,
                ""Description"" TEXT,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Vouchers_Code"" ON ""Vouchers"" (""Code"");

            -- Xóa menu Đơn thuê đồ cũ (đã gộp vào Đơn hàng)
            DELETE FROM ""Menus"" WHERE ""ParentId"" IS NULL AND ""Name"" LIKE '%thuê%';

            -- Dọn dẹp cấu hình Google Drive cũ
            DELETE FROM ""SystemSettings"" WHERE ""Key"" IN ('GoogleDrive_FolderId', 'GoogleAppScript_UploadUrl');

            -- Bảng SaleOrders (Đơn mua)
            CREATE TABLE IF NOT EXISTS ""SaleOrders"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Code"" VARCHAR(50) NOT NULL DEFAULT '',
                ""CustomerId"" INTEGER REFERENCES ""Customers""(""Id"") ON DELETE RESTRICT,
                ""SaleDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""TotalPrice"" DECIMAL NOT NULL DEFAULT 0,
                ""DiscountAmount"" DECIMAL NOT NULL DEFAULT 0,
                ""FinalAmount"" DECIMAL NOT NULL DEFAULT 0,
                ""Status"" VARCHAR(20) NOT NULL DEFAULT 'Draft',
                ""Notes"" TEXT,
                ""CreatedByUserId"" INTEGER REFERENCES ""Users""(""Id"") ON DELETE RESTRICT,
                ""VoucherId"" INTEGER REFERENCES ""Vouchers""(""Id"") ON DELETE SET NULL,
                ""AttachmentUrl"" TEXT,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            );

            -- Bảng SaleOrderDetails (Chi tiết đơn mua)
            CREATE TABLE IF NOT EXISTS ""SaleOrderDetails"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""SaleOrderId"" INTEGER NOT NULL REFERENCES ""SaleOrders""(""Id"") ON DELETE CASCADE,
                ""ProductId"" INTEGER NOT NULL REFERENCES ""Products""(""Id"") ON DELETE RESTRICT,
                ""Price"" DECIMAL NOT NULL DEFAULT 0,
                ""Quantity"" INTEGER NOT NULL DEFAULT 1
            );

            -- Thêm cột SaleOrderId vào Transactions và cho phép OrderId NULL
            ALTER TABLE ""Transactions"" ADD COLUMN IF NOT EXISTS ""SaleOrderId"" INTEGER REFERENCES ""SaleOrders""(""Id"") ON DELETE CASCADE;
            ALTER TABLE ""Transactions"" ALTER COLUMN ""OrderId"" DROP NOT NULL;

            -- Thêm cột OrderType vào Orders
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""OrderType"" VARCHAR(20) NOT NULL DEFAULT 'Rental';

            -- Thêm cột GiftProductsJson vào PriceLists
            ALTER TABLE ""PriceLists"" ADD COLUMN IF NOT EXISTS ""GiftProductsJson"" jsonb NOT NULL DEFAULT '[]';

            -- Thêm cột WarningStockLevel vào Products
            ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""WarningStockLevel"" INTEGER NOT NULL DEFAULT 0;

            -- Bảng LiquidationOrders (Đơn thanh lý)
            CREATE TABLE IF NOT EXISTS ""LiquidationOrders"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Code"" VARCHAR(50) NOT NULL DEFAULT '',
                ""LiquidationDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""Status"" VARCHAR(20) NOT NULL DEFAULT 'Completed',
                ""Notes"" TEXT,
                ""CreatedByUserId"" INTEGER REFERENCES ""Users""(""Id"") ON DELETE RESTRICT,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            );

            -- Bảng LiquidationOrderDetails (Chi tiết đơn thanh lý)
            CREATE TABLE IF NOT EXISTS ""LiquidationOrderDetails"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""LiquidationOrderId"" INTEGER NOT NULL REFERENCES ""LiquidationOrders""(""Id"") ON DELETE CASCADE,
                ""ProductId"" INTEGER NOT NULL REFERENCES ""Products""(""Id"") ON DELETE RESTRICT,
                ""Quantity"" INTEGER NOT NULL DEFAULT 1,
                ""Reason"" VARCHAR(500)
            );
        ");

        Console.WriteLine("[DB] Schema migration completed successfully.");
        
        await SeedPermissionsAndMenusAsync(db);
        Console.WriteLine("[DB] Seeding permissions and menus completed successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] Schema migration/seeding warning: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// Sử dụng Session trước Authorization
app.UseSession();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

#pragma warning disable CS8321 // Local function is declared but never used
async Task SeedPermissionsAndMenusAsync(ClothingRentalDbContext db)
{
    // 1. Seed Permissions
    var requiredPerms = new[]
    {
        new Permission { Code = "REPORT_VIEW", Name = "Xem Báo cáo", Type = "UI" },
        new Permission { Code = "REPORT_TRANSACTIONS", Name = "Báo cáo Giao dịch", Type = "UI" },
        new Permission { Code = "REPORT_CLOSED_ORDERS", Name = "Báo cáo Đơn đã đóng", Type = "UI" },
        new Permission { Code = "REPORT_OPEN_ORDERS", Name = "Báo cáo Đơn đang mở", Type = "UI" },
        new Permission { Code = "REPORT_ID_CARDS", Name = "Báo cáo Nhận CCCD", Type = "UI" },
        new Permission { Code = "REPORT_STAFF_REVENUE", Name = "Báo cáo Doanh thu nhân viên", Type = "UI" },
        new Permission { Code = "REPORT_LOW_STOCK", Name = "Báo cáo Cảnh báo tồn kho", Type = "UI" }
    };

    bool needsSave = false;
    foreach (var p in requiredPerms)
    {
        var existing = await db.Permissions.FirstOrDefaultAsync(x => x.Code == p.Code);
        if (existing == null)
        {
            db.Permissions.Add(p);
            needsSave = true;
        }
    }
    if (needsSave)
    {
        await db.SaveChangesAsync();
    }

    // 2. Assign all permissions to Admin users
    var admins = await db.Users.Where(u => u.Role == "Admin").ToListAsync();
    var allPerms = await db.Permissions.ToListAsync();
    foreach (var admin in admins)
    {
        foreach (var perm in allPerms)
        {
            var hasUp = await db.UserPermissions.AnyAsync(up => up.UserId == admin.Id && up.PermissionId == perm.Id);
            if (!hasUp)
            {
                db.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = perm.Id });
                needsSave = true;
            }
        }
    }
    if (needsSave)
    {
        await db.SaveChangesAsync();
    }

    // 3. Seed Menus
    var parentMenu = await db.Menus.FirstOrDefaultAsync(m => m.Name == "Báo cáo thống kê" && m.ParentId == null);
    var reportViewPerm = await db.Permissions.FirstAsync(p => p.Code == "REPORT_VIEW");
    if (parentMenu == null)
    {
        parentMenu = new Menu
        {
            Name = "Báo cáo thống kê",
            Url = "#",
            Icon = "📊",
            DisplayOrder = 90,
            RequiredPermissionId = reportViewPerm.Id
        };
        db.Menus.Add(parentMenu);
        await db.SaveChangesAsync();
    }
    else
    {
        if (parentMenu.Url != "#" || parentMenu.RequiredPermissionId != reportViewPerm.Id)
        {
            parentMenu.Url = "#";
            parentMenu.RequiredPermissionId = reportViewPerm.Id;
            db.Menus.Entry(parentMenu).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }
    }

    // Submenus details
    var subMenusToSeed = new[]
    {
        new { Name = "Tổng quan", Url = "/Reports/Index", Icon = "📊", Code = "REPORT_VIEW", Order = 1 },
        new { Name = "Thống kê giao dịch", Url = "/Reports/Transactions", Icon = "💸", Code = "REPORT_TRANSACTIONS", Order = 2 },
        new { Name = "Doanh thu đơn đã đóng", Url = "/Reports/ClosedOrders", Icon = "🔒", Code = "REPORT_CLOSED_ORDERS", Order = 3 },
        new { Name = "Doanh thu ước tính", Url = "/Reports/OpenOrders", Icon = "🔓", Code = "REPORT_OPEN_ORDERS", Order = 4 },
        new { Name = "Danh sách nhận CCCD", Url = "/Reports/IdCards", Icon = "🪪", Code = "REPORT_ID_CARDS", Order = 5 },
        new { Name = "Hiệu suất nhân viên", Url = "/Reports/StaffRevenue", Icon = "👥", Code = "REPORT_STAFF_REVENUE", Order = 6 },
        new { Name = "Cảnh báo tồn kho", Url = "/Reports/LowStock", Icon = "⚠️", Code = "REPORT_LOW_STOCK", Order = 7 }
    };

    foreach (var sub in subMenusToSeed)
    {
        var subPerm = await db.Permissions.FirstAsync(p => p.Code == sub.Code);
        var existingSub = await db.Menus.FirstOrDefaultAsync(m => m.Url == sub.Url && m.ParentId == parentMenu.Id);
        if (existingSub == null)
        {
            db.Menus.Add(new Menu
            {
                Name = sub.Name,
                Url = sub.Url,
                Icon = sub.Icon,
                ParentId = parentMenu.Id,
                DisplayOrder = sub.Order,
                RequiredPermissionId = subPerm.Id
            });
            needsSave = true;
        }
        else
        {
            if (existingSub.Name != sub.Name || existingSub.Icon != sub.Icon || existingSub.DisplayOrder != sub.Order || existingSub.RequiredPermissionId != subPerm.Id)
            {
                existingSub.Name = sub.Name;
                existingSub.Icon = sub.Icon;
                existingSub.DisplayOrder = sub.Order;
                existingSub.RequiredPermissionId = subPerm.Id;
                db.Menus.Entry(existingSub).State = EntityState.Modified;
                needsSave = true;
            }
        }
    }
    if (needsSave)
    {
        await db.SaveChangesAsync();
    }
}
#pragma warning restore CS8321
