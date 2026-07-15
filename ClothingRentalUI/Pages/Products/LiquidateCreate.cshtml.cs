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

public class LiquidateCreateModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public LiquidateCreateModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null) return RedirectToPage("/Auth/Login");

        bool hasPermission = user.Role == "Admin" || 
                             user.UserPermissions.Any(up => up.Permission != null && 
                                                            (up.Permission.Code == "CLOTHES_LIQUIDATE_CREATE" || up.Permission.Code == "CLOTHES_LIQUIDATE"));

        if (!hasPermission)
        {
            return RedirectToPage("/Index");
        }

        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;
        return Page();
    }

    // AJAX: Search available products for liquidation
    public async Task<IActionResult> OnGetSearchProductsAsync(string? term)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false });

        term = term?.Trim().ToLower() ?? "";

        var products = await _context.Products
            .Include(p => p.Category)
            .Where(p => !p.IsLiquidated && p.StockQuantity > p.RentedQuantity
                && (string.IsNullOrEmpty(term) || p.Name.ToLower().Contains(term) || p.Code.ToLower().Contains(term)))
            .Take(20)
            .Select(p => new {
                p.Id,
                p.Code,
                p.Name,
                p.Size,
                p.Color,
                categoryName = p.Category != null ? p.Category.Name : "Khác",
                available = p.StockQuantity - p.RentedQuantity,
                importPrice = p.ImportPrice,
                rentRevenue = p.TotalRentRevenue
            })
            .ToListAsync();

        return new JsonResult(new { success = true, data = products });
    }

    // POST: Create liquidation order
    public async Task<IActionResult> OnPostCreateLiquidationAjaxAsync([FromBody] CreateLiquidationRequest request)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

        var username = HttpContext.Session.GetString("Username") ?? "system";
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (request.Items == null || !request.Items.Any())
            return new JsonResult(new { success = false, message = "Vui lòng chọn ít nhất 1 sản phẩm để thanh lý." });

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Generate Code: TL + yyMMdd + 4-digit sequence
            DateTime vnNow = DateTime.UtcNow.AddHours(7);
            string todayStr = vnNow.ToString("yyMMdd");
            var count = await _context.LiquidationOrders.Where(o => o.Code.StartsWith("TL" + todayStr)).CountAsync();
            string code = $"TL{todayStr}{(count + 1):D4}";

            var liquidationOrder = new LiquidationOrder
            {
                Code = code,
                LiquidationDate = DateTime.SpecifyKind(vnNow, DateTimeKind.Utc),
                Status = "Completed",
                CreatedByUserId = user?.Id,
                CreatedAt = DateTime.UtcNow,
                Notes = request.Notes
            };

            foreach (var item in request.Items)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) throw new Exception($"Không tìm thấy sản phẩm ID {item.ProductId}.");

                int qty = item.Quantity > 0 ? item.Quantity : 1;
                int available = product.StockQuantity - product.RentedQuantity;
                if (available < qty)
                    throw new Exception($"Sản phẩm '{product.Name}' ({product.Code}) chỉ còn {available} chiếc khả dụng để thanh lý.");

                // Subtract from product stock
                product.StockQuantity -= qty;

                // Mark liquidated if stock reaches 0
                if (product.StockQuantity == 0)
                {
                    product.IsLiquidated = true;
                    product.IsAvailable = false;
                }

                var reason = string.IsNullOrWhiteSpace(item.Reason) ? "Thanh lý ngưng sử dụng sản phẩm" : item.Reason.Trim();

                // Add to details
                liquidationOrder.LiquidationOrderDetails.Add(new LiquidationOrderDetail
                {
                    ProductId = product.Id,
                    Quantity = qty,
                    Reason = reason
                });

                // Write stock history
                _context.StockHistories.Add(new StockHistory
                {
                    ProductId = product.Id,
                    ActionType = "LIQUIDATE",
                    QuantityChange = -qty,
                    RemainingTotal = product.StockQuantity,
                    Note = reason,
                    PerformedBy = username,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _context.LiquidationOrders.Add(liquidationOrder);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new JsonResult(new { success = true, orderId = liquidationOrder.Id, code = liquidationOrder.Code });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            var msg = ex.InnerException?.Message ?? ex.Message;
            return new JsonResult(new { success = false, message = msg });
        }
    }

    public class CreateLiquidationRequest
    {
        public string? Notes { get; set; }
        public List<LiquidationItemRequest> Items { get; set; } = new();
    }

    public class LiquidationItemRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
        public string? Reason { get; set; }
    }
}
