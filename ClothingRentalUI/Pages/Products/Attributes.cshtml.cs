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

namespace ClothingRentalUI.Pages.Products;

public class AttributesModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public AttributesModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public IList<ProductAttribute> Attributes { get; set; } = new List<ProductAttribute>();

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 10;

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
                           u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "PRODUCT_ATTRIBUTE_VIEW"));

        if (!hasPermission)
        {
            return RedirectToPage("/Clothes/Index");
        }
        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.ProductAttributes.AsQueryable();

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Attributes = await query
            .OrderBy(a => a.Id)
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAttributeAsync(int id, string key, string displayName, string description, string status)
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var hasEditPermission = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "PRODUCT_ATTRIBUTE_EDIT"));

        if (!hasEditPermission)
        {
            ErrorMessage = "Bạn không có quyền thêm/sửa thuộc tính.";
            return RedirectToPage(new { PageIndex });
        }

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(displayName))
        {
            ErrorMessage = "Key và Tên hiển thị không được bỏ trống.";
            return RedirectToPage(new { PageIndex });
        }

        key = key.Trim().ToLower().Replace(" ", "_");

        if (id == 0) // Add
        {
            var exists = await _context.ProductAttributes.AnyAsync(a => a.Key == key);
            if (exists)
            {
                ErrorMessage = $"Mã thuộc tính '{key}' đã tồn tại.";
                return RedirectToPage(new { PageIndex });
            }

            var newAttr = new ProductAttribute
            {
                Key = key,
                DisplayName = displayName.Trim(),
                Description = description?.Trim(),
                IsActive = status == "on"
            };
            _context.ProductAttributes.Add(newAttr);
            SuccessMessage = "Thêm thuộc tính mới thành công.";
        }
        else // Edit
        {
            var attr = await _context.ProductAttributes.FindAsync(id);
            if (attr == null)
            {
                ErrorMessage = "Không tìm thấy thuộc tính.";
                return RedirectToPage(new { PageIndex });
            }

            var exists = await _context.ProductAttributes.AnyAsync(a => a.Key == key && a.Id != id);
            if (exists)
            {
                ErrorMessage = $"Mã thuộc tính '{key}' đã tồn tại ở bản ghi khác.";
                return RedirectToPage(new { PageIndex });
            }

            attr.Key = key;
            attr.DisplayName = displayName.Trim();
            attr.Description = description?.Trim();
            attr.IsActive = status == "on";

            SuccessMessage = "Cập nhật thuộc tính thành công.";
        }

        await _context.SaveChangesAsync();
        return RedirectToPage(new { PageIndex });
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id, int pageIndex)
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var hasEditPermission = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "PRODUCT_ATTRIBUTE_EDIT"));

        if (!hasEditPermission)
        {
            ErrorMessage = "Bạn không có quyền thực thi thao tác này.";
            return RedirectToPage(new { PageIndex = pageIndex });
        }

        var attr = await _context.ProductAttributes.FindAsync(id);
        if (attr == null)
        {
            ErrorMessage = "Không tìm thấy thuộc tính.";
            return RedirectToPage(new { PageIndex = pageIndex });
        }

        attr.IsActive = !attr.IsActive;
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã {(attr.IsActive ? "mở khóa" : "tạm khóa")} thuộc tính thành công.";
        return RedirectToPage(new { PageIndex = pageIndex });
    }
}
