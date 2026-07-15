using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using MiniExcelLibs;

namespace ClothingRentalUI.Pages.Reports;

public class StaffRevenueModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public StaffRevenueModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    public List<StaffRevenueRow> StaffRevenueList { get; set; } = new();

    public decimal GrandTotalRental { get; set; }
    public decimal GrandTotalSale { get; set; }
    public decimal GrandTotalCombined { get; set; }

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
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_STAFF_REVENUE"));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        // Ngày mặc định: Từ đầu tháng đến ngày hiện tại
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = new DateTime(todayVn.Year, todayVn.Month, 1);
        if (ToDate == null) ToDate = todayVn;

        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        // Load tất cả người dùng
        var users = await _context.Users.ToListAsync();

        // Load toàn bộ orders trong khoảng thời gian để tính toán ở bộ nhớ (hiệu năng tốt cho quy mô cửa hàng)
        var rentalOrders = await _context.Orders
            .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc && o.Status != "Draft")
            .ToListAsync();

        var saleOrders = await _context.SaleOrders
            .Where(so => so.CreatedAt >= startUtc && so.CreatedAt < endUtc && so.Status != "Draft")
            .ToListAsync();

        StaffRevenueList = new List<StaffRevenueRow>();

        foreach (var u in users)
        {
            var userRentals = rentalOrders.Where(o => o.CreatedByUserId == u.Id).ToList();
            var userSales = saleOrders.Where(so => so.CreatedByUserId == u.Id).ToList();

            var row = new StaffRevenueRow
            {
                Username = u.Username,
                FullName = u.FullName,
                Role = u.Role,
                RentalRevenue = userRentals.Sum(o => o.FinalAmount),
                RentalOrdersCount = userRentals.Count,
                SaleRevenue = userSales.Sum(so => so.FinalAmount),
                SaleOrdersCount = userSales.Count
            };

            // Chỉ đưa vào danh sách nếu có phát sinh giao dịch hoặc là tài khoản đang hoạt động
            if (row.RentalOrdersCount > 0 || row.SaleOrdersCount > 0 || u.Role != "System")
            {
                StaffRevenueList.Add(row);
            }
        }

        // Sắp xếp theo tổng doanh thu giảm dần
        StaffRevenueList = StaffRevenueList.OrderByDescending(r => r.TotalRevenue).ToList();

        GrandTotalRental = StaffRevenueList.Sum(r => r.RentalRevenue);
        GrandTotalSale = StaffRevenueList.Sum(r => r.SaleRevenue);
        GrandTotalCombined = StaffRevenueList.Sum(r => r.TotalRevenue);

        return Page();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
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
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_STAFF_REVENUE"));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = new DateTime(todayVn.Year, todayVn.Month, 1);
        if (ToDate == null) ToDate = todayVn;

        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        var users = await _context.Users.ToListAsync();

        var rentalOrders = await _context.Orders
            .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc && o.Status != "Draft")
            .ToListAsync();

        var saleOrders = await _context.SaleOrders
            .Where(so => so.CreatedAt >= startUtc && so.CreatedAt < endUtc && so.Status != "Draft")
            .ToListAsync();

        var rows = new List<StaffRevenueRow>();

        foreach (var u in users)
        {
            var userRentals = rentalOrders.Where(o => o.CreatedByUserId == u.Id).ToList();
            var userSales = saleOrders.Where(so => so.CreatedByUserId == u.Id).ToList();

            var row = new StaffRevenueRow
            {
                Username = u.Username,
                FullName = u.FullName,
                Role = u.Role,
                RentalRevenue = userRentals.Sum(o => o.FinalAmount),
                RentalOrdersCount = userRentals.Count,
                SaleRevenue = userSales.Sum(so => so.FinalAmount),
                SaleOrdersCount = userSales.Count
            };

            if (row.RentalOrdersCount > 0 || row.SaleOrdersCount > 0 || u.Role != "System")
            {
                rows.Add(row);
            }
        }

        var sortedRows = rows.OrderByDescending(r => r.TotalRevenue).ToList();

        var excelData = sortedRows.Select((r, index) => new Dictionary<string, object> {
            { "STT", index + 1 },
            { "Tên nhân viên", r.FullName },
            { "Tài khoản", r.Username },
            { "Vai trò", r.Role },
            { "Số đơn thuê", r.RentalOrdersCount },
            { "Doanh thu thuê (đ)", r.RentalRevenue },
            { "Số đơn bán", r.SaleOrdersCount },
            { "Doanh thu bán (đ)", r.SaleRevenue },
            { "Tổng doanh thu (đ)", r.TotalRevenue }
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"DoanhThuNhanVien_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public class StaffRevenueRow
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public decimal RentalRevenue { get; set; }
        public int RentalOrdersCount { get; set; }
        public decimal SaleRevenue { get; set; }
        public int SaleOrdersCount { get; set; }
        public decimal TotalRevenue => RentalRevenue + SaleRevenue;
    }
}
