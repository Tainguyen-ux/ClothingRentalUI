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
using ClothingRentalUI.Models.Report;
using MiniExcelLibs;

namespace ClothingRentalUI.Pages.Reports;

public class ProductSalesModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public ProductSalesModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchKeyword { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int PageSize => 20;

    // Financial & Summary Metrics
    public int TotalProductsSold { get; set; }
    public int TotalQuantitySold { get; set; }
    public decimal TotalSalesRevenue { get; set; }
    public int TotalRemainingStock { get; set; }

    public List<ProductSaleReportDto> ReportData { get; set; } = new();

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
                      (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_PRODUCT_SALES")));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        // Ngày mặc định: Từ đầu tháng đến ngày hiện tại (múi giờ Việt Nam UTC+7)
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = new DateTime(todayVn.Year, todayVn.Month, 1);
        if (ToDate == null) ToDate = todayVn;

        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        // Lấy dữ liệu bán hàng từ SaleOrderDetails & SaleOrders
        var salesQuery = _context.SaleOrderDetails
            .Include(d => d.SaleOrder)
            .Include(d => d.Product)
                .ThenInclude(p => p.PriceList)
            .Where(d => d.SaleOrder != null && d.SaleOrder.Status != "Cancelled" &&
                        d.SaleOrder.SaleDate >= startUtc && d.SaleOrder.SaleDate < endUtc);

        var rawDetails = await salesQuery.ToListAsync();
        var categories = await _context.Categories.ToListAsync();

        // Gom nhóm theo sản phẩm
        var grouped = rawDetails
            .GroupBy(d => d.ProductId)
            .Select(g =>
            {
                var firstProduct = g.First().Product;
                var pCode = firstProduct?.Code ?? string.Empty;
                var categoryName = categories.FirstOrDefault(c => pCode.StartsWith(c.CodePrefix))?.Name ?? "Khác";

                var totalQty = g.Sum(x => x.Quantity);
                var totalRev = g.Sum(x => x.Quantity * x.Price);

                return new ProductSaleReportDto
                {
                    ProductId = g.Key,
                    ProductCode = pCode,
                    ProductName = firstProduct?.Name ?? "N/A",
                    CategoryName = categoryName,
                    Price = firstProduct?.PriceList?.PricePerDay ?? (g.FirstOrDefault()?.Price ?? 0),
                    TotalSoldQuantity = totalQty,
                    TotalRevenue = totalRev,
                    CurrentStockQuantity = firstProduct?.StockQuantity ?? 0,
                    IsAvailable = firstProduct?.IsAvailable ?? false,
                    WarningStockLevel = firstProduct?.WarningStockLevel ?? 0
                };
            }).AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var kw = SearchKeyword.Trim().ToLower();
            grouped = grouped.Where(p => p.ProductCode.ToLower().Contains(kw) || p.ProductName.ToLower().Contains(kw) || p.CategoryName.ToLower().Contains(kw));
        }

        var allList = grouped.OrderByDescending(p => p.TotalSoldQuantity).ThenBy(p => p.ProductName).ToList();

        // Tổng hợp thống kê
        TotalProductsSold = allList.Count;
        TotalQuantitySold = allList.Sum(p => p.TotalSoldQuantity);
        TotalSalesRevenue = allList.Sum(p => p.TotalRevenue);
        TotalRemainingStock = allList.Sum(p => p.CurrentStockQuantity);

        // Phân trang
        TotalItems = allList.Count;
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        ReportData = allList
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
                      (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_PRODUCT_SALES")));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = new DateTime(todayVn.Year, todayVn.Month, 1);
        if (ToDate == null) ToDate = todayVn;

        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        var salesQuery = _context.SaleOrderDetails
            .Include(d => d.SaleOrder)
            .Include(d => d.Product)
                .ThenInclude(p => p.PriceList)
            .Where(d => d.SaleOrder != null && d.SaleOrder.Status != "Cancelled" &&
                        d.SaleOrder.SaleDate >= startUtc && d.SaleOrder.SaleDate < endUtc);

        var rawDetails = await salesQuery.ToListAsync();
        var categories = await _context.Categories.ToListAsync();

        var grouped = rawDetails
            .GroupBy(d => d.ProductId)
            .Select(g =>
            {
                var firstProduct = g.First().Product;
                var pCode = firstProduct?.Code ?? string.Empty;
                var categoryName = categories.FirstOrDefault(c => pCode.StartsWith(c.CodePrefix))?.Name ?? "Khác";

                var totalQty = g.Sum(x => x.Quantity);
                var totalRev = g.Sum(x => x.Quantity * x.Price);

                return new ProductSaleReportDto
                {
                    ProductId = g.Key,
                    ProductCode = pCode,
                    ProductName = firstProduct?.Name ?? "N/A",
                    CategoryName = categoryName,
                    Price = firstProduct?.PriceList?.PricePerDay ?? (g.FirstOrDefault()?.Price ?? 0),
                    TotalSoldQuantity = totalQty,
                    TotalRevenue = totalRev,
                    CurrentStockQuantity = firstProduct?.StockQuantity ?? 0,
                    IsAvailable = firstProduct?.IsAvailable ?? false,
                    WarningStockLevel = firstProduct?.WarningStockLevel ?? 0
                };
            }).AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var kw = SearchKeyword.Trim().ToLower();
            grouped = grouped.Where(p => p.ProductCode.ToLower().Contains(kw) || p.ProductName.ToLower().Contains(kw) || p.CategoryName.ToLower().Contains(kw));
        }

        var allList = grouped.OrderByDescending(p => p.TotalSoldQuantity).ThenBy(p => p.ProductName).ToList();

        var excelData = allList.Select((p, index) => new Dictionary<string, object> {
            { "STT", index + 1 },
            { "Mã sản phẩm", p.ProductCode },
            { "Tên mặt hàng", p.ProductName },
            { "Danh mục", p.CategoryName },
            { "Giá bán (đ)", p.Price },
            { "Số lượng đã bán", p.TotalSoldQuantity },
            { "Doanh thu bán (đ)", p.TotalRevenue },
            { "Tồn kho còn lại", p.CurrentStockQuantity },
            { "Trạng thái kho", p.StockStatus }
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"BaoCaoMatHangDaBan_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
