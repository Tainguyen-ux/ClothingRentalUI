using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using MiniExcelLibs;

namespace ClothingRentalUI.Pages.Customers;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public IndexModel(ClothingRentalDbContext context) { _context = context; }

    public IList<Customer> Customers { get; set; } = new List<Customer>();
    public List<string> CurrentUserPermissions { get; set; } = new();
    public bool IsAdmin { get; set; }

    [BindProperty(SupportsGet = true)] public string? SearchTerm { get; set; }
    [BindProperty(SupportsGet = true)] public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 15;

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync(string perm = "CUSTOMER_VIEW")
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

    private async Task SeedPermissionsAsync()
    {
        var codes = new[] {
            ("CUSTOMER_VIEW", "Xem Khách hàng"),
            ("CUSTOMER_CREATE", "Thêm Khách hàng"),
            ("CUSTOMER_EDIT", "Sửa Khách hàng")
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
            var newPerms = await _context.Permissions.Where(p => p.Code.StartsWith("CUSTOMER_")).ToListAsync();
            foreach (var admin in admins)
                foreach (var np in newPerms)
                    if (!await _context.UserPermissions.AnyAsync(up => up.UserId == admin.Id && up.PermissionId == np.Id))
                        _context.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = np.Id });
            await _context.SaveChangesAsync();
        }

        // Seed menu
        var menuExists = await _context.Menus.AnyAsync(m => m.Url == "/Customers/Index");
        if (!menuExists)
        {
            var viewPerm = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == "CUSTOMER_VIEW");
            _context.Menus.Add(new Menu { Name = "Khách hàng", Url = "/Customers/Index", Icon = "👥", ParentId = null, DisplayOrder = 3, RequiredPermissionId = viewPerm?.Id });
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await SeedPermissionsAsync();
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var s = SearchTerm.Trim().ToLower();
            query = query.Where(c => c.FullName.ToLower().Contains(s) || c.PhoneNumber.Contains(s));
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Customers = await query.OrderByDescending(c => c.Id).Skip((PageIndex - 1) * PageSize).Take(PageSize).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var s = SearchTerm.Trim().ToLower();
            query = query.Where(c => c.FullName.ToLower().Contains(s) || c.PhoneNumber.Contains(s));
        }

        var list = await query.OrderByDescending(c => c.Id).ToListAsync();

        var excelData = list.Select((c, index) => new Dictionary<string, object> {
            { "STT", index + 1 },
            { "Họ tên", c.FullName },
            { "Số điện thoại", c.PhoneNumber },
            { "Số CCCD", c.IdentityCard ?? "" },
            { "Địa chỉ", c.Address ?? "" },
            { "Ghi chú", c.Notes ?? "" },
            { "Trạng thái", c.Status == "Active" ? "Bình thường" : "Nợ xấu" },
            { "Ngày đăng ký", c.CreatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm") }
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"DanhSachKhachHang_{DateTime.UtcNow.AddHours(7):yyyyMMdd_HHmmss}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public async Task<IActionResult> OnPostCreateAsync(string fullName, string phoneNumber, string? identityCard, string? address, string? notes)
    {
        var authCheck = await VerifyAccessAsync("CUSTOMER_CREATE");
        if (authCheck != null) return authCheck;

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneNumber))
        { ErrorMessage = "Họ tên và Số điện thoại là bắt buộc."; return RedirectToPage(); }

        if (await _context.Customers.AnyAsync(c => c.PhoneNumber == phoneNumber.Trim()))
        { ErrorMessage = "Số điện thoại đã tồn tại trong hệ thống."; return RedirectToPage(); }

        _context.Customers.Add(new Customer
        {
            FullName = fullName.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            IdentityCard = identityCard?.Trim(),
            Address = address?.Trim(),
            Notes = notes?.Trim(),
            Status = "Active",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        SuccessMessage = "Thêm khách hàng thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAsync(int id, string fullName, string phoneNumber, string? identityCard, string? address, string? notes)
    {
        var authCheck = await VerifyAccessAsync("CUSTOMER_EDIT");
        if (authCheck != null) return authCheck;

        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) { ErrorMessage = "Không tìm thấy khách hàng."; return RedirectToPage(); }

        if (await _context.Customers.AnyAsync(c => c.PhoneNumber == phoneNumber.Trim() && c.Id != id))
        { ErrorMessage = "Số điện thoại đã được sử dụng bởi khách hàng khác."; return RedirectToPage(); }

        customer.FullName = fullName.Trim();
        customer.PhoneNumber = phoneNumber.Trim();
        customer.IdentityCard = identityCard?.Trim();
        customer.Address = address?.Trim();
        customer.Notes = notes?.Trim();
        await _context.SaveChangesAsync();
        SuccessMessage = "Cập nhật khách hàng thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleBlacklistAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("CUSTOMER_EDIT");
        if (authCheck != null) return authCheck;
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) { ErrorMessage = "Không tìm thấy khách hàng."; return RedirectToPage(); }
        customer.Status = customer.Status == "Active" ? "Blacklisted" : "Active";
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã {(customer.Status == "Blacklisted" ? "đánh dấu nợ xấu" : "gỡ đánh dấu nợ xấu")} khách hàng.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetSearchAjaxAsync(string term)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false });
        var results = await _context.Customers
            .Where(c => c.FullName.ToLower().Contains(term.ToLower()) || c.PhoneNumber.Contains(term))
            .Take(10)
            .Select(c => new { c.Id, c.FullName, c.PhoneNumber, c.IdentityCard, c.Status })
            .ToListAsync();
        return new JsonResult(new { success = true, data = results });
    }
}
