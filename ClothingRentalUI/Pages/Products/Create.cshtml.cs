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

    [TempData]
    public string? ErrorMessage { get; set; }

    public class ProductInputModel
    {
        public string Name { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public int PriceListId { get; set; }
        public decimal ImportPrice { get; set; }
        public int StockQuantity { get; set; }
        public int WarningStockLevel { get; set; } // Ngưỡng cảnh báo tồn kho
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

        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null || user.IsLocked) return RedirectToPage("/Auth/Login");
        if (user.Role == "Admin") return null;

        var hasPermission = user.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "CLOTHES_CREATE");
        if (!hasPermission)
        {
            return RedirectToPage("/Index");
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
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        await LoadDropdownsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAjaxAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

        var username = HttpContext.Session.GetString("Username") ?? "system";

        if (Input.CategoryId == 0 || Input.PriceListId == 0 || string.IsNullOrWhiteSpace(Input.Name))
        {
            return new JsonResult(new { success = false, message = "Vui lòng nhập đầy đủ các trường bắt buộc (Tên, Loại hàng, Loại giá)." });
        }

        if (Input.StockQuantity < 0)
        {
            return new JsonResult(new { success = false, message = "Số lượng tồn kho không hợp lệ." });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Sinh Mã Sản Phẩm: Tiếp đầu ngữ + STT(tăng dần chung toàn hệ thống)
            var category = await _context.Categories.FindAsync(Input.CategoryId);
            if (category == null) throw new Exception("Không tìm thấy Loại hàng.");

            string prefix = category.CodePrefix;

            var allCategories = await _context.Categories
                .Select(c => c.CodePrefix)
                .ToListAsync();
            var sortedPrefixes = allCategories.OrderByDescending(pfx => pfx.Length).ToList();

            var allCodes = await _context.Products
                .Select(p => p.Code)
                .ToListAsync();

            int maxSeq = 0;
            foreach (var code in allCodes)
            {
                var matchedPrefix = sortedPrefixes.FirstOrDefault(pfx => code.StartsWith(pfx, StringComparison.OrdinalIgnoreCase));
                if (matchedPrefix != null)
                {
                    var suffix = code.Substring(matchedPrefix.Length);
                    if (suffix.Length >= 1 && suffix.Length <= 6 && int.TryParse(suffix, out int seq))
                    {
                        if (seq > maxSeq)
                        {
                            maxSeq = seq;
                        }
                    }
                }
            }

            int nextSeq = maxSeq + 1;
            string generatedCode = $"{prefix}{nextSeq:D4}";

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
                WarningStockLevel = Input.WarningStockLevel,
                ImageUrl = string.Empty,
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
            return new JsonResult(new { success = true, productId = product.Id, code = generatedCode });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new JsonResult(new { success = false, message = $"Đã xảy ra lỗi: {ex.Message}" });
        }
    }

    public class UpdateImagesRequest
    {
        public int ProductId { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostUpdateImagesAjaxAsync([FromBody] UpdateImagesRequest request)
    {
        try
        {
            var authCheck = await VerifyAccessAsync();
            if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

            if (request == null || request.ProductId <= 0)
                return new JsonResult(new { success = false, message = "Dữ liệu không hợp lệ." });

            var product = await _context.Products.FindAsync(request.ProductId);
            if (product == null)
                return new JsonResult(new { success = false, message = "Không tìm thấy sản phẩm." });

            product.ImageUrl = request.Url;
            await _context.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"Lỗi máy chủ: {ex.Message}" });
        }
    }

    public async Task<IActionResult> OnPostUploadLocalImageAsync(IFormFile file)
    {
        try
        {
            var authCheck = await VerifyAccessAsync();
            if (authCheck != null) return new JsonResult(new { success = false, error = "Không có quyền truy cập." });

            if (file == null || file.Length == 0)
            {
                return new JsonResult(new { success = false, error = "Tệp tin không hợp lệ." });
            }

            var ext = System.IO.Path.GetExtension(file.FileName).ToLower();

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
