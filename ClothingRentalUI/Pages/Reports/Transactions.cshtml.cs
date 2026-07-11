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

namespace ClothingRentalUI.Pages.Reports;

public class TransactionsModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public TransactionsModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    public List<Transaction> TransactionsData { get; set; } = new();
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetRevenue { get; set; }

    public decimal CashIncome { get; set; }
    public decimal CashExpense { get; set; }
    public decimal TransferIncome { get; set; }
    public decimal TransferExpense { get; set; }

    public Dictionary<string, string> UserDisplayNames { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        // 1. Kiểm tra đăng nhập
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        // 2. Kiểm tra quyền REPORT_VIEW
        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_VIEW"));

        if (!hasPermission)
        {
            return RedirectToPage("/Clothes/Index");
        }

        // 3. Đảm bảo cấu trúc menu đã được đồng bộ
        await SeedReportMenusAsync();

        // 4. Thiết lập ngày mặc định (múi giờ Việt Nam UTC+7)
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = todayVn;
        if (ToDate == null) ToDate = todayVn;

        // 5. Chuyển đổi ngày sang UTC để truy vấn DB chính xác
        var startUtc = FromDate.Value.Date.AddHours(-7);
        var endUtc = ToDate.Value.Date.AddDays(1).AddTicks(-1).AddHours(-7);

        // 6. Truy vấn danh sách giao dịch
        TransactionsData = await _context.Transactions
            .Include(t => t.Order)
                .ThenInclude(o => o!.Customer)
            .Where(t => t.TransactionDate >= startUtc && t.TransactionDate <= endUtc)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();

        // 7. Load danh sách hiển thị tên nhân viên
        var users = await _context.Users.ToListAsync();
        UserDisplayNames = users.ToDictionary(
            u => u.Username.ToLower(),
            u => u.FullName,
            StringComparer.OrdinalIgnoreCase
        );

        // 8. Tính toán các chỉ số thống kê
        CalculateStatistics();

        return Page();
    }

    private void CalculateStatistics()
    {
        TotalIncome = 0;
        TotalExpense = 0;
        CashIncome = 0;
        CashExpense = 0;
        TransferIncome = 0;
        TransferExpense = 0;

        foreach (var t in TransactionsData)
        {
            // Xác định giao dịch là Thu hay Chi
            var isIn = t.Type == "DEPOSIT_RECEIVED" || t.Type == "RENTAL_PAYMENT" || t.Type == "PENALTY_PAYMENT" || t.Type == "DEPOSIT_REFUNDED_CANCEL";
            var isCash = t.PaymentMethod == "CASH";

            if (isIn)
            {
                TotalIncome += t.Amount;
                if (isCash) CashIncome += t.Amount;
                else TransferIncome += t.Amount;
            }
            else
            {
                TotalExpense += t.Amount;
                if (isCash) CashExpense += t.Amount;
                else TransferExpense += t.Amount;
            }
        }

        NetRevenue = TotalIncome - TotalExpense;
    }

    public string GetUserDisplayName(string username)
    {
        if (string.IsNullOrEmpty(username)) return "System";
        return UserDisplayNames.TryGetValue(username.ToLower(), out var fn) ? fn : username;
    }

    private async Task SeedReportMenusAsync()
    {
        var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Url == "/Reports/Index" || (m.Name == "Báo cáo thống kê" && m.ParentId == null));
        if (parentMenu != null)
        {
            bool needsSave = false;

            if (parentMenu.Url != "#")
            {
                parentMenu.Url = "#";
                needsSave = true;
            }

            var hasSummary = await _context.Menus.AnyAsync(m => m.Url == "/Reports/Index" && m.ParentId == parentMenu.Id);
            if (!hasSummary)
            {
                _context.Menus.Add(new Menu
                {
                    Name = "Tổng quan",
                    Url = "/Reports/Index",
                    Icon = "📊",
                    ParentId = parentMenu.Id,
                    DisplayOrder = 1,
                    RequiredPermissionId = parentMenu.RequiredPermissionId
                });
                needsSave = true;
            }

            var hasTxnReport = await _context.Menus.AnyAsync(m => m.Url == "/Reports/Transactions" && m.ParentId == parentMenu.Id);
            if (!hasTxnReport)
            {
                _context.Menus.Add(new Menu
                {
                    Name = "Thống kê giao dịch",
                    Url = "/Reports/Transactions",
                    Icon = "💸",
                    ParentId = parentMenu.Id,
                    DisplayOrder = 2,
                    RequiredPermissionId = parentMenu.RequiredPermissionId
                });
                needsSave = true;
            }

            if (needsSave)
            {
                await _context.SaveChangesAsync();
            }
        }
    }
}
