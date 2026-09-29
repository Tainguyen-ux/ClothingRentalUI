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
using ClothingRentalUI.Models.Clothes;
using ClothingRentalUI.Helpers;

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

            // Lưu vết các thay đổi thuộc tính (Audit Log)
            var changes = new List<ProductFieldChange>();

            var oldCategory = await _context.Categories.FindAsync(product.CategoryId);
            var newCategory = await _context.Categories.FindAsync(Input.CategoryId);
            var oldPriceList = await _context.PriceLists.FindAsync(product.PriceListId);
            var newPriceList = await _context.PriceLists.FindAsync(Input.PriceListId);

            if (product.Name.Trim() != Input.Name.Trim())
            {
                changes.Add(new ProductFieldChange { Field = "Name", DisplayName = "Tên sản phẩm", OldValue = product.Name, NewValue = Input.Name.Trim() });
            }
            if (product.CategoryId != Input.CategoryId)
            {
                changes.Add(new ProductFieldChange { Field = "CategoryId", DisplayName = "Loại hàng", OldValue = oldCategory != null ? $"{oldCategory.Name} ({oldCategory.CodePrefix})" : product.CategoryId.ToString(), NewValue = newCategory != null ? $"{newCategory.Name} ({newCategory.CodePrefix})" : Input.CategoryId.ToString() });
            }
            if (product.PriceListId != Input.PriceListId)
            {
                changes.Add(new ProductFieldChange { Field = "PriceListId", DisplayName = "Bảng giá thuê", OldValue = oldPriceList != null ? $"{oldPriceList.Name} ({oldPriceList.PricePerDay:N0}đ)" : product.PriceListId.ToString(), NewValue = newPriceList != null ? $"{newPriceList.Name} ({newPriceList.PricePerDay:N0}đ)" : Input.PriceListId.ToString() });
            }
            if (product.ImportPrice != Input.ImportPrice)
            {
                changes.Add(new ProductFieldChange { Field = "ImportPrice", DisplayName = "Giá nhập", OldValue = ProductAuditHelper.FormatCurrency(product.ImportPrice), NewValue = ProductAuditHelper.FormatCurrency(Input.ImportPrice) });
            }
            if (product.StockQuantity != Input.StockQuantity)
            {
                changes.Add(new ProductFieldChange { Field = "StockQuantity", DisplayName = "Số lượng tồn kho", OldValue = $"{product.StockQuantity} chiếc", NewValue = $"{Input.StockQuantity} chiếc" });
            }
            if (product.WarningStockLevel != Input.WarningStockLevel)
            {
                changes.Add(new ProductFieldChange { Field = "WarningStockLevel", DisplayName = "Ngưỡng cảnh báo tồn", OldValue = $"{product.WarningStockLevel}", NewValue = $"{Input.WarningStockLevel}" });
            }
            var newColor = Input.Color?.Trim();
            if ((product.Color ?? "") != (newColor ?? ""))
            {
                changes.Add(new ProductFieldChange { Field = "Color", DisplayName = "Màu sắc", OldValue = ProductAuditHelper.FormatValue(product.Color), NewValue = ProductAuditHelper.FormatValue(newColor) });
            }
            var newSize = Input.Size?.Trim();
            if ((product.Size ?? "") != (newSize ?? ""))
            {
                changes.Add(new ProductFieldChange { Field = "Size", DisplayName = "Kích cỡ", OldValue = ProductAuditHelper.FormatValue(product.Size), NewValue = ProductAuditHelper.FormatValue(newSize) });
            }
            var newMaterial = Input.Material?.Trim();
            if ((product.Material ?? "") != (newMaterial ?? ""))
            {
                changes.Add(new ProductFieldChange { Field = "Material", DisplayName = "Chất liệu", OldValue = ProductAuditHelper.FormatValue(product.Material), NewValue = ProductAuditHelper.FormatValue(newMaterial) });
            }
            var newCondition = Input.Condition?.Trim();
            if ((product.Condition ?? "") != (newCondition ?? ""))
            {
                changes.Add(new ProductFieldChange { Field = "Condition", DisplayName = "Tình trạng", OldValue = ProductAuditHelper.FormatValue(product.Condition), NewValue = ProductAuditHelper.FormatValue(newCondition) });
            }
            var newDescription = Input.Description?.Trim();
            if ((product.Description ?? "") != (newDescription ?? ""))
            {
                changes.Add(new ProductFieldChange { Field = "Description", DisplayName = "Ghi chú", OldValue = ProductAuditHelper.FormatValue(product.Description), NewValue = ProductAuditHelper.FormatValue(newDescription) });
            }

            // So sánh các thuộc tính động (DynamicAttributes)
            var oldAttrsDict = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(product.DynamicAttributes))
            {
                try
                {
                    using var doc = JsonDocument.Parse(product.DynamicAttributes);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            if (el.TryGetProperty("key", out var k) && el.TryGetProperty("value", out var v))
                            {
                                oldAttrsDict[k.GetString() ?? ""] = v.GetString() ?? "";
                            }
                        }
                    }
                }
                catch { }
            }

            foreach (var attr in allAttrs)
            {
                oldAttrsDict.TryGetValue(attr.Key, out var oldAttrVal);
                DynamicAttrs.TryGetValue(attr.Key, out var newAttrVal);
                oldAttrVal = oldAttrVal?.Trim() ?? "";
                newAttrVal = newAttrVal?.Trim() ?? "";

                if (oldAttrVal != newAttrVal)
                {
                    changes.Add(new ProductFieldChange
                    {
                        Field = "attr_" + attr.Key,
                        DisplayName = $"Thuộc tính: {attr.DisplayName}",
                        OldValue = ProductAuditHelper.FormatValue(oldAttrVal),
                        NewValue = ProductAuditHelper.FormatValue(newAttrVal)
                    });
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

            // Ghi nhận Audit Log nếu có thay đổi
            if (changes.Count > 0)
            {
                var fullName = HttpContext.Session.GetString("FullName") ?? username;
                var auditEntry = new ProductAuditLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Username = username,
                    FullName = fullName,
                    Action = "UPDATE",
                    Description = $"Cập nhật {changes.Count} thông tin sản phẩm",
                    Changes = changes
                };
                ProductAuditHelper.AppendLog(product, auditEntry);
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
