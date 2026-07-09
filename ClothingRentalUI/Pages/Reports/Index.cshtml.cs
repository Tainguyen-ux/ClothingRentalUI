using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;

namespace ClothingRentalUI.Pages.Reports;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
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

        // 2. Kiểm tra quyền truy cập Báo cáo (REPORT_VIEW)
        var hasPermission = _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .Any(u => u.Username.ToLower() == username.ToLower() && 
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_VIEW"));

        if (!hasPermission)
        {
            // Trả về trang chủ nếu không đủ quyền hạn
            return RedirectToPage("/Clothes/Index");
        }

        return Page();
    }
}
