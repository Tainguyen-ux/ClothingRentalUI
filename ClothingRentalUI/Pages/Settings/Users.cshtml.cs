using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;

namespace ClothingRentalUI.Pages.Settings;

public class UsersModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public UsersModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public IActionResult OnGet()
    {
        // 1. Kiểm tra đăng nhập qua Session
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        // 2. Kiểm tra quyền truy cập Cấu hình hệ thống (SYSTEM_SETTINGS_VIEW)
        var hasPermission = _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .Any(u => u.Username.ToLower() == username.ToLower() && 
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "SYSTEM_SETTINGS_VIEW"));

        if (!hasPermission)
        {
            // Trả về trang chủ hoặc trang lỗi nếu không đủ quyền hạn
            return RedirectToPage("/Clothes/Index");
        }

        return Page();
    }
}
