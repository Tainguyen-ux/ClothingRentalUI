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

public class LowStockModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public LowStockModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int PageSize => 20;

    public List<Product> ProductsData { get; set; } = new();

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
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_LOW_STOCK"));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        var query = _context.Products
            .Include(p => p.Category)
            .Where(p => !p.IsLiquidated && p.WarningStockLevel > 0 && p.StockQuantity <= p.WarningStockLevel);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var clean = SearchTerm.Trim().ToLower();
            query = query.Where(p => p.Code.ToLower().Contains(clean) || 
                                     p.Name.ToLower().Contains(clean) || 
                                     (p.Category != null && p.Category.Name.ToLower().Contains(clean)));
        }

        var allFiltered = await query
            .OrderBy(p => p.StockQuantity)
            .ToListAsync();

        // Phân trang
        TotalItems = allFiltered.Count;
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        ProductsData = allFiltered
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToList();

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
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_LOW_STOCK"));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

         var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.PriceList)
            .Where(p => !p.IsLiquidated && p.WarningStockLevel > 0 && p.StockQuantity <= p.WarningStockLevel);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var clean = SearchTerm.Trim().ToLower();
            query = query.Where(p => p.Code.ToLower().Contains(clean) || 
                                     p.Name.ToLower().Contains(clean) || 
                                     (p.Category != null && p.Category.Name.ToLower().Contains(clean)));
        }

        var list = await query.OrderBy(p => p.StockQuantity).ToListAsync();

        var excelData = list.Select((p, index) => new Dictionary<string, object> {
            { "STT", index + 1 },
            { "Mã sản phẩm", p.Code },
            { "Tên sản phẩm", p.Name },
            { "Danh mục", p.Category?.Name ?? "" },
            { "Tồn kho hiện tại", p.StockQuantity },
            { "Ngưỡng cảnh báo", p.WarningStockLevel },
            { "Giá thuê/ngày (đ)", p.PriceList?.PricePerDay ?? 0 },
            { "Giá trị cọc (đ)", p.PriceList?.Deposit ?? 0 }
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"BaoCaoCanhBaoTonKho_{DateTime.UtcNow.AddHours(7):yyyyMMdd_HHmmss}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
