using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Services;

namespace ClothingRentalUI.Pages.Products;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public IList<Product> Products { get; set; } = new List<Product>();
    public IList<Category> Categories { get; set; } = new List<Category>();

    public class BarcodeConfigData
    {
        public int Width { get; set; } = 2;
        public int Height { get; set; } = 60;
        public int FontSize { get; set; } = 16;
    }
    public BarcodeConfigData BarcodeConfig { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 10;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CategoryId { get; set; }



    public List<string> CurrentUserPermissions { get; set; } = new();
    public bool IsAdmin { get; set; } = false;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync(string requiredPermission = "CLOTHES_VIEW")
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null) return RedirectToPage("/Auth/Login");

        IsAdmin = user.Role == "Admin";
        CurrentUserPermissions = user.UserPermissions
            .Where(up => up.Permission != null)
            .Select(up => up.Permission!.Code)
            .ToList();

        if (!IsAdmin && !CurrentUserPermissions.Contains(requiredPermission))
        {
            return RedirectToPage("/Home/Index"); // Assuming a generic fallback
        }
        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Self-healing: Ensure CLOTHES_EDIT, CLOTHES_LOCK, CLOTHES_DELETE exist
        var requiredPerms = new[] 
        { 
            new Permission { Code = "CLOTHES_EDIT", Name = "Sửa Sản phẩm", Type = "UI" },
            new Permission { Code = "CLOTHES_LOCK", Name = "Khóa Sản phẩm", Type = "UI" },
            new Permission { Code = "CLOTHES_DELETE", Name = "Xóa Sản phẩm", Type = "UI" }
        };
        
        bool needsSave = false;
        var existingPerms = await _context.Permissions.Select(p => p.Code).ToListAsync();
        foreach (var p in requiredPerms)
        {
            if (!existingPerms.Contains(p.Code))
            {
                _context.Permissions.Add(p);
                needsSave = true;
            }
        }
        if (needsSave)
        {
            await _context.SaveChangesAsync();
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            var newPerms = await _context.Permissions.Where(p => p.Code == "CLOTHES_EDIT" || p.Code == "CLOTHES_LOCK" || p.Code == "CLOTHES_DELETE").ToListAsync();
            foreach (var admin in admins)
            {
                foreach (var np in newPerms)
                {
                    if (!await _context.UserPermissions.AnyAsync(up => up.UserId == admin.Id && up.PermissionId == np.Id))
                    {
                        _context.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = np.Id });
                    }
                }
            }
            await _context.SaveChangesAsync();
        }

        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        // Load Barcode Config
        var wStr = await _context.SystemSettings.Where(s => s.Key == "Barcode_Width").Select(s => s.ValueJson).FirstOrDefaultAsync();
        var hStr = await _context.SystemSettings.Where(s => s.Key == "Barcode_Height").Select(s => s.ValueJson).FirstOrDefaultAsync();
        var fsStr = await _context.SystemSettings.Where(s => s.Key == "Barcode_FontSize").Select(s => s.ValueJson).FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(wStr))
        {
            try { var obj = System.Text.Json.JsonSerializer.Deserialize<ClothingRentalUI.Pages.Settings.SystemSettingsModel.StandardSettingJson>(wStr); if (obj != null && int.TryParse(obj.value, out int w)) BarcodeConfig.Width = w; } catch {}
        }
        if (!string.IsNullOrEmpty(hStr))
        {
            try { var obj = System.Text.Json.JsonSerializer.Deserialize<ClothingRentalUI.Pages.Settings.SystemSettingsModel.StandardSettingJson>(hStr); if (obj != null && int.TryParse(obj.value, out int h)) BarcodeConfig.Height = h; } catch {}
        }
        if (!string.IsNullOrEmpty(fsStr))
        {
            try { var obj = System.Text.Json.JsonSerializer.Deserialize<ClothingRentalUI.Pages.Settings.SystemSettingsModel.StandardSettingJson>(fsStr); if (obj != null && int.TryParse(obj.value, out int fs)) BarcodeConfig.FontSize = fs; } catch {}
        }



        Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();

        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.PriceList)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            query = query.Where(p => p.Name.ToLower().Contains(SearchTerm.ToLower()) || p.Code.ToLower().Contains(SearchTerm.ToLower()));
        }

        if (CategoryId.HasValue && CategoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == CategoryId.Value);
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Products = await query
            .OrderByDescending(p => p.Id)
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("CLOTHES_LOCK");
        if (authCheck != null) return authCheck;

        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            ErrorMessage = "Không tìm thấy sản phẩm.";
            return RedirectToPage();
        }

        product.IsAvailable = !product.IsAvailable;
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã {(product.IsAvailable ? "mở khóa" : "tạm khóa")} sản phẩm thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("CLOTHES_DELETE");
        if (authCheck != null) return authCheck;

        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            ErrorMessage = "Không tìm thấy sản phẩm.";
            return RedirectToPage();
        }

        var hasOrders = await _context.OrderDetails.AnyAsync(od => od.ProductId == id);
        if (hasOrders)
        {
            ErrorMessage = "Không thể xóa sản phẩm đã có lịch sử thuê. Bạn chỉ có thể Khóa sản phẩm này.";
            return RedirectToPage();
        }

        var histories = await _context.StockHistories.Where(h => h.ProductId == id).ToListAsync();
        _context.StockHistories.RemoveRange(histories);
        _context.Products.Remove(product);
        
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã xóa sản phẩm {product.Code} khỏi hệ thống.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetRentalHistoryAsync(int productId, int pageIndex = 1, string? startDate = null, string? endDate = null)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

        var query = _context.OrderDetails
            .Include(od => od.Order)
            .ThenInclude(o => o!.Customer)
            .Where(od => od.ProductId == productId);

        if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var startVal))
        {
            var startUtc = DateTime.SpecifyKind(startVal.Date, DateTimeKind.Utc);
            query = query.Where(od => od.Order != null && od.Order.CreatedAt >= startUtc);
        }

        if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var endVal))
        {
            var endUtc = DateTime.SpecifyKind(endVal.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(od => od.Order != null && od.Order.CreatedAt <= endUtc);
        }

        var totalItems = await query.CountAsync();
        int pageSize = 5;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (pageIndex < 1) pageIndex = 1;
        if (totalPages > 0 && pageIndex > totalPages) pageIndex = totalPages;

        var history = await query
            .OrderByDescending(od => od.Order != null ? od.Order.CreatedAt : DateTime.MinValue)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(od => new
            {
                orderId = od.Order != null ? od.Order.Code : "",
                customerName = od.Order != null && od.Order.Customer != null ? od.Order.Customer.FullName : "Khách vãng lai",
                createdAt = od.Order != null ? od.Order.CreatedAt : DateTime.MinValue,
                rentDays = od.RentDays,
                extendedDays = od.ExtendedDays,
                isReturned = od.IsReturned
            })
            .ToListAsync();

        return new JsonResult(new { success = true, history, pageIndex, totalPages, totalItems });
    }

    public class UpdateImageRequest
    {
        public int ProductId { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostUpdateImageAjaxAsync([FromBody] UpdateImageRequest request)
    {
        var authCheck = await VerifyAccessAsync("CLOTHES_EDIT");
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền chỉnh sửa sản phẩm." });

        if (request == null || request.ProductId <= 0)
            return new JsonResult(new { success = false, message = "Dữ liệu không hợp lệ." });

        var product = await _context.Products.FindAsync(request.ProductId);
        if (product == null)
            return new JsonResult(new { success = false, message = "Không tìm thấy sản phẩm." });

        product.ImageUrl = request.Url;
        await _context.SaveChangesAsync();

        return new JsonResult(new { success = true, message = "Cập nhật hình ảnh thành công." });
    }

    public static string GetDirectGoogleDriveImageUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        
        string targetUrl = url;
        // Check if it's a JSON array
        if (url.Trim().StartsWith("[") && url.Trim().EndsWith("]"))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(url);
                if (list != null && list.Count > 0)
                {
                    targetUrl = list[0];
                }
                else
                {
                    return string.Empty;
                }
            }
            catch
            {
                // Fallback to single url
            }
        }

        if (string.IsNullOrEmpty(targetUrl)) return string.Empty;
        if (targetUrl.StartsWith("/") || (targetUrl.StartsWith("http") && !targetUrl.Contains("drive.google.com")))
        {
            return targetUrl;
        }

        if (targetUrl.Contains("lh3.googleusercontent.com")) return targetUrl;

        // Match /file/d/FILE_ID
        var match1 = System.Text.RegularExpressions.Regex.Match(targetUrl, @"/file/d/([a-zA-Z0-9_-]+)");
        if (match1.Success && match1.Groups.Count > 1)
        {
            return $"https://lh3.googleusercontent.com/d/{match1.Groups[1].Value}";
        }

        // Match id=FILE_ID
        var match2 = System.Text.RegularExpressions.Regex.Match(targetUrl, @"[?&]id=([a-zA-Z0-9_-]+)");
        if (match2.Success && match2.Groups.Count > 1)
        {
            return $"https://lh3.googleusercontent.com/d/{match2.Groups[1].Value}";
        }

        return targetUrl;
    }

    public async Task<IActionResult> OnPostUploadLocalImageAsync(IFormFile file)
    {
        var authCheck = await VerifyAccessAsync("CLOTHES_EDIT");
        if (authCheck != null) return new JsonResult(new { success = false, error = "Không có quyền truy cập." });

        if (file == null || file.Length == 0)
        {
            return new JsonResult(new { success = false, error = "Tệp tin không hợp lệ." });
        }

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        var ext = System.IO.Path.GetExtension(file.FileName).ToLower();
        if (!allowedExtensions.Contains(ext))
        {
            return new JsonResult(new { success = false, error = "Chỉ cho phép tải lên hình ảnh (.jpg, .jpeg, .png, .webp, .gif)" });
        }

        try
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var relativeUrl = $"/uploads/{uniqueFileName}";
            return new JsonResult(new { success = true, url = relativeUrl });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = $"Lỗi khi lưu tệp tin: {ex.Message}" });
        }
    }
}
