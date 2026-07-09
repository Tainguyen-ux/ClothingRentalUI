using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Pages.Products;

public class CategoriesModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public CategoriesModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public IList<Category> Categories { get; set; } = new List<Category>();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private class AuditLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    private async Task<IActionResult?> VerifyAccessAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null || user.IsLocked) return RedirectToPage("/Auth/Login");

        if (user.Role == "Admin") return null;

        var hasPermission = user.UserPermissions.Any(up => up.Permission.Code == "CATEGORY_VIEW" || up.Permission.Code == "CATEGORY_EDIT");
        if (!hasPermission)
        {
            ErrorMessage = "Bạn không có quyền truy cập chức năng này.";
            return RedirectToPage("/Clothes/Index");
        }

        return null;
    }
    
    private async Task<bool> VerifyEditAccessAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return false;

        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null || user.IsLocked) return false;
        if (user.Role == "Admin") return true;

        return user.UserPermissions.Any(up => up.Permission.Code == "CATEGORY_EDIT");
    }

    private void AppendSystemLog(Category category, string action, string details, string username)
    {
        List<AuditLogEntry> logs = new();
        if (!string.IsNullOrWhiteSpace(category.SystemLog))
        {
            try
            {
                logs = JsonSerializer.Deserialize<List<AuditLogEntry>>(category.SystemLog) ?? new List<AuditLogEntry>();
            }
            catch { }
        }

        logs.Add(new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Username = username,
            Action = action,
            Details = details
        });

        category.SystemLog = JsonSerializer.Serialize(logs);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authResult = await VerifyAccessAsync();
        if (authResult != null) return authResult;

        Categories = await _context.Categories.OrderBy(c => c.Id).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string name, string prefixCode, string description)
    {
        if (!await VerifyEditAccessAsync())
        {
            ErrorMessage = "Bạn không có quyền thực thi thao tác này.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prefixCode))
        {
            ErrorMessage = "Tên loại và Tiếp đầu ngữ không được bỏ trống.";
            return RedirectToPage();
        }

        var exists = await _context.Categories.AnyAsync(c => c.CodePrefix.ToUpper() == prefixCode.ToUpper());
        if (exists)
        {
            ErrorMessage = "Tiếp đầu ngữ này đã tồn tại.";
            return RedirectToPage();
        }

        var username = HttpContext.Session.GetString("Username") ?? "Unknown";

        var newCategory = new Category
        {
            Name = name.Trim(),
            CodePrefix = prefixCode.Trim().ToUpper(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        AppendSystemLog(newCategory, "CREATE", $"Tạo danh mục mới: {newCategory.Name} ({newCategory.CodePrefix})", username);

        _context.Categories.Add(newCategory);
        await _context.SaveChangesAsync();

        SuccessMessage = "Thêm loại hàng hoá mới thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, string prefixCode, string description)
    {
        if (!await VerifyEditAccessAsync())
        {
            ErrorMessage = "Bạn không có quyền thực thi thao tác này.";
            return RedirectToPage();
        }

        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            ErrorMessage = "Loại hàng hoá không tồn tại.";
            return RedirectToPage();
        }

        var exists = await _context.Categories.AnyAsync(c => c.CodePrefix.ToUpper() == prefixCode.ToUpper() && c.Id != id);
        if (exists)
        {
            ErrorMessage = "Tiếp đầu ngữ này đã tồn tại ở loại khác.";
            return RedirectToPage();
        }

        var username = HttpContext.Session.GetString("Username") ?? "Unknown";
        
        var oldName = category.Name;
        var oldPrefix = category.CodePrefix;

        category.Name = name.Trim();
        category.CodePrefix = prefixCode.Trim().ToUpper();
        category.Description = description?.Trim();
        category.UpdatedAt = DateTime.UtcNow;

        string changes = $"Cập nhật từ [{oldName} - {oldPrefix}] thành [{category.Name} - {category.CodePrefix}]";
        AppendSystemLog(category, "UPDATE", changes, username);

        await _context.SaveChangesAsync();
        SuccessMessage = "Cập nhật loại hàng hoá thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        if (!await VerifyEditAccessAsync())
        {
            ErrorMessage = "Bạn không có quyền thực thi thao tác này.";
            return RedirectToPage();
        }

        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            ErrorMessage = "Loại hàng hoá không tồn tại.";
            return RedirectToPage();
        }

        category.IsActive = !category.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        var username = HttpContext.Session.GetString("Username") ?? "Unknown";
        var statusStr = category.IsActive ? "Mở khóa" : "Tạm khóa";
        
        AppendSystemLog(category, "TOGGLE_STATUS", $"Chuyển trạng thái thành: {statusStr}", username);

        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã {statusStr.ToLower()} loại hàng hoá.";
        return RedirectToPage();
    }
}
