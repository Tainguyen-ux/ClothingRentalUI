using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;

namespace ClothingRentalUI.Pages.Reports;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public decimal ClosedOrdersRevenue { get; set; }
    public decimal EstimatedOpenRevenue { get; set; }
    public int LowStockCount { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalActiveProducts { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_VIEW"));

        if (!hasPermission)
        {
            return RedirectToPage("/Clothes/Index");
        }

        // Tính toán thống kê nhanh
        ClosedOrdersRevenue = await _context.Orders
            .Where(o => o.Status == "Closed")
            .SumAsync(o => o.TotalPrice);

        EstimatedOpenRevenue = await _context.Orders
            .Where(o => o.Status != "Closed" && o.Status != "Draft")
            .SumAsync(o => o.TotalPrice);

        LowStockCount = await _context.Products
            .CountAsync(p => p.WarningStockLevel > 0 && p.StockQuantity <= p.WarningStockLevel);

        TotalCustomers = await _context.Customers
            .CountAsync(c => c.Status == "Active");

        TotalActiveProducts = await _context.Products
            .CountAsync(p => !p.IsLiquidated);

        return Page();
    }
}
