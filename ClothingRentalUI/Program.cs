using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Chỉ truyền qua HTTPS, bảo mật tuyệt đối chống sniff
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

var app = builder.Build();

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
        ");
        Console.WriteLine("[DB] Schema migration completed successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] Schema migration warning: {ex.Message}");
    }
}

// Không còn sử dụng DbSeeder tự động nữa. Khách hàng sẽ tự chạy SQL thủ công khi có thay đổi DB.

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

// Sử dụng Session trước Authorization
app.UseSession();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
