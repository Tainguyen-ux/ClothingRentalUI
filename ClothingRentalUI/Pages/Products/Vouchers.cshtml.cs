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

public class VouchersModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public VouchersModel(ClothingRentalDbContext context) { _context = context; }

    public IList<Voucher> Vouchers { get; set; } = new List<Voucher>();
    public List<string> CurrentUserPermissions { get; set; } = new();
    public bool IsAdmin { get; set; }

    [BindProperty(SupportsGet = true)] public string? SearchTerm { get; set; }
    [BindProperty(SupportsGet = true)] public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 15;

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync(string perm = "VOUCHER_VIEW")
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");
        var user = await _context.Users.Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return RedirectToPage("/Auth/Login");
        IsAdmin = user.Role == "Admin";
        CurrentUserPermissions = user.UserPermissions.Where(up => up.Permission != null).Select(up => up.Permission!.Code).ToList();
        if (!IsAdmin && !CurrentUserPermissions.Contains(perm)) return RedirectToPage("/Products/Index");
        return null;
    }

    private async Task SeedPermissionsAndMenuAsync()
    {
        var codes = new[] {
            ("VOUCHER_VIEW", "Xem Voucher"),
            ("VOUCHER_CREATE", "Thêm Voucher"),
            ("VOUCHER_EDIT", "Sửa Voucher"),
            ("VOUCHER_DELETE", "Xóa Voucher")
        };
        bool needsSave = false;
        var existing = await _context.Permissions.Select(p => p.Code).ToListAsync();
        foreach (var (code, name) in codes)
        {
            if (!existing.Contains(code)) { _context.Permissions.Add(new Permission { Code = code, Name = name, Type = "UI" }); needsSave = true; }
        }
        if (needsSave)
        {
            await _context.SaveChangesAsync();
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            var newPerms = await _context.Permissions.Where(p => p.Code.StartsWith("VOUCHER_")).ToListAsync();
            foreach (var admin in admins)
                foreach (var np in newPerms)
                    if (!await _context.UserPermissions.AnyAsync(up => up.UserId == admin.Id && up.PermissionId == np.Id))
                        _context.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = np.Id });
            await _context.SaveChangesAsync();
        }

        // Seed menu dưới nhóm Hàng hóa
        if (!await _context.Menus.AnyAsync(m => m.Url == "/Products/Vouchers"))
        {
            var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Name.Contains("Hàng") && m.ParentId == null);
            if (parentMenu != null)
            {
                var viewPerm = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == "VOUCHER_VIEW");
                _context.Menus.Add(new Menu { Name = "Mã giảm giá", Url = "/Products/Vouchers", Icon = "🎟️", ParentId = parentMenu.Id, DisplayOrder = 6, RequiredPermissionId = viewPerm?.Id });
                await _context.SaveChangesAsync();
            }
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await SeedPermissionsAndMenuAsync();
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.Vouchers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var s = SearchTerm.Trim().ToLower();
            query = query.Where(v => v.Code.ToLower().Contains(s) || v.Name.ToLower().Contains(s));
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Vouchers = await query.OrderByDescending(v => v.Id).Skip((PageIndex - 1) * PageSize).Take(PageSize).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string code, string name, string discountType, decimal discountValue,
        decimal? maxDiscountAmount, decimal minOrderAmount, int? maxUsageCount, DateTime startDate, DateTime endDate, string? description)
    {
        var authCheck = await VerifyAccessAsync("VOUCHER_CREATE");
        if (authCheck != null) return authCheck;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        { ErrorMessage = "Mã và Tên voucher là bắt buộc."; return RedirectToPage(); }

        code = code.Trim().ToUpper();
        if (await _context.Vouchers.AnyAsync(v => v.Code == code))
        { ErrorMessage = $"Mã voucher '{code}' đã tồn tại."; return RedirectToPage(); }

        _context.Vouchers.Add(new Voucher
        {
            Code = code,
            Name = name.Trim(),
            DiscountType = discountType ?? "FIXED",
            DiscountValue = discountValue,
            MaxDiscountAmount = maxDiscountAmount,
            MinOrderAmount = minOrderAmount,
            MaxUsageCount = maxUsageCount,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        SuccessMessage = $"Thêm voucher '{code}' thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAsync(int id, string code, string name, string discountType, decimal discountValue,
        decimal? maxDiscountAmount, decimal minOrderAmount, int? maxUsageCount, DateTime startDate, DateTime endDate, string? description)
    {
        var authCheck = await VerifyAccessAsync("VOUCHER_EDIT");
        if (authCheck != null) return authCheck;

        var voucher = await _context.Vouchers.FindAsync(id);
        if (voucher == null) { ErrorMessage = "Không tìm thấy voucher."; return RedirectToPage(); }

        code = code.Trim().ToUpper();
        if (await _context.Vouchers.AnyAsync(v => v.Code == code && v.Id != id))
        { ErrorMessage = $"Mã voucher '{code}' đã được sử dụng."; return RedirectToPage(); }

        voucher.Code = code;
        voucher.Name = name.Trim();
        voucher.DiscountType = discountType ?? "FIXED";
        voucher.DiscountValue = discountValue;
        voucher.MaxDiscountAmount = maxDiscountAmount;
        voucher.MinOrderAmount = minOrderAmount;
        voucher.MaxUsageCount = maxUsageCount;
        voucher.StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        voucher.EndDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
        voucher.Description = description?.Trim();
        await _context.SaveChangesAsync();
        SuccessMessage = "Cập nhật voucher thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("VOUCHER_EDIT");
        if (authCheck != null) return authCheck;
        var voucher = await _context.Vouchers.FindAsync(id);
        if (voucher == null) { ErrorMessage = "Không tìm thấy voucher."; return RedirectToPage(); }
        voucher.IsActive = !voucher.IsActive;
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã {(voucher.IsActive ? "kích hoạt" : "tạm ngưng")} voucher '{voucher.Code}'.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("VOUCHER_DELETE");
        if (authCheck != null) return authCheck;
        var voucher = await _context.Vouchers.FindAsync(id);
        if (voucher == null) { ErrorMessage = "Không tìm thấy voucher."; return RedirectToPage(); }
        if (voucher.UsedCount > 0) { ErrorMessage = "Không thể xóa voucher đã được sử dụng. Bạn có thể tạm ngưng thay thế."; return RedirectToPage(); }
        _context.Vouchers.Remove(voucher);
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã xóa voucher '{voucher.Code}'.";
        return RedirectToPage();
    }
}
