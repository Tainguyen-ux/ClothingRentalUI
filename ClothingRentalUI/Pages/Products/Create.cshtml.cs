using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Pages.Products;

public class CreateModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public CreateModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public ProductInputModel Input { get; set; } = new ProductInputModel();

    [BindProperty]
    public Dictionary<string, string> DynamicAttrs { get; set; } = new Dictionary<string, string>();

    public IList<SelectListItem> Categories { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> PriceLists { get; set; } = new List<SelectListItem>();
    public IList<ProductAttribute> ActiveAttributes { get; set; } = new List<ProductAttribute>();

    public string UploadUrl { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;

    [TempData]
    public string? ErrorMessage { get; set; }

    public class ProductInputModel
    {
        public string Name { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public int PriceListId { get; set; }
        public decimal ImportPrice { get; set; }
        public int StockQuantity { get; set; }
        public string? ImageUrl { get; set; }
        public string? Color { get; set; }
        public string? Size { get; set; }
        public string? Material { get; set; }
        public string? Condition { get; set; }
        public string? Description { get; set; }
    }

    private async Task<IActionResult?> VerifyAccessAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "CLOTHES_CREATE"));

        if (!hasPermission)
        {
            return RedirectToPage("/Products/Index");
        }
        return null;
    }

    private async Task LoadDropdownsAsync()
    {
        Categories = await _context.Categories
            .Where(c => c.IsActive)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = $"{c.Name} ({c.CodePrefix})" })
            .ToListAsync();

        PriceLists = await _context.PriceLists
            .Where(p => p.IsActive)
            .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name })
            .ToListAsync();

        ActiveAttributes = await _context.ProductAttributes
            .Where(a => a.IsActive)
            .ToListAsync();

        var uploadUrlSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "GoogleAppScript_UploadUrl");
        if (uploadUrlSetting != null && !string.IsNullOrEmpty(uploadUrlSetting.ValueJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(uploadUrlSetting.ValueJson);
                if (parsed != null && parsed.ContainsKey("value")) UploadUrl = parsed["value"];
            }
            catch {}
        }

        var folderIdSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "GoogleDrive_FolderId");
        if (folderIdSetting != null && !string.IsNullOrEmpty(folderIdSetting.ValueJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(folderIdSetting.ValueJson);
                if (parsed != null && parsed.ContainsKey("value")) FolderId = parsed["value"];
            }
            catch {}
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        await LoadDropdownsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var username = HttpContext.Session.GetString("Username") ?? "system";

        if (Input.CategoryId == 0 || Input.PriceListId == 0 || string.IsNullOrWhiteSpace(Input.Name))
        {
            ErrorMessage = "Vui lòng nhập đầy đủ các trường bắt buộc (Tên, Loại hàng, Loại giá).";
            await LoadDropdownsAsync();
            return Page();
        }

        if (Input.StockQuantity < 0)
        {
            ErrorMessage = "Số lượng tồn kho không hợp lệ.";
            await LoadDropdownsAsync();
            return Page();
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Sinh Mã Sản Phẩm
            var category = await _context.Categories.FindAsync(Input.CategoryId);
            if (category == null) throw new Exception("Không tìm thấy Loại hàng.");

            string todayStr = DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd"); // Giả định múi giờ VN +7
            string prefix = category.CodePrefix;

            // Đếm số sản phẩm trong ngày có cùng prefix
            var countToday = await _context.Products
                .Where(p => p.Code.StartsWith(prefix + todayStr))
                .CountAsync();

            string generatedCode = $"{prefix}{todayStr}{(countToday + 1):D4}";

            // 2. Xử lý Dynamic Attributes JSON
            var attrList = new List<object>();
            var allAttrs = await _context.ProductAttributes.Where(a => a.IsActive).ToListAsync();
            foreach (var key in DynamicAttrs.Keys)
            {
                if (!string.IsNullOrWhiteSpace(DynamicAttrs[key]))
                {
                    var definition = allAttrs.FirstOrDefault(a => a.Key == key);
                    if (definition != null)
                    {
                        attrList.Add(new
                        {
                            key = definition.Key,
                            display = definition.DisplayName,
                            value = DynamicAttrs[key].Trim()
                        });
                    }
                }
            }
            string dynamicAttrsJson = JsonSerializer.Serialize(attrList);

            // 3. Khởi tạo và Lưu Sản phẩm
            var product = new Product
            {
                Code = generatedCode,
                Name = Input.Name.Trim(),
                CategoryId = Input.CategoryId,
                PriceListId = Input.PriceListId,
                ImportPrice = Input.ImportPrice,
                StockQuantity = Input.StockQuantity,
                RentedQuantity = 0,
                ImageUrl = string.IsNullOrWhiteSpace(Input.ImageUrl) ? "https://via.placeholder.com/600x800.png?text=No+Image" : Input.ImageUrl.Trim(),
                Color = Input.Color?.Trim(),
                Size = Input.Size?.Trim(),
                Material = Input.Material?.Trim(),
                Condition = Input.Condition?.Trim(),
                Description = Input.Description?.Trim(),
                DynamicAttributes = dynamicAttrsJson,
                IsAvailable = Input.StockQuantity > 0,
                IsLiquidated = false,
                TotalRentRevenue = 0
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            // 4. Đồng bộ StockHistory
            if (Input.StockQuantity > 0)
            {
                var history = new StockHistory
                {
                    ProductId = product.Id,
                    ActionType = "IMPORT",
                    QuantityChange = Input.StockQuantity,
                    RemainingTotal = Input.StockQuantity,
                    Note = "Nhập kho ban đầu khi tạo mới sản phẩm",
                    PerformedBy = username,
                    CreatedAt = DateTime.UtcNow
                };
                _context.StockHistories.Add(history);
                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] = $"Tạo thành công sản phẩm: {generatedCode}";
            return RedirectToPage("/Products/Index");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Đã xảy ra lỗi: {ex.Message}";
            await LoadDropdownsAsync();
            return Page();
        }
    }
}
