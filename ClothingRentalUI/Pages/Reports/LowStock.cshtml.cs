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
    public int? StockThreshold { get; set; } = 0;

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int PageSize => 20;

    public int OutOfStockCount { get; set; }
    public int TotalStockSum { get; set; }
    public int TotalRentedSum { get; set; }

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
                      (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_LOW_STOCK")));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        // Mặc định ngưỡng tồn kho là 0 nếu người dùng không truyền giá trị
        if (!StockThreshold.HasValue)
        {
            StockThreshold = 0;
        }

        var query = BuildQuery();

        var allFiltered = await query
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .ToListAsync();

        TotalItems = allFiltered.Count;
        OutOfStockCount = allFiltered.Count(p => p.StockQuantity <= 0);
        TotalStockSum = allFiltered.Sum(p => p.StockQuantity);
        TotalRentedSum = allFiltered.Sum(p => p.RentedQuantity);

        // Phân trang
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
                      (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_LOW_STOCK")));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        if (!StockThreshold.HasValue)
        {
            StockThreshold = 0;
        }

        var query = BuildQuery();

        var list = await query
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .ToListAsync();

        var excelData = list.Select((p, index) => {
            string statusStr = "Hoạt động";
            if (p.IsLiquidated) statusStr = "Đã thanh lý";
            else if (!p.IsAvailable) statusStr = "Tạm khóa";

            return new Dictionary<string, object> {
                { "STT", index + 1 },
                { "Mã sản phẩm", p.Code },
                { "Tên sản phẩm", p.Name },
                { "Danh mục", p.Category?.Name ?? "Chưa phân loại" },
                { "Trạng thái", statusStr },
                { "Tồn kho thực tế", p.StockQuantity },
                { "Đang cho thuê", p.RentedQuantity },
                { "Giá thuê/ngày (đ)", p.PriceList?.PricePerDay ?? 0 },
                { "Giá trị cọc (đ)", p.PriceList?.Deposit ?? 0 }
            };
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"BaoCaoTonKho_{DateTime.UtcNow.AddHours(7):yyyyMMdd_HHmmss}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private IQueryable<Product> BuildQuery()
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.PriceList)
            .AsQueryable();

        // 1. Tìm kiếm theo từ khóa (Mã, Tên, Danh mục)
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var clean = SearchTerm.Trim().ToLower();
            query = query.Where(p => p.Code.ToLower().Contains(clean) || 
                                     p.Name.ToLower().Contains(clean) || 
                                     (p.Category != null && p.Category.Name.ToLower().Contains(clean)));
        }

        // 2. Hai filter: StockThreshold (Tồn kho <=) và Status (Trạng thái) áp dụng điều kiện OR
        var threshold = StockThreshold ?? 0;
        var status = Status?.Trim();

        if (!string.IsNullOrEmpty(status))
        {
            // Điều kiện OR: (Tồn kho <= threshold) HOẶC (Trạng thái thỏa mãn)
            switch (status)
            {
                case "Active":
                    query = query.Where(p => (p.StockQuantity <= threshold) || (!p.IsLiquidated && p.IsAvailable));
                    break;
                case "Locked":
                    query = query.Where(p => (p.StockQuantity <= threshold) || (!p.IsLiquidated && !p.IsAvailable));
                    break;
                case "Liquidated":
                    query = query.Where(p => (p.StockQuantity <= threshold) || p.IsLiquidated);
                    break;
                default:
                    query = query.Where(p => p.StockQuantity <= threshold);
                    break;
            }
        }
        else
        {
            // Nếu không chọn trạng thái: Lọc theo ngưỡng tồn kho <= threshold
            query = query.Where(p => p.StockQuantity <= threshold);
        }

        return query;
    }
}
