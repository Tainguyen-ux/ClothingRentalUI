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
using ClothingRentalUI.Services;

namespace ClothingRentalUI.Pages.Products;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public IList<Product> Products { get; set; } = new List<Product>();
    public IList<Category> Categories { get; set; } = new List<Category>();

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 10;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CategoryId { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "CLOTHES_VIEW"));

        if (!hasPermission)
        {
            return RedirectToPage("/Home/Index"); // Assuming a generic fallback
        }
        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();

        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.PriceList)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            query = query.Where(p => p.Name.ToLower().Contains(SearchTerm.ToLower()) || p.Code.ToLower().Contains(SearchTerm.ToLower()));
        }

        if (CategoryId.HasValue && CategoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == CategoryId.Value);
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Products = await query
            .OrderByDescending(p => p.Id)
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            ErrorMessage = "Không tìm thấy sản phẩm.";
            return RedirectToPage();
        }

        product.IsAvailable = !product.IsAvailable;
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã {(product.IsAvailable ? "mở khóa" : "tạm khóa")} sản phẩm thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetRentalHistoryAsync(int productId)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

        var history = await _context.OrderDetails
            .Include(od => od.Order)
            .Where(od => od.ProductId == productId)
            .OrderByDescending(od => od.Order.CreatedDate)
            .Select(od => new
            {
                orderId = od.Order.Code,
                createdAt = od.Order.CreatedDate,
                rentDays = od.RentDays,
                extendedDays = od.ExtendedDays,
                isReturned = od.IsReturned
            })
            .ToListAsync();

        return new JsonResult(new { success = true, history });
    }
}
