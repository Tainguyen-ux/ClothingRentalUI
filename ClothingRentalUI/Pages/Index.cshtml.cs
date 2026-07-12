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

namespace ClothingRentalUI.Pages;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public int ActiveRentalsCount { get; set; }
    public decimal TodayRevenue { get; set; }
    public int LowStockCount { get; set; }
    public int TodaySalesCount { get; set; }

    public List<RecentActivityItem> RecentActivities { get; set; } = new();
    public List<RevenueChartPoint> RevenueChartData { get; set; } = new();

    public class RecentActivityItem
    {
        public string Code { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "Rental" or "Sale"
        public DateTime DateTime { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class RevenueChartPoint
    {
        public string Label { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        // Time calculations (Vietnam time UTC+7)
        DateTime vnNow = DateTime.UtcNow.AddHours(7);
        DateTime todayStartVn = vnNow.Date;
        DateTime todayStartUtc = DateTime.SpecifyKind(todayStartVn.AddHours(-7), DateTimeKind.Utc);
        DateTime todayEndUtc = DateTime.SpecifyKind(todayStartVn.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        // 1. KPI Count: Active Rentals (đơn thuê đang mở)
        ActiveRentalsCount = await _context.Orders
            .Where(o => o.OrderType == "Rental" && o.ActualReturnDate == null)
            .CountAsync();

        // 2. KPI Count: Today Revenue (doanh thu thực tế hôm nay từ Transactions)
        TodayRevenue = await _context.Transactions
            .Where(t => t.TransactionDate >= todayStartUtc && t.TransactionDate < todayEndUtc)
            .SumAsync(t => t.Amount);

        // 3. KPI Count: Today Sales (số lượng đơn bán hôm nay)
        TodaySalesCount = await _context.SaleOrders
            .Where(s => s.CreatedAt >= todayStartUtc && s.CreatedAt < todayEndUtc)
            .CountAsync();

        // 4. KPI Count: Low Stock Products (sản phẩm dưới hạn mức)
        LowStockCount = await _context.Products
            .Where(p => !p.IsLiquidated && p.WarningStockLevel > 0 && p.StockQuantity <= p.WarningStockLevel)
            .CountAsync();

        // 5. Recent Activity: Get 5 latest rental orders and 5 latest sale orders, then combine and take top 5
        var latestRentals = await _context.Orders
            .Include(o => o.Customer)
            .OrderByDescending(o => o.CreatedAt)
            .Take(5)
            .Select(o => new RecentActivityItem
            {
                Code = o.Code,
                CustomerName = o.Customer != null ? o.Customer.FullName : "Khách vãng lai",
                Type = "Rental",
                DateTime = o.CreatedAt,
                Amount = o.FinalAmount,
                Status = o.ActualReturnDate != null ? "Đã trả" : "Đang thuê"
            })
            .ToListAsync();

        var latestSales = await _context.SaleOrders
            .Include(s => s.Customer)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new RecentActivityItem
            {
                Code = s.Code,
                CustomerName = s.Customer != null ? s.Customer.FullName : "Khách vãng lai",
                Type = "Sale",
                DateTime = s.CreatedAt,
                Amount = s.FinalAmount,
                Status = s.Status == "Completed" ? "Đã thanh toán" : "Bản nháp"
            })
            .ToListAsync();

        RecentActivities = latestRentals.Concat(latestSales)
            .OrderByDescending(a => a.DateTime)
            .Take(5)
            .ToList();

        // 6. Last 7 Days Revenue Chart Data (using Transactions)
        for (int i = 6; i >= 0; i--)
        {
            DateTime dayVn = vnNow.Date.AddDays(-i);
            DateTime dayStartUtc = DateTime.SpecifyKind(dayVn.AddHours(-7), DateTimeKind.Utc);
            DateTime dayEndUtc = DateTime.SpecifyKind(dayVn.AddDays(1).AddHours(-7), DateTimeKind.Utc);

            decimal dayRev = await _context.Transactions
                .Where(t => t.TransactionDate >= dayStartUtc && t.TransactionDate < dayEndUtc)
                .SumAsync(t => t.Amount);

            RevenueChartData.Add(new RevenueChartPoint
            {
                Label = dayVn.ToString("dd/MM"),
                Revenue = dayRev
            });
        }

        return Page();
    }
}
