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

    public List<LiquidationOrder> LiquidationOrders { get; set; } = new();

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

        // 3. Seed test category and product if none exist
        var hasCategory = await _context.Categories.AnyAsync();
        if (!hasCategory)
        {
            var cat = new Category
            {
                CodePrefix = "AO",
                Name = "Áo thun & Sơ mi",
                Description = "Danh mục áo",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Categories.Add(cat);
            await _context.SaveChangesAsync();
        }

        var hasProduct = await _context.Products.AnyAsync();
        if (!hasProduct)
        {
            var cat = await _context.Categories.FirstOrDefaultAsync();
            var price = new PriceList
            {
                Name = "Gia 100K",
                PricePerDay = 100000,
                Deposit = 300000,
                Description = "Giá thuê tiêu chuẩn",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.PriceLists.Add(price);
            await _context.SaveChangesAsync();

            if (cat == null)
            {
                cat = new Category
                {
                    CodePrefix = "AO",
                    Name = "Áo thun & Sơ mi",
                    Description = "Danh mục áo",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Categories.Add(cat);
                await _context.SaveChangesAsync();
            }

            var prod = new Product
            {
                Code = "AO0001",
                Name = "Áo thun unisex Antigravity",
                CategoryId = cat.Id,
                PriceListId = price.Id,
                ImportPrice = 120000,
                StockQuantity = 10,
                RentedQuantity = 0,
                ImageUrl = "[]",
                IsAvailable = true,
                IsLiquidated = false,
                SystemLog = "[]",
                TotalRentRevenue = 150000
            };
            _context.Products.Add(prod);
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

        // Filtering date defaults to last 30 days if not set
        DateTime vnNow = DateTime.UtcNow.AddHours(7);
        DateTime vnFrom = FromDate ?? vnNow.Date.AddDays(-30);
        DateTime vnTo = ToDate ?? vnNow.Date;

        FromDate = vnFrom;
        ToDate = vnTo;

        var startUtc = DateTime.SpecifyKind(vnFrom.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(vnTo.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        var query = _context.LiquidationOrders
            .Include(o => o.CreatedByUser)
            .Include(o => o.LiquidationOrderDetails)
            .Where(o => o.LiquidationDate >= startUtc && o.LiquidationDate < endUtc);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var s = SearchTerm.Trim().ToLower();
            query = query.Where(o => o.Code.ToLower().Contains(s) || (o.Notes != null && o.Notes.ToLower().Contains(s)));
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        LiquidationOrders = await query
            .OrderByDescending(o => o.LiquidationDate)
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }

    // AJAX: Get details of a single liquidation order
    public async Task<IActionResult> OnGetOrderDetailAjaxAsync(int id)
    {
        var authCheck = await VerifyAccessAndSeedAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền" });

        var order = await _context.LiquidationOrders
            .Include(o => o.CreatedByUser)
            .Include(o => o.LiquidationOrderDetails)
                .ThenInclude(d => d.Product)
                    .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return new JsonResult(new { success = false, message = "Không tìm thấy phiếu thanh lý." });

        var details = order.LiquidationOrderDetails.Select(d => new
        {
            productCode = d.Product?.Code ?? "N/A",
            productName = d.Product?.Name ?? "Sản phẩm đã bị xóa",
            size = d.Product?.Size ?? "—",
            color = d.Product?.Color ?? "—",
            category = d.Product?.Category?.Name ?? "Khác",
            quantity = d.Quantity,
            importPrice = d.Product?.ImportPrice ?? 0,
            rentRevenue = d.Product?.TotalRentRevenue ?? 0,
            reason = d.Reason ?? "Thanh lý ngưng sử dụng"
        }).ToList();

        return new JsonResult(new
        {
            success = true,
            code = order.Code,
            date = order.LiquidationDate.AddHours(7).ToString("dd/MM/yyyy HH:mm"),
            creator = order.CreatedByUser?.FullName ?? order.CreatedByUser?.Username ?? "N/A",
            notes = order.Notes ?? "Không có ghi chú",
            items = details
        });
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var authCheck = await VerifyAccessAndSeedAsync();
        if (authCheck != null) return authCheck;

        var username = HttpContext.Session.GetString("Username") ?? "system";

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.LiquidationOrders
                .Include(o => o.LiquidationOrderDetails)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                ErrorMessage = "Không tìm thấy phiếu thanh lý.";
                return RedirectToPage(new { SearchTerm, FromDate, ToDate, PageIndex });
            }

            if (order.Status == "Cancelled")
            {
                ErrorMessage = "Phiếu thanh lý này đã bị hủy trước đó.";
                return RedirectToPage(new { SearchTerm, FromDate, ToDate, PageIndex });
            }

            // Restore product quantities
            foreach (var item in order.LiquidationOrderDetails)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product != null)
                {
                    product.StockQuantity += item.Quantity;
                    // Reactivate product if it was marked as liquidated
                    if (product.IsLiquidated)
                    {
                        product.IsLiquidated = false;
                        product.IsAvailable = true;
                    }

                    // Log stock history for cancellation/restoration
                    _context.StockHistories.Add(new StockHistory
                    {
                        ProductId = product.Id,
                        ActionType = "RESTORE",
                        QuantityChange = item.Quantity,
                        RemainingTotal = product.StockQuantity,
                        Note = $"Hủy phiếu thanh lý {order.Code}, khôi phục tồn kho",
                        PerformedBy = username,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            order.Status = "Cancelled";
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            SuccessMessage = $"Hủy thành công phiếu thanh lý {order.Code} và khôi phục tồn kho.";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi khi hủy phiếu: {ex.Message}";
        }

        return RedirectToPage(new { SearchTerm, FromDate, ToDate, PageIndex });
    }
}
