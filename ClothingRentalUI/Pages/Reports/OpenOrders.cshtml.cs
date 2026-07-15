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

public class OpenOrdersModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public OpenOrdersModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int PageSize => 20;

    public List<Order> OrdersData { get; set; } = new();

    // Financial Metrics
    public decimal TotalEstimatedRent { get; set; }
    public decimal TotalHoldingDeposit { get; set; }
    public int TotalOpenOrdersCount { get; set; }

    public Dictionary<string, string> UserDisplayNames { get; set; } = new();

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
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_OPEN_ORDERS"));

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

        // Lọc các đơn hàng chưa đóng (không phải Draft/Closed) trong khoảng thời gian tạo đơn
        var query = _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.CreatedByUser)
            .Where(o => o.Status != "Closed" && o.Status != "Draft" && o.CreatedAt >= startUtc && o.CreatedAt < endUtc);

        var allFiltered = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        // Tính toán các chỉ số
        TotalEstimatedRent = allFiltered.Sum(o => o.TotalPrice);
        TotalHoldingDeposit = allFiltered.Sum(o => o.TotalDeposit);
        TotalOpenOrdersCount = allFiltered.Count;

        // Phân trang
        TotalItems = allFiltered.Count;
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        OrdersData = allFiltered
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        // Load hiển thị tên
        var users = await _context.Users.ToListAsync();
        UserDisplayNames = users.ToDictionary(
            u => u.Username.ToLower(),
            u => u.FullName,
            StringComparer.OrdinalIgnoreCase
        );

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
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_OPEN_ORDERS"));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = new DateTime(todayVn.Year, todayVn.Month, 1);
        if (ToDate == null) ToDate = todayVn;

        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        var list = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.CreatedByUser)
            .Where(o => o.Status != "Closed" && o.Status != "Draft" && o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var excelData = list.Select((o, index) => {
            string statusStr = o.Status switch {
                "Rented" => "Đang thuê",
                "PartiallyReturned" => "Trả một phần",
                "Overdue" => "Quá hạn",
                _ => o.Status
            };

            return new Dictionary<string, object> {
                { "STT", index + 1 },
                { "Mã đơn", o.Code },
                { "Ngày tạo", o.CreatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm") },
                { "Khách hàng", o.Customer?.FullName ?? "" },
                { "Số điện thoại", o.Customer?.PhoneNumber ?? "" },
                { "Tiền thuê dự kiến (đ)", o.TotalPrice },
                { "Tiền cọc giữ (đ)", o.TotalDeposit },
                { "Trạng thái", statusStr },
                { "Nhân viên tạo", o.CreatedByUser?.FullName ?? "" }
            };
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"BaoCaoDonChuaTra_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public string GetUserDisplayName(string? username)
    {
        if (string.IsNullOrEmpty(username)) return "N/A";
        return UserDisplayNames.TryGetValue(username.ToLower(), out var fn) ? fn : username;
    }
}
