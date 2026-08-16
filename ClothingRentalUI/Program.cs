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

            -- Bảng RoleGroups (Nhóm quyền)
            CREATE TABLE IF NOT EXISTS ""RoleGroups"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Name"" VARCHAR(150) NOT NULL,
                ""Description"" TEXT,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            );

            -- Bảng RoleGroupPermissions (Quyền của nhóm quyền)
            CREATE TABLE IF NOT EXISTS ""RoleGroupPermissions"" (
                ""RoleGroupId"" INTEGER NOT NULL REFERENCES ""RoleGroups""(""Id"") ON DELETE CASCADE,
                ""PermissionId"" INTEGER NOT NULL REFERENCES ""Permissions""(""Id"") ON DELETE CASCADE,
                PRIMARY KEY (""RoleGroupId"", ""PermissionId"")
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
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""IsPenaltyPaid"" BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""ConditionAtReceive"" TEXT;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""PricePerDay"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""AddAmt"" DECIMAL NOT NULL DEFAULT 0;
            ALTER TABLE ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""DeductAmt"" DECIMAL NOT NULL DEFAULT 0;

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

            -- Thêm cột RoleGroupId vào Users
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""RoleGroupId"" INTEGER REFERENCES ""RoleGroups""(""Id"") ON DELETE SET NULL;
        ");

        Console.WriteLine("[DB] Schema migration completed successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] Schema migration warning: {ex.Message}");
    }

    try
    {
        await SeedPermissionsAndMenusAsync(db);
        Console.WriteLine("[DB] Seeding permissions and menus completed successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] Seeding permissions and menus warning: {ex.Message}");
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
    // Temporary debug prints
    try
    {
        var allMenus = await db.Menus.Include(m => m.RequiredPermission).ToListAsync();
        foreach (var m in allMenus)
        {
            Console.WriteLine($"[MENU_DEBUG] ID: {m.Id}, Name: {m.Name}, Url: {m.Url}, ParentId: {m.ParentId}, PermissionCode: {m.RequiredPermission?.Code}");
        }
        var allPerms2 = await db.Permissions.ToListAsync();
        foreach (var p in allPerms2)
        {
            Console.WriteLine($"[PERM_DEBUG] Code: {p.Code}, Name: {p.Name}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MENU_DEBUG_ERROR] {ex.Message}");
    }

    // Seed Admin User
    var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == "admin");
    if (adminUser == null)
    {
        adminUser = new User
        {
            Username = "admin",
            PasswordHash = ClothingRentalUI.Helpers.PasswordHasher.HashPassword("admin"),
            Role = "Admin",
            FullName = "Administrator"
        };
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();
    }
    else if (adminUser.PasswordHash == "fakehash")
    {
        adminUser.PasswordHash = ClothingRentalUI.Helpers.PasswordHasher.HashPassword("admin");
        db.Users.Update(adminUser);
        await db.SaveChangesAsync();
    }

    // 1. Seed Permissions
    var requiredPerms = new[]
    {
        // Reports Permissions
        new Permission { Code = "REPORT_VIEW", Name = "Xem Báo cáo", Type = "UI", Description = "Xem trang báo cáo tổng quan" },
        new Permission { Code = "REPORT_TRANSACTIONS", Name = "Báo cáo Giao dịch", Type = "UI", Description = "Xem báo cáo thống kê giao dịch" },
        new Permission { Code = "REPORT_CLOSED_ORDERS", Name = "Báo cáo Đơn đã đóng", Type = "UI", Description = "Xem báo cáo đơn thuê đã đóng" },
        new Permission { Code = "REPORT_OPEN_ORDERS", Name = "Báo cáo Đơn đang mở", Type = "UI", Description = "Xem báo cáo đơn thuê đang mở (Doanh thu đơn chưa đóng)" },
        new Permission { Code = "REPORT_PRODUCT_SALES", Name = "Doanh thu mặt hàng bán", Type = "UI", Description = "Xem báo cáo doanh thu mặt hàng đã bán và tồn kho" },
        new Permission { Code = "REPORT_ID_CARDS", Name = "Báo cáo Nhận CCCD", Type = "UI", Description = "Xem báo cáo lưu giữ CCCD khách hàng" },
        new Permission { Code = "REPORT_STAFF_REVENUE", Name = "Báo cáo Doanh thu nhân viên", Type = "UI", Description = "Xem báo cáo doanh thu theo nhân viên" },
        new Permission { Code = "REPORT_LOW_STOCK", Name = "Báo cáo Cảnh báo tồn kho", Type = "UI", Description = "Xem báo cáo sản phẩm sắp hết hàng" },

        // Products Permissions
        new Permission { Code = "CLOTHES_VIEW", Name = "Xem sản phẩm", Type = "UI", Description = "Xem danh sách sản phẩm" },
        new Permission { Code = "CLOTHES_CREATE", Name = "Thêm sản phẩm", Type = "UI", Description = "Tạo sản phẩm mới hoặc nhập từ Excel" },
        new Permission { Code = "CLOTHES_EDIT", Name = "Sửa sản phẩm", Type = "UI", Description = "Chỉnh sửa thông tin sản phẩm" },
        new Permission { Code = "CLOTHES_LOCK", Name = "Khóa sản phẩm", Type = "UI", Description = "Khóa hoặc mở khóa sản phẩm" },
        new Permission { Code = "CLOTHES_DELETE", Name = "Xóa sản phẩm", Type = "UI", Description = "Xóa sản phẩm khỏi hệ thống" },

        // Categories Permissions
        new Permission { Code = "CATEGORY_VIEW", Name = "Xem loại hàng", Type = "UI", Description = "Xem danh sách loại hàng hóa" },
        new Permission { Code = "CATEGORY_CREATE", Name = "Thêm loại hàng", Type = "UI", Description = "Tạo loại hàng hóa mới" },
        new Permission { Code = "CATEGORY_EDIT", Name = "Sửa loại hàng", Type = "UI", Description = "Chỉnh sửa loại hàng hóa" },
        new Permission { Code = "CATEGORY_LOCK", Name = "Khóa loại hàng", Type = "UI", Description = "Khóa hoặc mở khóa loại hàng hóa" },

        // Attributes Permissions
        new Permission { Code = "PRODUCT_ATTRIBUTE_VIEW", Name = "Xem thuộc tính động", Type = "UI", Description = "Xem danh sách thuộc tính sản phẩm" },
        new Permission { Code = "PRODUCT_ATTRIBUTE_CREATE", Name = "Thêm thuộc tính động", Type = "UI", Description = "Tạo thuộc tính sản phẩm mới" },
        new Permission { Code = "PRODUCT_ATTRIBUTE_EDIT", Name = "Chỉnh sửa thuộc tính động", Type = "UI", Description = "Chỉnh sửa thuộc tính sản phẩm" },
        new Permission { Code = "PRODUCT_ATTRIBUTE_LOCK", Name = "Khóa thuộc tính động", Type = "UI", Description = "Khóa hoặc mở khóa thuộc tính sản phẩm" },

        // PriceLists Permissions
        new Permission { Code = "PRICELIST_VIEW", Name = "Xem loại giá", Type = "UI", Description = "Xem danh sách bảng giá sản phẩm" },
        new Permission { Code = "PRICELIST_CREATE", Name = "Thêm loại giá", Type = "UI", Description = "Tạo bảng giá sản phẩm mới" },
        new Permission { Code = "PRICELIST_EDIT", Name = "Chỉnh sửa loại giá", Type = "UI", Description = "Chỉnh sửa bảng giá sản phẩm" },
        new Permission { Code = "PRICELIST_LOCK", Name = "Khóa loại giá", Type = "UI", Description = "Khóa hoặc mở khóa bảng giá sản phẩm" },
        new Permission { Code = "PRICELIST_DELETE", Name = "Xóa loại giá", Type = "UI", Description = "Xóa bảng giá sản phẩm" },

        // Vouchers Permissions
        new Permission { Code = "VOUCHER_VIEW", Name = "Xem Voucher", Type = "UI", Description = "Xem danh sách mã giảm giá" },
        new Permission { Code = "VOUCHER_CREATE", Name = "Thêm Voucher", Type = "UI", Description = "Tạo mã giảm giá mới" },
        new Permission { Code = "VOUCHER_EDIT", Name = "Sửa Voucher", Type = "UI", Description = "Chỉnh sửa thông tin mã giảm giá" },
        new Permission { Code = "VOUCHER_DELETE", Name = "Xóa Voucher", Type = "UI", Description = "Xóa mã giảm giá" },

        // Import History Permissions
        new Permission { Code = "CLOTHES_IMPORT_HISTORY", Name = "Xem Lịch sử Nhập hàng", Type = "UI", Description = "Xem lịch sử thay đổi kho hàng" },

        // Liquidation Permissions
        new Permission { Code = "CLOTHES_LIQUIDATE", Name = "Thanh lý & Ngừng sử dụng sản phẩm (Legacy)", Type = "UI", Description = "Quyền thanh lý sản phẩm cũ" },
        new Permission { Code = "CLOTHES_LIQUIDATE_VIEW", Name = "Xem lịch sử thanh lý", Type = "UI", Description = "Xem danh sách phiếu thanh lý sản phẩm" },
        new Permission { Code = "CLOTHES_LIQUIDATE_CREATE", Name = "Thực hiện thanh lý", Type = "UI", Description = "Tạo phiếu thanh lý sản phẩm" },
        new Permission { Code = "CLOTHES_LIQUIDATE_CANCEL", Name = "Hủy phiếu thanh lý", Type = "UI", Description = "Hủy phiếu thanh lý sản phẩm và hoàn kho" },

        // User & Settings Permissions
        new Permission { Code = "USER_MANAGEMENT_VIEW", Name = "Quản lý Người dùng", Type = "UI", Description = "Xem và phân quyền tài khoản người dùng" },
        new Permission { Code = "ROLE_GROUP_VIEW", Name = "Xem Nhóm quyền", Type = "UI", Description = "Xem danh sách nhóm quyền mẫu" },
        new Permission { Code = "ROLE_GROUP_CREATE", Name = "Thêm Nhóm quyền", Type = "UI", Description = "Tạo nhóm quyền mẫu mới" },
        new Permission { Code = "ROLE_GROUP_EDIT", Name = "Sửa Nhóm quyền", Type = "UI", Description = "Chỉnh sửa nhóm quyền mẫu" },
        new Permission { Code = "ROLE_GROUP_DELETE", Name = "Xóa Nhóm quyền", Type = "UI", Description = "Xóa nhóm quyền mẫu" },
        new Permission { Code = "SYSTEM_SETTINGS_VIEW", Name = "Cài đặt Hệ thống", Type = "UI", Description = "Xem và cấu hình cài đặt hệ thống" },

        // Orders Permissions (Split Rental vs Sale)
        new Permission { Code = "ORDER_VIEW", Name = "Xem Đơn hàng", Type = "UI", Description = "Xem danh sách các đơn thuê và đơn mua" },
        new Permission { Code = "ORDER_CREATE", Name = "Tạo Đơn thuê", Type = "UI", Description = "Cho phép lập đơn thuê trang phục / đạo cụ" },
        new Permission { Code = "SALE_CREATE", Name = "Tạo Đơn mua (Bán đứt)", Type = "UI", Description = "Cho phép lập đơn bán lẻ / bán đứt sản phẩm" },
        new Permission { Code = "ORDER_DETAIL", Name = "Xem Chi tiết Đơn", Type = "UI", Description = "Xem chi tiết hợp đồng và thông tin đơn hàng" }
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
        else
        {
            if (p.Code == "ORDER_CREATE" && existing.Name == "Tạo Đơn hàng")
            {
                existing.Name = "Tạo Đơn thuê";
                db.Permissions.Entry(existing).State = EntityState.Modified;
                needsSave = true;
            }
        }
    }
    // Adjust Homepage menu item to root /Index instead of /Clothes/Index
    var homepageMenus = await db.Menus.Where(m => m.Url == "/Clothes/Index" || m.Name.Contains("Trang chủ") || m.Name.Contains("Trang chu")).ToListAsync();
    foreach (var menu in homepageMenus)
    {
        if (menu.Url != "/Index")
        {
            menu.Url = "/Index";
            db.Menus.Entry(menu).State = EntityState.Modified;
            needsSave = true;
        }
    }
    if (needsSave)
    {
        await db.SaveChangesAsync();
        needsSave = false;
    }

    // Tự động cấp SALE_CREATE cho bất kỳ user hoặc RoleGroup nào đang có ORDER_CREATE
    var permOrderCreate = await db.Permissions.FirstOrDefaultAsync(p => p.Code == "ORDER_CREATE");
    var permSaleCreate = await db.Permissions.FirstOrDefaultAsync(p => p.Code == "SALE_CREATE");
    if (permOrderCreate != null && permSaleCreate != null)
    {
        var usersWithOrderCreate = await db.UserPermissions
            .Where(up => up.PermissionId == permOrderCreate.Id)
            .Select(up => up.UserId)
            .Distinct()
            .ToListAsync();

        foreach (var uId in usersWithOrderCreate)
        {
            var hasSale = await db.UserPermissions.AnyAsync(up => up.UserId == uId && up.PermissionId == permSaleCreate.Id);
            if (!hasSale)
            {
                db.UserPermissions.Add(new UserPermission { UserId = uId, PermissionId = permSaleCreate.Id });
                needsSave = true;
            }
        }

        var groupsWithOrderCreate = await db.RoleGroupPermissions
            .Where(rgp => rgp.PermissionId == permOrderCreate.Id)
            .Select(rgp => rgp.RoleGroupId)
            .Distinct()
            .ToListAsync();

        foreach (var rgId in groupsWithOrderCreate)
        {
            var hasSale = await db.RoleGroupPermissions.AnyAsync(rgp => rgp.RoleGroupId == rgId && rgp.PermissionId == permSaleCreate.Id);
            if (!hasSale)
            {
                db.RoleGroupPermissions.Add(new RoleGroupPermission { RoleGroupId = rgId, PermissionId = permSaleCreate.Id });
                needsSave = true;
            }
        }

        // Cập nhật Menu "/Orders/SaleCreate" sang quyền SALE_CREATE
        var saleCreateMenuInDb = await db.Menus.FirstOrDefaultAsync(m => m.Url == "/Orders/SaleCreate");
        if (saleCreateMenuInDb != null && saleCreateMenuInDb.RequiredPermissionId != permSaleCreate.Id)
        {
            saleCreateMenuInDb.RequiredPermissionId = permSaleCreate.Id;
            db.Menus.Entry(saleCreateMenuInDb).State = EntityState.Modified;
            needsSave = true;
        }
    }

    if (needsSave)
    {
        await db.SaveChangesAsync();
        needsSave = false;
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
    if (parentMenu == null)
    {
        parentMenu = new Menu
        {
            Name = "Báo cáo thống kê",
            Url = "#",
            Icon = "📊",
            DisplayOrder = 90,
            RequiredPermissionId = null
        };
        db.Menus.Add(parentMenu);
        await db.SaveChangesAsync();
    }
    else
    {
        if (parentMenu.Url != "#" || parentMenu.RequiredPermissionId != null)
        {
            parentMenu.Url = "#";
            parentMenu.RequiredPermissionId = null;
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
        new { Name = "Doanh thu đơn chưa đóng", Url = "/Reports/OpenOrders", Icon = "🔓", Code = "REPORT_OPEN_ORDERS", Order = 4 },
        new { Name = "Doanh thu mặt hàng bán", Url = "/Reports/ProductSales", Icon = "🛍️", Code = "REPORT_PRODUCT_SALES", Order = 5 },
        new { Name = "Danh sách nhận CCCD", Url = "/Reports/IdCards", Icon = "🪪", Code = "REPORT_ID_CARDS", Order = 6 },
        new { Name = "Doanh thu nhân viên", Url = "/Reports/StaffRevenue", Icon = "👥", Code = "REPORT_STAFF_REVENUE", Order = 7 },
        new { Name = "Cảnh báo tồn kho", Url = "/Reports/LowStock", Icon = "⚠️", Code = "REPORT_LOW_STOCK", Order = 8 }
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
        needsSave = false;
    }

    // 4. Seed "Hàng hoá" Parent and Submenus
    var productParentMenu = await db.Menus.FirstOrDefaultAsync(m => m.Name == "Hàng hoá" && m.ParentId == null);
    var clothesViewPerm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == "CLOTHES_VIEW");
    if (productParentMenu == null)
    {
        productParentMenu = new Menu
        {
            Name = "Hàng hoá",
            Url = "#",
            Icon = "👕",
            DisplayOrder = 20,
            RequiredPermissionId = clothesViewPerm?.Id
        };
        db.Menus.Add(productParentMenu);
        await db.SaveChangesAsync();
    }
    else
    {
        if (productParentMenu.Url != "#" || productParentMenu.RequiredPermissionId != clothesViewPerm?.Id)
        {
            productParentMenu.Url = "#";
            productParentMenu.RequiredPermissionId = clothesViewPerm?.Id;
            db.Menus.Entry(productParentMenu).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }
    }

    var productSubMenus = new[]
    {
        new { Name = "Danh sách sản phẩm", Url = "/Products/Index", Icon = "📋", Code = "CLOTHES_VIEW", Order = 1 },
        new { Name = "Thuộc tính sản phẩm", Url = "/Products/Attributes", Icon = "⚙️", Code = "PRODUCT_ATTRIBUTE_VIEW", Order = 2 },
        new { Name = "Quản lý loại hàng", Url = "/Products/Categories", Icon = "🏷️", Code = "CATEGORY_VIEW", Order = 3 },
        new { Name = "Quản lý loại giá", Url = "/Products/PriceLists", Icon = "💰", Code = "PRICELIST_VIEW", Order = 4 },
        new { Name = "Lịch sử nhập hàng", Url = "/Products/ImportHistory", Icon = "🕒", Code = "CLOTHES_IMPORT_HISTORY", Order = 5 },
        new { Name = "Mã giảm giá", Url = "/Products/Vouchers", Icon = "🎟️", Code = "VOUCHER_VIEW", Order = 6 },
        new { Name = "Thanh lý & Ngừng dùng", Url = "/Products/Liquidate", Icon = "♻️", Code = "CLOTHES_LIQUIDATE_VIEW", Order = 7 }
    };

    foreach (var sub in productSubMenus)
    {
        var subPerm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == sub.Code);
        if (subPerm == null) continue;

        var existingSub = await db.Menus.FirstOrDefaultAsync(m => m.Url == sub.Url && m.ParentId == productParentMenu.Id);
        if (existingSub == null)
        {
            db.Menus.Add(new Menu
            {
                Name = sub.Name,
                Url = sub.Url,
                Icon = sub.Icon,
                ParentId = productParentMenu.Id,
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
        needsSave = false;
    }

    // 5. Seed default RoleGroups if empty
    if (!await db.RoleGroups.AnyAsync())
    {
        var allPermissionsList = await db.Permissions.ToListAsync();

        // 1. Quản trị viên (Toàn quyền)
        var adminGroup = new RoleGroup
        {
            Name = "Quản trị viên (Toàn quyền)",
            Description = "Toàn quyền quản trị tất cả các chức năng, báo cáo và cài đặt hệ thống.",
            CreatedAt = DateTime.UtcNow
        };
        db.RoleGroups.Add(adminGroup);
        await db.SaveChangesAsync();
        foreach (var p in allPermissionsList)
        {
            db.RoleGroupPermissions.Add(new RoleGroupPermission { RoleGroupId = adminGroup.Id, PermissionId = p.Id });
        }

        // 2. Nhân viên Thu ngân & Bán hàng
        var staffGroup = new RoleGroup
        {
            Name = "Nhân viên Thu ngân & Bán hàng",
            Description = "Dành cho nhân viên bán hàng, lập đơn thuê, trả đồ, áp mã giảm giá và xem danh sách sản phẩm.",
            CreatedAt = DateTime.UtcNow
        };
        db.RoleGroups.Add(staffGroup);
        await db.SaveChangesAsync();
        var staffPermCodes = new HashSet<string> {
            "CLOTHES_VIEW", "CATEGORY_VIEW", "PRICELIST_VIEW", "PRODUCT_ATTRIBUTE_VIEW",
            "VOUCHER_VIEW", "CLOTHES_IMPORT_HISTORY"
        };
        foreach (var p in allPermissionsList.Where(p => staffPermCodes.Contains(p.Code)))
        {
            db.RoleGroupPermissions.Add(new RoleGroupPermission { RoleGroupId = staffGroup.Id, PermissionId = p.Id });
        }

        // 3. Thủ kho & Quản lý hàng hóa
        var warehouseGroup = new RoleGroup
        {
            Name = "Thủ kho & Quản lý hàng hóa",
            Description = "Dành cho thủ kho phụ trách nhập hàng, quản lý danh mục, bảng giá, thuộc tính và thanh lý hàng cũ/hỏng.",
            CreatedAt = DateTime.UtcNow
        };
        db.RoleGroups.Add(warehouseGroup);
        await db.SaveChangesAsync();
        var warehousePermCodes = new HashSet<string> {
            "CLOTHES_VIEW", "CLOTHES_CREATE", "CLOTHES_EDIT", "CLOTHES_LOCK", "CLOTHES_DELETE",
            "CATEGORY_VIEW", "CATEGORY_CREATE", "CATEGORY_EDIT", "CATEGORY_LOCK",
            "PRODUCT_ATTRIBUTE_VIEW", "PRODUCT_ATTRIBUTE_CREATE", "PRODUCT_ATTRIBUTE_EDIT", "PRODUCT_ATTRIBUTE_LOCK",
            "PRICELIST_VIEW", "PRICELIST_CREATE", "PRICELIST_EDIT", "PRICELIST_LOCK", "PRICELIST_DELETE",
            "CLOTHES_IMPORT_HISTORY", "CLOTHES_LIQUIDATE_VIEW", "CLOTHES_LIQUIDATE_CREATE", "CLOTHES_LIQUIDATE_CANCEL"
        };
        foreach (var p in allPermissionsList.Where(p => warehousePermCodes.Contains(p.Code)))
        {
            db.RoleGroupPermissions.Add(new RoleGroupPermission { RoleGroupId = warehouseGroup.Id, PermissionId = p.Id });
        }

        // 4. Kế toán & Báo cáo
        var accountantGroup = new RoleGroup
        {
            Name = "Kế toán & Báo cáo",
            Description = "Dành cho kế toán theo dõi toàn bộ báo cáo doanh thu, dòng tiền, công nợ, đơn hàng và tồn kho.",
            CreatedAt = DateTime.UtcNow
        };
        db.RoleGroups.Add(accountantGroup);
        await db.SaveChangesAsync();
        var accountantPermCodes = new HashSet<string> {
            "REPORT_VIEW", "REPORT_TRANSACTIONS", "REPORT_CLOSED_ORDERS", "REPORT_OPEN_ORDERS",
            "REPORT_PRODUCT_SALES", "REPORT_ID_CARDS", "REPORT_STAFF_REVENUE", "REPORT_LOW_STOCK",
            "VOUCHER_VIEW"
        };
        foreach (var p in allPermissionsList.Where(p => accountantPermCodes.Contains(p.Code)))
        {
            db.RoleGroupPermissions.Add(new RoleGroupPermission { RoleGroupId = accountantGroup.Id, PermissionId = p.Id });
        }

        await db.SaveChangesAsync();
    }

    // 6. Seed "Cài đặt hệ thống" Parent and Submenus
    var settingsParentMenu = await db.Menus.FirstOrDefaultAsync(m => (m.Name == "Cài đặt hệ thống" || m.Name == "Cấu hình hệ thống") && m.ParentId == null);
    var userMgmtPerm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == "USER_MANAGEMENT_VIEW");
    if (settingsParentMenu == null)
    {
        settingsParentMenu = new Menu
        {
            Name = "Cài đặt hệ thống",
            Url = "#",
            Icon = "⚙️",
            DisplayOrder = 99,
            RequiredPermissionId = userMgmtPerm?.Id
        };
        db.Menus.Add(settingsParentMenu);
        await db.SaveChangesAsync();
    }
    else
    {
        if (settingsParentMenu.Name != "Cài đặt hệ thống" || settingsParentMenu.Url != "#" || settingsParentMenu.DisplayOrder != 99 || settingsParentMenu.RequiredPermissionId != userMgmtPerm?.Id)
        {
            settingsParentMenu.Name = "Cài đặt hệ thống";
            settingsParentMenu.Url = "#";
            settingsParentMenu.DisplayOrder = 99;
            settingsParentMenu.RequiredPermissionId = userMgmtPerm?.Id;
            db.Menus.Entry(settingsParentMenu).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }
    }

    // Dọn dẹp bất kỳ menu cha nào khác trùng lặp liên quan đến Cài đặt/Cấu hình hoặc trỏ vào /Settings/Users
    var duplicateSettingsMenus = await db.Menus
        .Where(m => m.ParentId == null && m.Id != settingsParentMenu.Id && 
                   (m.Name.Contains("Cấu hình") || m.Name.Contains("Cài đặt") || m.Url == "/Settings/Users" || m.Url == "/Settings/SystemSettings" || m.Url == "/Settings/RoleGroups"))
        .ToListAsync();

    foreach (var dup in duplicateSettingsMenus)
    {
        var orphanChildren = await db.Menus.Where(m => m.ParentId == dup.Id).ToListAsync();
        foreach (var child in orphanChildren)
        {
            child.ParentId = settingsParentMenu.Id;
        }
        db.Menus.Remove(dup);
        needsSave = true;
    }

    var settingsSubMenus = new[]
    {
        new { Name = "Quản lý người dùng", Url = "/Settings/Users", Icon = "👥", Code = "USER_MANAGEMENT_VIEW", Order = 1 },
        new { Name = "Nhóm quyền & Mẫu", Url = "/Settings/RoleGroups", Icon = "🛡️", Code = "ROLE_GROUP_VIEW", Order = 2 },
        new { Name = "Quản lý Menu & Quyền", Url = "/Settings/MenuManager", Icon = "📑", Code = "USER_MANAGEMENT_VIEW", Order = 3 },
        new { Name = "Cấu hình chung", Url = "/Settings/SystemSettings", Icon = "🛠️", Code = "SYSTEM_SETTINGS_VIEW", Order = 4 }
    };

    foreach (var sub in settingsSubMenus)
    {
        var subPerm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == sub.Code);
        if (subPerm == null) continue;

        var existingSub = await db.Menus.FirstOrDefaultAsync(m => m.Url == sub.Url && m.ParentId == settingsParentMenu.Id);
        if (existingSub == null)
        {
            db.Menus.Add(new Menu
            {
                Name = sub.Name,
                Url = sub.Url,
                Icon = sub.Icon,
                ParentId = settingsParentMenu.Id,
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
        needsSave = false;
    }

    // Tự động xóa các bản ghi menu bị duplicate trong Database
    var allDbMenus = await db.Menus.ToListAsync();
    var duplicateGroups = allDbMenus
        .GroupBy(m => new { m.ParentId, Key = m.Url == "#" ? m.Name.Trim().ToLower() : m.Url.Trim().ToLower() })
        .Where(g => g.Count() > 1);

    foreach (var group in duplicateGroups)
    {
        var toKeep = group.OrderBy(m => m.Id).First();
        var toRemove = group.Where(m => m.Id != toKeep.Id).ToList();
        foreach (var dup in toRemove)
        {
            var subChildren = allDbMenus.Where(m => m.ParentId == dup.Id).ToList();
            foreach (var sc in subChildren)
            {
                sc.ParentId = toKeep.Id;
            }
            db.Menus.Remove(dup);
            needsSave = true;
        }
    }

    if (needsSave)
    {
        await db.SaveChangesAsync();
    }
}
#pragma warning restore CS8321
