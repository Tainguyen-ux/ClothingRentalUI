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

public class PriceListsModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public PriceListsModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public IList<PriceList> PriceLists { get; set; } = new List<PriceList>();

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 10;

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

        var hasPermission = user.UserPermissions.Any(up => up.Permission.Code == "PRICELIST_VIEW" || up.Permission.Code == "PRICELIST_EDIT");
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

        return user.UserPermissions.Any(up => up.Permission.Code == "PRICELIST_EDIT");
    }

    private void AppendSystemLog(PriceList priceList, string action, string details, string username)
    {
        List<AuditLogEntry> logs = new();
        if (!string.IsNullOrWhiteSpace(priceList.SystemLog))
        {
            try
            {
                logs = JsonSerializer.Deserialize<List<AuditLogEntry>>(priceList.SystemLog) ?? new List<AuditLogEntry>();
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

        priceList.SystemLog = JsonSerializer.Serialize(logs);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authResult = await VerifyAccessAsync();
        if (authResult != null) return authResult;

        if (PageIndex < 1) PageIndex = 1;

        var query = _context.PriceLists.OrderBy(p => p.Id);
        
        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (TotalPages == 0) TotalPages = 1;

        PriceLists = await query
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
            
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string name, decimal pricePerDay, decimal deposit, string description)
    {
        if (!await VerifyEditAccessAsync())
        {
            ErrorMessage = "Bạn không có quyền thực thi thao tác này.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Tên loại giá không được bỏ trống.";
            return RedirectToPage();
        }
        
        if (pricePerDay < 0 || deposit < 0)
        {
            ErrorMessage = "Giá thuê và Tiền cọc không được là số âm.";
            return RedirectToPage();
        }

        var exists = await _context.PriceLists.AnyAsync(p => p.Name.ToLower() == name.Trim().ToLower());
        if (exists)
        {
            ErrorMessage = "Tên loại giá này đã tồn tại.";
            return RedirectToPage();
        }

        var username = HttpContext.Session.GetString("Username") ?? "Unknown";

        var newPriceList = new PriceList
        {
            Name = name.Trim(),
            PricePerDay = pricePerDay,
            Deposit = deposit,
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        AppendSystemLog(newPriceList, "CREATE", $"Tạo loại giá mới: {newPriceList.Name} (Thuê: {pricePerDay:N0}đ, Cọc: {deposit:N0}đ)", username);

        _context.PriceLists.Add(newPriceList);
        await _context.SaveChangesAsync();

        SuccessMessage = "Thêm loại giá mới thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, decimal pricePerDay, decimal deposit, string description)
    {
        if (!await VerifyEditAccessAsync())
        {
            ErrorMessage = "Bạn không có quyền thực thi thao tác này.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Tên loại giá không được bỏ trống.";
            return RedirectToPage();
        }
        
        if (pricePerDay < 0 || deposit < 0)
        {
            ErrorMessage = "Giá thuê và Tiền cọc không được là số âm.";
            return RedirectToPage();
        }

        var priceList = await _context.PriceLists.FindAsync(id);
        if (priceList == null)
        {
            ErrorMessage = "Loại giá không tồn tại.";
            return RedirectToPage();
        }

        var exists = await _context.PriceLists.AnyAsync(p => p.Name.ToLower() == name.Trim().ToLower() && p.Id != id);
        if (exists)
        {
            ErrorMessage = "Tên loại giá này đã bị trùng lặp.";
            return RedirectToPage();
        }

        var username = HttpContext.Session.GetString("Username") ?? "Unknown";
        
        var oldName = priceList.Name;
        var oldPrice = priceList.PricePerDay;
        var oldDeposit = priceList.Deposit;
        var oldDesc = priceList.Description;

        priceList.Name = name.Trim();
        priceList.PricePerDay = pricePerDay;
        priceList.Deposit = deposit;
        priceList.Description = description?.Trim();
        priceList.UpdatedAt = DateTime.UtcNow;

        string changes = $"Cập nhật loại giá.";
        if (oldName != priceList.Name) changes += $" Tên: [{oldName}] -> [{priceList.Name}].";
        if (oldPrice != priceList.PricePerDay) changes += $" Giá thuê: [{oldPrice:N0}đ] -> [{priceList.PricePerDay:N0}đ].";
        if (oldDeposit != priceList.Deposit) changes += $" Tiền cọc: [{oldDeposit:N0}đ] -> [{priceList.Deposit:N0}đ].";
        if (oldDesc != priceList.Description)
        {
            changes += $" Ghi chú: [{(string.IsNullOrWhiteSpace(oldDesc) ? "Trống" : oldDesc)}] -> [{(string.IsNullOrWhiteSpace(priceList.Description) ? "Trống" : priceList.Description)}].";
        }

        if (changes == "Cập nhật loại giá.") 
        {
            changes = "Cập nhật loại giá nhưng không có trường nào thay đổi.";
        }

        AppendSystemLog(priceList, "UPDATE", changes, username);

        await _context.SaveChangesAsync();
        SuccessMessage = "Cập nhật thông tin loại giá thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        if (!await VerifyEditAccessAsync())
        {
            ErrorMessage = "Bạn không có quyền thực thi thao tác này.";
            return RedirectToPage();
        }

        var priceList = await _context.PriceLists.FindAsync(id);
        if (priceList == null)
        {
            ErrorMessage = "Loại giá không tồn tại.";
            return RedirectToPage();
        }

        priceList.IsActive = !priceList.IsActive;
        priceList.UpdatedAt = DateTime.UtcNow;

        var username = HttpContext.Session.GetString("Username") ?? "Unknown";
        var statusStr = priceList.IsActive ? "Mở khóa (áp dụng)" : "Tạm khóa (ngừng áp dụng)";
        
        AppendSystemLog(priceList, "TOGGLE_STATUS", $"Chuyển trạng thái thành: {statusStr}", username);

        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã chuyển trạng thái loại giá thành {statusStr.ToLower()}.";
        return RedirectToPage();
    }
}
