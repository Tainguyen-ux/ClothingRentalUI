using ClothingRentalUI.Handlers;
using ClothingRentalUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Đăng ký HttpContextAccessor và Session phục vụ việc lưu trữ JWT Token trên Server
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2); // Session sống trong 2 tiếng
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Đăng ký Http Message Handler để tự động đính kèm token vào Header
builder.Services.AddTransient<AuthTokenHandler>();

// Đọc API BaseUrl từ appsettings.json
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "https://api.clothingrental.example.com/api/";

// Đăng ký các HttpClient Services gọi API
builder.Services.AddHttpClient<IAuthService, AuthService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddHttpClient<IClothesService, ClothesService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<AuthTokenHandler>(); // Đính kèm token tự động khi gọi các API Quần áo

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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
