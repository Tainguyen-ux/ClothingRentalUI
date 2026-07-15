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

    [BindProperty(SupportsGet = true)]
    public string? OrderCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CustomerName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TxnType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PaymentMethod { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PerformedBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 20;

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
        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        // 6. Truy vấn danh sách giao dịch
        var query = _context.Transactions
            .Include(t => t.Order)
                .ThenInclude(o => o!.Customer)
            .Include(t => t.SaleOrder)
                .ThenInclude(so => so!.Customer)
            .Where(t => t.TransactionDate >= startUtc && t.TransactionDate < endUtc);

        // Áp dụng bộ lọc bổ sung trên grid
        if (!string.IsNullOrEmpty(OrderCode))
        {
            var lowerCode = OrderCode.ToLower().Trim();
            query = query.Where(t => 
                (t.Order != null && t.Order.Code.ToLower().Contains(lowerCode)) ||
                (t.SaleOrder != null && t.SaleOrder.Code.ToLower().Contains(lowerCode))
            );
        }

        if (!string.IsNullOrEmpty(CustomerName))
        {
            var lowerName = CustomerName.ToLower().Trim();
            query = query.Where(t => 
                (t.Order != null && t.Order.Customer != null &&
                 (t.Order.Customer.FullName.ToLower().Contains(lowerName) || t.Order.Customer.PhoneNumber.Contains(lowerName))) ||
                (t.SaleOrder != null && t.SaleOrder.Customer != null &&
                 (t.SaleOrder.Customer.FullName.ToLower().Contains(lowerName) || t.SaleOrder.Customer.PhoneNumber.Contains(lowerName)))
            );
        }

        if (!string.IsNullOrEmpty(TxnType))
        {
            query = query.Where(t => t.Type == TxnType);
        }

        if (!string.IsNullOrEmpty(PaymentMethod))
        {
            query = query.Where(t => t.PaymentMethod == PaymentMethod);
        }

        if (!string.IsNullOrEmpty(PerformedBy))
        {
            var lowerPerformedBy = PerformedBy.ToLower().Trim();
            query = query.Where(t => t.PerformedBy.ToLower().Contains(lowerPerformedBy) ||
                _context.Users.Any(u => u.Username.ToLower() == t.PerformedBy.ToLower() && u.FullName.ToLower().Contains(lowerPerformedBy)));
        }

        // Lấy tất cả bản ghi đã lọc để tính toán chỉ số thống kê (toàn bộ khoảng/bộ lọc hiện tại)
        var allFilteredTransactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();

        // 7. Load danh sách hiển thị tên nhân viên
        var users = await _context.Users.ToListAsync();
        UserDisplayNames = users.ToDictionary(
            u => u.Username.ToLower(),
            u => u.FullName,
            StringComparer.OrdinalIgnoreCase
        );

        // 8. Tính toán các chỉ số thống kê trên toàn bộ tập dữ liệu đã lọc
        CalculateStatistics(allFilteredTransactions);

        // Phân trang
        TotalItems = allFilteredTransactions.Count;
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        TransactionsData = allFilteredTransactions
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
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

        // 4. Thiết lập ngày mặc định (múi giờ Việt Nam UTC+7)
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = todayVn;
        if (ToDate == null) ToDate = todayVn;

        // 5. Chuyển đổi ngày sang UTC để truy vấn DB chính xác
        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        // 6. Truy vấn danh sách giao dịch
        var query = _context.Transactions
            .Include(t => t.Order)
                .ThenInclude(o => o!.Customer)
            .Include(t => t.SaleOrder)
                .ThenInclude(so => so!.Customer)
            .Where(t => t.TransactionDate >= startUtc && t.TransactionDate < endUtc);

        // Áp dụng bộ lọc bổ sung trên grid
        if (!string.IsNullOrEmpty(OrderCode))
        {
            var lowerCode = OrderCode.ToLower().Trim();
            query = query.Where(t => 
                (t.Order != null && t.Order.Code.ToLower().Contains(lowerCode)) ||
                (t.SaleOrder != null && t.SaleOrder.Code.ToLower().Contains(lowerCode))
            );
        }

        if (!string.IsNullOrEmpty(CustomerName))
        {
            var lowerName = CustomerName.ToLower().Trim();
            query = query.Where(t => 
                (t.Order != null && t.Order.Customer != null &&
                 (t.Order.Customer.FullName.ToLower().Contains(lowerName) || t.Order.Customer.PhoneNumber.Contains(lowerName))) ||
                (t.SaleOrder != null && t.SaleOrder.Customer != null &&
                 (t.SaleOrder.Customer.FullName.ToLower().Contains(lowerName) || t.SaleOrder.Customer.PhoneNumber.Contains(lowerName)))
            );
        }

        if (!string.IsNullOrEmpty(TxnType))
        {
            query = query.Where(t => t.Type == TxnType);
        }

        if (!string.IsNullOrEmpty(PaymentMethod))
        {
            query = query.Where(t => t.PaymentMethod == PaymentMethod);
        }

        if (!string.IsNullOrEmpty(PerformedBy))
        {
            var lowerPerformedBy = PerformedBy.ToLower().Trim();
            query = query.Where(t => t.PerformedBy.ToLower().Contains(lowerPerformedBy) ||
                _context.Users.Any(u => u.Username.ToLower() == t.PerformedBy.ToLower() && u.FullName.ToLower().Contains(lowerPerformedBy)));
        }

        var list = await query.OrderByDescending(t => t.TransactionDate).ToListAsync();

        var users = await _context.Users.ToListAsync();
        var userDisplayNames = users.ToDictionary(
            u => u.Username.ToLower(),
            u => u.FullName,
            StringComparer.OrdinalIgnoreCase
        );

        string GetUserDisplayName(string uname)
        {
            if (string.IsNullOrEmpty(uname)) return "System";
            return userDisplayNames.TryGetValue(uname.ToLower(), out var fn) ? fn : uname;
        }

        var excelData = list.Select((t, index) => {
            var code = t.Order?.Code ?? t.SaleOrder?.Code ?? "";
            var customer = t.Order?.Customer?.FullName ?? t.SaleOrder?.Customer?.FullName ?? "Khách lẻ";
            var phone = t.Order?.Customer?.PhoneNumber ?? t.SaleOrder?.Customer?.PhoneNumber ?? "";
            
            var typeName = t.Type switch {
                "DEPOSIT_RECEIVED" => "Nhận cọc",
                "DEPOSIT_REFUNDED" => "Hoàn cọc",
                "RENTAL_PAYMENT" => "Tiền thuê",
                "SALE_PAYMENT" => "Tiền bán hàng",
                "PENALTY_PAYMENT" => "Phí phát sinh",
                "DEPOSIT_RECEIVED_CANCEL" => "Hủy Nhận cọc",
                "DEPOSIT_REFUNDED_CANCEL" => "Hủy Hoàn cọc",
                "RENTAL_PAYMENT_CANCEL" => "Hủy Tiền thuê",
                "SALE_PAYMENT_CANCEL" => "Hủy Tiền bán hàng",
                "PENALTY_PAYMENT_CANCEL" => "Hủy Phí phát sinh",
                _ => t.Type
            };

            var method = t.PaymentMethod switch {
                "CASH" => "Tiền mặt",
                "TRANSFER" => "Chuyển khoản",
                "CARD" => "Quẹt thẻ",
                _ => t.PaymentMethod
            };

            var isIncome = t.Type == "DEPOSIT_RECEIVED" || t.Type == "RENTAL_PAYMENT" || t.Type == "PENALTY_PAYMENT" || t.Type == "DEPOSIT_REFUNDED_CANCEL" || t.Type == "SALE_PAYMENT";
            var flowType = isIncome ? "Thu" : "Chi";

            return new Dictionary<string, object> {
                { "STT", index + 1 },
                { "Thời gian", t.TransactionDate.AddHours(7).ToString("dd/MM/yyyy HH:mm") },
                { "Mã đơn hàng", code },
                { "Khách hàng", customer + (string.IsNullOrEmpty(phone) ? "" : $" ({phone})") },
                { "Loại giao dịch", typeName },
                { "Phương thức", method },
                { "Số tiền (đ)", t.Amount },
                { "Thu/Chi", flowType },
                { "Người thực hiện", GetUserDisplayName(t.PerformedBy) },
                { "Ghi chú", t.Notes ?? "" }
            };
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"ThongKeGiaoDich_{FromDate?.ToString("yyyyMMdd")}_{ToDate?.ToString("yyyyMMdd")}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private void CalculateStatistics(List<Transaction> data)
    {
        TotalIncome = 0;
        TotalExpense = 0;
        CashIncome = 0;
        CashExpense = 0;
        TransferIncome = 0;
        TransferExpense = 0;

        foreach (var t in data)
        {
            // Xác định giao dịch là Thu hay Chi
            var isIn = t.Type == "DEPOSIT_RECEIVED" || t.Type == "RENTAL_PAYMENT" || t.Type == "PENALTY_PAYMENT" || t.Type == "DEPOSIT_REFUNDED_CANCEL" || t.Type == "SALE_PAYMENT";
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
