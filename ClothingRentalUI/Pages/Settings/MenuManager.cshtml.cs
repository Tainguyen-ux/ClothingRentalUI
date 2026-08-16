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

namespace ClothingRentalUI.Pages.Settings;

public class MenuManagerModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public MenuManagerModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public List<Menu> ParentMenusList { get; set; } = new();
    public List<Permission> AllPermissionsList { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        // Lấy tất cả menu và quyền liên kết
        var allMenus = await _context.Menus
            .Include(m => m.RequiredPermission)
            .Include(m => m.SubMenus)
                .ThenInclude(sm => sm.RequiredPermission)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();

        // Lọc menu cha
        ParentMenusList = allMenus
            .Where(m => m.ParentId == null)
            .OrderBy(m => m.DisplayOrder)
            .ToList();

        // Đảm bảo menu con trong từng menu cha cũng được sắp xếp theo DisplayOrder
        foreach (var parent in ParentMenusList)
        {
            parent.SubMenus = parent.SubMenus.OrderBy(sm => sm.DisplayOrder).ToList();
        }

        // Lấy tất cả quyền hạn
        AllPermissionsList = await _context.Permissions
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateMenuAsync(int menuId, string name, string? icon, int displayOrder, int? requiredPermissionId)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var menu = await _context.Menus.FindAsync(menuId);
        if (menu == null)
        {
            ErrorMessage = "Không tìm thấy thông tin menu cần cập nhật.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Tên hiển thị menu không được để trống.";
            return RedirectToPage();
        }

        menu.Name = name.Trim();
        menu.Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        menu.DisplayOrder = displayOrder;
        menu.RequiredPermissionId = requiredPermissionId > 0 ? requiredPermissionId : null;

        await _context.SaveChangesAsync();

        SuccessMessage = $"Cập nhật menu \"{menu.Name}\" thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdatePermissionAsync(int permissionId, string name, string? description)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var permission = await _context.Permissions.FindAsync(permissionId);
        if (permission == null)
        {
            ErrorMessage = "Không tìm thấy quyền hạn cần cập nhật.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Tên quyền hạn không được để trống.";
            return RedirectToPage();
        }

        permission.Name = name.Trim();
        permission.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        await _context.SaveChangesAsync();

        SuccessMessage = $"Cập nhật quyền hạn [{permission.Code}] - \"{permission.Name}\" thành công.";
        return RedirectToPage();
    }

    private async Task<IActionResult?> VerifyAccessAsync()
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
                           (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && 
                            (up.Permission.Code == "USER_MANAGEMENT_VIEW" || up.Permission.Code == "SYSTEM_SETTINGS_VIEW"))));

        if (!hasPermission)
        {
            return RedirectToPage("/Clothes/Index");
        }

        return null;
    }
}
