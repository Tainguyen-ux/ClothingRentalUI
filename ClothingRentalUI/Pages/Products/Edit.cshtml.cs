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

public class EditModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public EditModel(ClothingRentalDbContext context)
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



    public Product? ProductData { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class ProductInputModel
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
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

        var hasPermission = user.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "CLOTHES_EDIT");
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

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        ProductData = await _context.Products.FindAsync(id);
        if (ProductData == null)
        {
            TempData["ErrorMessage"] = "Sản phẩm không tồn tại.";
            return RedirectToPage("/Products/Index");
        }

        Input = new ProductInputModel
        {
            Id = ProductData.Id,
            Code = ProductData.Code,
            Name = ProductData.Name,
            CategoryId = ProductData.CategoryId,
            PriceListId = ProductData.PriceListId,
            ImportPrice = ProductData.ImportPrice,
            StockQuantity = ProductData.StockQuantity,
            WarningStockLevel = ProductData.WarningStockLevel,
            Color = ProductData.Color,
            Size = ProductData.Size,
            Material = ProductData.Material,
            Condition = ProductData.Condition,
            Description = ProductData.Description
        };

        if (!string.IsNullOrEmpty(ProductData.DynamicAttributes))
        {
            try
            {
                var attrs = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(ProductData.DynamicAttributes);
                if (attrs != null)
                {
                    foreach (var attr in attrs)
                    {
                        if (attr.ContainsKey("key") && attr.ContainsKey("value"))
                        {
                            DynamicAttrs[attr["key"]] = attr["value"];
                        }
                    }
                }
            }
            catch { }
        }

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

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var product = await _context.Products.FindAsync(Input.Id);
            if (product == null) throw new Exception("Không tìm thấy Sản phẩm để cập nhật.");

            if (Input.StockQuantity < product.RentedQuantity)
            {
                return new JsonResult(new { success = false, message = $"Số lượng tồn kho không được nhỏ hơn số lượng đang thuê ({product.RentedQuantity} chiếc)." });
            }

            // Xử lý Dynamic Attributes
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
            product.DynamicAttributes = JsonSerializer.Serialize(attrList);

            // Cập nhật Lịch sử nhập hàng gốc thay vì thêm mới chênh lệch
            int newStock = Input.StockQuantity;
            if (product.StockQuantity != newStock)
            {
                var importHistory = await _context.StockHistories
                    .FirstOrDefaultAsync(h => h.ProductId == product.Id && h.ActionType == "IMPORT");
                
                if (importHistory != null)
                {
                    importHistory.QuantityChange = newStock;
                    importHistory.RemainingTotal = newStock;
                    importHistory.Note = "Nhập kho ban đầu (Đã hiệu chỉnh số lượng)";
                    importHistory.PerformedBy = username;
                }
                else
                {
                    var history = new StockHistory
                    {
                        ProductId = product.Id,
                        ActionType = "IMPORT",
                        QuantityChange = newStock,
                        RemainingTotal = newStock,
                        Note = "Nhập kho ban đầu (Hiệu chỉnh)",
                        PerformedBy = username,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.StockHistories.Add(history);
                }
            }

            // Cập nhật Sản phẩm
            product.Name = Input.Name.Trim();
            product.CategoryId = Input.CategoryId;
            product.PriceListId = Input.PriceListId;
            product.ImportPrice = Input.ImportPrice;
            product.StockQuantity = Input.StockQuantity;
            product.WarningStockLevel = Input.WarningStockLevel;
            product.Color = Input.Color?.Trim();
            product.Size = Input.Size?.Trim();
            product.Material = Input.Material?.Trim();
            product.Condition = Input.Condition?.Trim();
            product.Description = Input.Description?.Trim();
            
            // Nếu tồn kho mới = 0 thì tự khóa
            if (product.StockQuantity == 0 && product.RentedQuantity == 0)
            {
                product.IsAvailable = false;
            }
            else if (product.StockQuantity > 0)
            {
                product.IsAvailable = true;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["SuccessMessage"] = $"Cập nhật thành công sản phẩm: {product.Code}";
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new JsonResult(new { success = false, message = $"Đã xảy ra lỗi: {ex.Message}" });
        }
    }
}
