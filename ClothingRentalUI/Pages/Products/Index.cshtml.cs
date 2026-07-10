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
        // Self-healing: Ensure CLOTHES_EDIT and CLOTHES_LOCK exist
        var requiredPerms = new[] 
        { 
            new Permission { Code = "CLOTHES_EDIT", Name = "Sửa Sản phẩm", Type = "UI" },
            new Permission { Code = "CLOTHES_LOCK", Name = "Khóa Sản phẩm", Type = "UI" }
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
            var newPerms = await _context.Permissions.Where(p => p.Code == "CLOTHES_EDIT" || p.Code == "CLOTHES_LOCK").ToListAsync();
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

    public async Task<IActionResult> OnGetRentalHistoryAsync(int productId)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

        var history = await _context.OrderDetails
            .Include(od => od.Order)
            .Where(od => od.ProductId == productId)
            .OrderByDescending(od => od.Order.CreatedDate)
            .Select(od => new
            {
                orderId = od.Order.Code,
                createdAt = od.Order.CreatedDate,
                rentDays = od.RentDays,
                extendedDays = od.ExtendedDays,
                isReturned = od.IsReturned
            })
            .ToListAsync();

        return new JsonResult(new { success = true, history });
    }
}
