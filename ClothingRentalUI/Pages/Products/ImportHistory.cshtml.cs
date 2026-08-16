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

namespace ClothingRentalUI.Pages.Products;

public class ImportHistoryModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public ImportHistoryModel(ClothingRentalDbContext context)
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
    public const int PageSize = 25;

    public List<StockHistory> Histories { get; set; } = new List<StockHistory>();

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAndSeedAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        // Check user permission
        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "CLOTHES_IMPORT_HISTORY")));

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

        // Xử lý logic Múi giờ Việt Nam (UTC+7)
        DateTime vnNow = DateTime.UtcNow.AddHours(7);
        DateTime vnFrom = FromDate ?? vnNow.Date;
        DateTime vnTo = ToDate ?? vnNow.Date;

        FromDate = vnFrom;
        ToDate = vnTo;

        var startUtc = DateTime.SpecifyKind(vnFrom.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(vnTo.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        var query = _context.StockHistories
            .Include(s => s.Product)
            .Where(s => s.ActionType == "IMPORT" && s.CreatedAt >= startUtc && s.CreatedAt < endUtc);

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Histories = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
    {
        var authCheck = await VerifyAccessAndSeedAsync();
        if (authCheck != null) return authCheck;

        DateTime vnNow = DateTime.UtcNow.AddHours(7);
        DateTime vnFrom = FromDate ?? vnNow.Date;
        DateTime vnTo = ToDate ?? vnNow.Date;

        FromDate = vnFrom;
        ToDate = vnTo;

        var startUtc = DateTime.SpecifyKind(vnFrom.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(vnTo.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        var list = await _context.StockHistories
            .Include(s => s.Product)
            .Where(s => s.ActionType == "IMPORT" && s.CreatedAt >= startUtc && s.CreatedAt < endUtc)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var excelData = list.Select((s, index) => new Dictionary<string, object> {
            { "STT", index + 1 },
            { "Thời gian", s.CreatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm") },
            { "Mã sản phẩm", s.Product?.Code ?? "" },
            { "Tên sản phẩm", s.Product?.Name ?? "" },
            { "Số lượng nhập", s.QuantityChange },
            { "Người thực hiện", s.PerformedBy },
            { "Mã tham chiếu", s.ReferenceCode ?? "" },
            { "Ghi chú", s.Note ?? "" }
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"LichSuNhapHang_{vnFrom:yyyyMMdd}_{vnTo:yyyyMMdd}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
