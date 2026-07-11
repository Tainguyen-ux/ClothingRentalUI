using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Pages.Reports;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // 1. Kiểm tra đăng nhập qua Session
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        // 2. Kiểm tra quyền truy cập Báo cáo (REPORT_VIEW)
        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_VIEW"));

        if (!hasPermission)
        {
            // Trả về trang chủ nếu không đủ quyền hạn
            return RedirectToPage("/Clothes/Index");
        }

        // 3. Seed menus
        await SeedReportMenusAsync();

        return Page();
    }

    private async Task SeedReportMenusAsync()
    {
        var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Url == "/Reports/Index" || (m.Name == "Báo cáo thống kê" && m.ParentId == null));
        if (parentMenu != null)
        {
            bool needsSave = false;

            // Change parent menu URL to "#" if it isn't already
            if (parentMenu.Url != "#")
            {
                parentMenu.Url = "#";
                needsSave = true;
            }

            // Add "Tổng quan" submenu if not exists
            var hasSummary = await _context.Menus.AnyAsync(m => m.Url == "/Reports/Index" && m.ParentId == parentMenu.Id);
            if (!hasSummary)
            {
                _context.Menus.Add(new Menu
                {
                    Name = "Tổng quan",
                    Url = "/Reports/Index",
                    Icon = "📊",
                    ParentId = parentMenu.Id,
                    DisplayOrder = 1,
                    RequiredPermissionId = parentMenu.RequiredPermissionId
                });
                needsSave = true;
            }

            // Add "Thống kê giao dịch" submenu if not exists
            var hasTxnReport = await _context.Menus.AnyAsync(m => m.Url == "/Reports/Transactions" && m.ParentId == parentMenu.Id);
            if (!hasTxnReport)
            {
                _context.Menus.Add(new Menu
                {
                    Name = "Thống kê giao dịch",
                    Url = "/Reports/Transactions",
                    Icon = "💸",
                    ParentId = parentMenu.Id,
                    DisplayOrder = 2,
                    RequiredPermissionId = parentMenu.RequiredPermissionId
                });
                needsSave = true;
            }

            if (needsSave)
            {
                await _context.SaveChangesAsync();
            }
        }
    }
}
