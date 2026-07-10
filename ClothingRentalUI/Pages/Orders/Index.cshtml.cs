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

namespace ClothingRentalUI.Pages.Orders;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public IndexModel(ClothingRentalDbContext context) { _context = context; }

    public IList<Order> Orders { get; set; } = new List<Order>();
    public List<string> CurrentUserPermissions { get; set; } = new();
    public bool IsAdmin { get; set; }

    [BindProperty(SupportsGet = true)] public string? SearchTerm { get; set; }
    [BindProperty(SupportsGet = true)] public string? StatusFilter { get; set; }
    [BindProperty(SupportsGet = true)] public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 15;

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync(string perm = "ORDER_VIEW")
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

    private async Task SeedPermissionsAndMenusAsync()
    {
        var codes = new[] {
            ("ORDER_VIEW", "Xem Đơn hàng"), ("ORDER_CREATE", "Tạo Đơn hàng"), ("ORDER_DETAIL", "Xem Chi tiết Đơn"),
            ("ORDER_CONFIRM", "Xác nhận Đơn"), ("ORDER_RETURN", "Trả hàng"), ("ORDER_CLOSE", "Đóng Đơn hàng"), ("ORDER_DELETE", "Xóa Đơn hàng")
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
            var newPerms = await _context.Permissions.Where(p => p.Code.StartsWith("ORDER_")).ToListAsync();
            foreach (var admin in admins)
                foreach (var np in newPerms)
                    if (!await _context.UserPermissions.AnyAsync(up => up.UserId == admin.Id && up.PermissionId == np.Id))
                        _context.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = np.Id });
            await _context.SaveChangesAsync();
        }

        // Seed menu
        var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Name.Contains("Đơn hàng") && m.ParentId == null);
        if (parentMenu == null)
        {
            parentMenu = new Menu { Name = "Đơn hàng", Url = "#", Icon = "📋", ParentId = null, DisplayOrder = 2, RequiredPermissionId = null };
            _context.Menus.Add(parentMenu);
            await _context.SaveChangesAsync();
        }

        if (!await _context.Menus.AnyAsync(m => m.Url == "/Orders/Index"))
        {
            var viewPerm = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == "ORDER_VIEW");
            _context.Menus.Add(new Menu { Name = "Danh sách đơn", Url = "/Orders/Index", Icon = "📝", ParentId = parentMenu.Id, DisplayOrder = 1, RequiredPermissionId = viewPerm?.Id });
            await _context.SaveChangesAsync();
        }
        if (!await _context.Menus.AnyAsync(m => m.Url == "/Orders/Create"))
        {
            var createPerm = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == "ORDER_CREATE");
            _context.Menus.Add(new Menu { Name = "Tạo đơn mới", Url = "/Orders/Create", Icon = "➕", ParentId = parentMenu.Id, DisplayOrder = 2, RequiredPermissionId = createPerm?.Id });
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await SeedPermissionsAndMenusAsync();
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.Orders.Include(o => o.Customer).Include(o => o.CreatedByUser).Include(o => o.OrderDetails).AsQueryable();

        if (!string.IsNullOrWhiteSpace(StatusFilter))
            query = query.Where(o => o.Status == StatusFilter);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var s = SearchTerm.Trim().ToLower();
            query = query.Where(o => o.Code.ToLower().Contains(s)
                || (o.Customer != null && o.Customer.FullName.ToLower().Contains(s))
                || (o.Customer != null && o.Customer.PhoneNumber.Contains(s)));
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Orders = await query.OrderByDescending(o => o.Id).Skip((PageIndex - 1) * PageSize).Take(PageSize).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("ORDER_DELETE");
        if (authCheck != null) return authCheck;
        var order = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) { ErrorMessage = "Không tìm thấy đơn hàng."; return RedirectToPage(); }
        if (order.Status != "Draft") { ErrorMessage = "Chỉ có thể xóa đơn hàng ở trạng thái Nháp."; return RedirectToPage(); }
        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã xóa đơn hàng {order.Code}.";
        return RedirectToPage();
    }
}
