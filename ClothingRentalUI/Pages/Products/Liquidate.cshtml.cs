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

namespace ClothingRentalUI.Pages.Products;

public class LiquidateModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public LiquidateModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "active"; // active or history

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 15;

    public List<Product> ActiveProducts { get; set; } = new();
    public List<StockHistory> LiquidateHistories { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAndSeedAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        bool needsSave = false;

        // 1. Check Permission
        var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == "CLOTHES_LIQUIDATE");
        if (permission == null)
        {
            permission = new Permission
            {
                Code = "CLOTHES_LIQUIDATE",
                Name = "Thanh lý & Ngừng sử dụng sản phẩm",
                Type = "UI"
            };
            _context.Permissions.Add(permission);
            needsSave = true;
        }

        if (needsSave) await _context.SaveChangesAsync();

        // 2. Check Menu
        var menu = await _context.Menus.FirstOrDefaultAsync(m => m.Url == "/Products/Liquidate");
        if (menu == null)
        {
            var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Name.Contains("Hàng") && m.ParentId == null);
            if (parentMenu != null)
            {
                menu = new Menu
                {
                    Name = "Thanh lý & Ngừng dùng",
                    Url = "/Products/Liquidate",
                    Icon = "♻️",
                    ParentId = parentMenu.Id,
                    DisplayOrder = 6,
                    RequiredPermissionId = permission.Id
                };
                _context.Menus.Add(menu);
                needsSave = true;
            }
        }

        if (needsSave)
        {
            await _context.SaveChangesAsync();

            var admins = await _context.Users
                .Include(u => u.UserPermissions)
                .Where(u => u.Role == "Admin")
                .ToListAsync();

            foreach (var admin in admins)
            {
                if (!admin.UserPermissions.Any(up => up.PermissionId == permission.Id))
                {
                    _context.UserPermissions.Add(new UserPermission
                    {
                        UserId = admin.Id,
                        PermissionId = permission.Id
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "CLOTHES_LIQUIDATE")));

        if (!hasPermission)
        {
            return RedirectToPage("/Products/Index");
        }

        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAndSeedAsync();
        if (authCheck != null) return authCheck;

        if (Tab == "history")
        {
            // Lịch sử thanh lý
            DateTime vnNow = DateTime.UtcNow.AddHours(7);
            DateTime vnFrom = FromDate ?? vnNow.Date.AddDays(-30);
            DateTime vnTo = ToDate ?? vnNow.Date;

            FromDate = vnFrom;
            ToDate = vnTo;

            var startUtc = DateTime.SpecifyKind(vnFrom.AddHours(-7), DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(vnTo.AddDays(1).AddHours(-7), DateTimeKind.Utc);

            var query = _context.StockHistories
                .Include(s => s.Product)
                .Where(s => s.ActionType == "LIQUIDATE" && s.CreatedAt >= startUtc && s.CreatedAt < endUtc);

            TotalItems = await query.CountAsync();
            TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
            if (PageIndex < 1) PageIndex = 1;
            if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

            LiquidateHistories = await query
                .OrderByDescending(s => s.CreatedAt)
                .Skip((PageIndex - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }
        else
        {
            // Danh sách sản phẩm khả dụng để thanh lý
            var query = _context.Products
                .Include(p => p.Category)
                .Where(p => !p.IsLiquidated && p.StockQuantity > 0);

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var s = SearchTerm.Trim().ToLower();
                query = query.Where(p => p.Code.ToLower().Contains(s) || p.Name.ToLower().Contains(s));
            }

            TotalItems = await query.CountAsync();
            TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
            if (PageIndex < 1) PageIndex = 1;
            if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

            ActiveProducts = await query
                .OrderByDescending(p => p.Id)
                .Skip((PageIndex - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostLiquidateAsync(List<int> SelectedProductIds, Dictionary<int, int> Quantities, Dictionary<int, string> Notes)
    {
        var authCheck = await VerifyAccessAndSeedAsync();
        if (authCheck != null) return authCheck;

        var username = HttpContext.Session.GetString("Username") ?? "system";

        if (SelectedProductIds == null || !SelectedProductIds.Any())
        {
            ErrorMessage = "Vui lòng chọn ít nhất 1 sản phẩm để thanh lý.";
            return RedirectToPage(new { Tab, SearchTerm, FromDate, ToDate, PageIndex });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            int successCount = 0;
            foreach (var productId in SelectedProductIds)
            {
                var product = await _context.Products.FindAsync(productId);
                if (product == null || product.IsLiquidated) continue;

                if (!Quantities.TryGetValue(productId, out var qtyToLiquidate) || qtyToLiquidate <= 0)
                {
                    throw new Exception($"Số lượng thanh lý cho sản phẩm {product.Code} không hợp lệ.");
                }

                int availableQty = product.StockQuantity - product.RentedQuantity;
                if (qtyToLiquidate > availableQty)
                {
                    throw new Exception($"Số lượng thanh lý ({qtyToLiquidate}) vượt quá số lượng khả dụng ({availableQty}) của sản phẩm {product.Code}. Vui lòng kiểm tra lại đồ đang cho thuê.");
                }

                Notes.TryGetValue(productId, out var note);
                if (string.IsNullOrWhiteSpace(note))
                {
                    note = "Thanh lý ngưng sử dụng sản phẩm";
                }

                // Trừ tồn kho
                product.StockQuantity -= qtyToLiquidate;

                // Nếu tồn kho bằng 0, đánh dấu ngưng sử dụng vĩnh viễn (IsLiquidated = true)
                if (product.StockQuantity == 0)
                {
                    product.IsLiquidated = true;
                    product.IsAvailable = false;
                }

                // Ghi nhận lịch sử kho
                _context.StockHistories.Add(new StockHistory
                {
                    ProductId = product.Id,
                    ActionType = "LIQUIDATE",
                    QuantityChange = -qtyToLiquidate,
                    RemainingTotal = product.StockQuantity,
                    Note = note,
                    PerformedBy = username,
                    CreatedAt = DateTime.UtcNow
                });

                successCount++;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            SuccessMessage = $"Đã thực hiện thanh lý thành công {successCount} sản phẩm.";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi: {ex.Message}";
        }

        return RedirectToPage(new { Tab, SearchTerm, FromDate, ToDate, PageIndex });
    }

    // AJAX: Tìm nhanh bằng mã/barcode
    public async Task<IActionResult> OnGetFindProductByCodeAsync(string code)
    {
        var authCheck = await VerifyAccessAndSeedAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền" });

        if (string.IsNullOrWhiteSpace(code))
            return new JsonResult(new { success = false, message = "Mã không hợp lệ" });

        var product = await _context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => !p.IsLiquidated && p.StockQuantity > 0 && p.Code.ToLower() == code.Trim().ToLower());

        if (product == null)
            return new JsonResult(new { success = false, message = "Không tìm thấy sản phẩm khả dụng hoặc sản phẩm đã thanh lý hết." });

        int available = product.StockQuantity - product.RentedQuantity;

        return new JsonResult(new { 
            success = true, 
            id = product.Id, 
            code = product.Code, 
            name = product.Name,
            category = product.Category?.Name ?? "Khác",
            size = product.Size ?? "—",
            color = product.Color ?? "—",
            stock = product.StockQuantity,
            rented = product.RentedQuantity,
            available = available,
            importPrice = product.ImportPrice,
            rentRevenue = product.TotalRentRevenue
        });
    }
}
