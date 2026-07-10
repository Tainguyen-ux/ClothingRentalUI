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

public class ImportHistoryModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public ImportHistoryModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    public List<StockHistory> Histories { get; set; } = new List<StockHistory>();

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAndSeedAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        // Self-healing: Check & Seed Permission & Menu
        bool needsSave = false;

        // 1. Check Permission
        var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == "CLOTHES_IMPORT_HISTORY");
        if (permission == null)
        {
            permission = new Permission
            {
                Code = "CLOTHES_IMPORT_HISTORY",
                Name = "Xem Lịch sử Nhập hàng",
                Type = "UI"
            };
            _context.Permissions.Add(permission);
            needsSave = true;
        }

        if (needsSave) await _context.SaveChangesAsync(); // Save to get Permission ID

        // 2. Check Menu
        var menu = await _context.Menus.FirstOrDefaultAsync(m => m.Url == "/Products/ImportHistory");
        if (menu == null)
        {
            var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Name == "Hàng hóa" && m.ParentId == null);
            if (parentMenu != null)
            {
                menu = new Menu
                {
                    Name = "Lịch sử nhập hàng",
                    Url = "/Products/ImportHistory",
                    Icon = "fas fa-history",
                    ParentId = parentMenu.Id,
                    DisplayOrder = 5,
                    RequiredPermissionId = permission.Id
                };
                _context.Menus.Add(menu);
                needsSave = true;
            }
        }

        // 3. Grant to Admins if new permission was created
        if (needsSave)
        {
            await _context.SaveChangesAsync(); // Save to get Menu ID

            var admins = await _context.Users
                .Include(u => u.UserPermissions)
                .Where(u => u.Role == "Admin")
                .ToListAsync();

            foreach (var admin in admins)
            {
                if (!admin.UserPermissions.Any(up => up.PermissionId == permission.Id))
                {
                    _context.UserPermissions.Add(new UserPermission
                    {
                        UserId = admin.Id,
                        PermissionId = permission.Id
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        // Check user permission
        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "CLOTHES_IMPORT_HISTORY")));

        if (!hasPermission)
        {
            return RedirectToPage("/Products/Index");
        }

        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAndSeedAsync();
        if (authCheck != null) return authCheck;

        // Xử lý logic Múi giờ Việt Nam (UTC+7)
        DateTime vnNow = DateTime.UtcNow.AddHours(7);
        DateTime vnFrom = FromDate ?? vnNow.Date;
        DateTime vnTo = ToDate ?? vnNow.Date;

        FromDate = vnFrom;
        ToDate = vnTo;

        var startUtc = vnFrom.AddHours(-7);
        var endUtc = vnTo.AddDays(1).AddHours(-7);

        Histories = await _context.StockHistories
            .Include(s => s.Product)
            .Where(s => s.ActionType == "IMPORT" && s.CreatedAt >= startUtc && s.CreatedAt < endUtc)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Page();
    }
}
