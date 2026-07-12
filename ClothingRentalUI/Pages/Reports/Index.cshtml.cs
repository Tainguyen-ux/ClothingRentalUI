using System;
using System.Collections.Generic;
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

    // Chart data properties
    public List<string> MonthlyLabels { get; set; } = new();
    public List<decimal> MonthlyRentalRevenue { get; set; } = new();
    public List<decimal> MonthlySaleRevenue { get; set; } = new();

    public decimal TotalRentalRevenueBreakdown { get; set; }
    public decimal TotalSaleRevenueBreakdown { get; set; }
    public decimal TotalPenaltyRevenueBreakdown { get; set; }

    public List<string> TopStaffNames { get; set; } = new();
    public List<decimal> TopStaffRevenues { get; set; } = new();

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

        // 1. Tính toán thống kê nhanh
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

        // 2. Doanh thu 6 tháng gần nhất
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        for (int i = 5; i >= 0; i--)
        {
            var targetMonth = todayVn.AddMonths(-i);
            var startOfMonth = new DateTime(targetMonth.Year, targetMonth.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1);
            
            var startUtc = DateTime.SpecifyKind(startOfMonth.AddHours(-7), DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(endOfMonth.AddHours(-7), DateTimeKind.Utc);

            MonthlyLabels.Add($"T{targetMonth.Month}/{targetMonth.Year}");

            var rentalRev = await _context.Orders
                .Where(o => o.Status == "Closed" && o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
                .SumAsync(o => o.FinalAmount);
            MonthlyRentalRevenue.Add(rentalRev);

            var saleRev = await _context.SaleOrders
                .Where(so => so.Status == "Closed" && so.CreatedAt >= startUtc && so.CreatedAt < endUtc)
                .SumAsync(so => so.FinalAmount);
            MonthlySaleRevenue.Add(saleRev);
        }

        // 3. Cơ cấu doanh thu đóng (Thuê đồ, phạt phát sinh, bán đứt)
        TotalRentalRevenueBreakdown = await _context.Orders
            .Where(o => o.Status == "Closed")
            .SumAsync(o => o.TotalPrice - o.DiscountAmount);

        TotalPenaltyRevenueBreakdown = await _context.Orders
            .Where(o => o.Status == "Closed")
            .SumAsync(o => o.TotalPenalty);

        TotalSaleRevenueBreakdown = await _context.SaleOrders
            .Where(so => so.Status == "Closed")
            .SumAsync(so => so.FinalAmount);

        // 4. Top 5 hiệu suất nhân viên (Tổng doanh số gộp)
        var users = await _context.Users.ToListAsync();
        var staffStats = new List<(string Name, decimal Revenue)>();
        foreach (var u in users)
        {
            if (u.Role == "System") continue;
            var rentalSum = await _context.Orders
                .Where(o => o.CreatedByUserId == u.Id && o.Status != "Draft")
                .SumAsync(o => o.FinalAmount);

            var saleSum = await _context.SaleOrders
                .Where(so => so.CreatedByUserId == u.Id && so.Status != "Draft")
                .SumAsync(so => so.FinalAmount);

            var total = rentalSum + saleSum;
            if (total > 0)
            {
                staffStats.Add((u.FullName, total));
            }
        }

        var topStaff = staffStats.OrderByDescending(x => x.Revenue).Take(5).ToList();
        TopStaffNames = topStaff.Select(x => x.Name).ToList();
        TopStaffRevenues = topStaff.Select(x => x.Revenue).ToList();

        return Page();
    }
}
