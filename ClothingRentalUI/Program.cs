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

var app = builder.Build();

// Tự động khởi tạo Database và nạp dữ liệu mẫu khi chạy ứng dụng
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ClothingRentalDbContext>();
        DbSeeder.Seed(dbContext);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Đã xảy ra lỗi trong quá trình khởi tạo cơ sở dữ liệu.");
    }
}

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
