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
using ClothingRentalUI.Helpers;

namespace ClothingRentalUI.Pages.Settings;

public class UsersModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public UsersModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public List<User> UsersList { get; set; } = new();
    public List<Permission> AllPermissions { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 10;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        // --- Tự động khởi tạo quyền Lịch sử nhập hàng nếu chưa có ---
        var permCode = "CLOTHES_IMPORT_HISTORY";
        var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == permCode);
        if (permission == null)
        {
            permission = new Permission { Code = permCode, Name = "Xem Lịch sử Nhập hàng", Type = "UI" };
            _context.Permissions.Add(permission);
            await _context.SaveChangesAsync();
            
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            foreach (var admin in admins)
            {
                _context.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = permission.Id });
            }
            await _context.SaveChangesAsync();
        }

        // Tự động khởi tạo Menu nếu chưa có
        var menu = await _context.Menus.FirstOrDefaultAsync(m => m.Url == "/Products/ImportHistory");
        if (menu == null)
        {
            var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Name.Contains("Hàng") && m.ParentId == null);
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
                await _context.SaveChangesAsync();
            }
        }

        if (PageIndex < 1) PageIndex = 1;

        var query = _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .OrderBy(u => u.Username);

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (TotalPages == 0) TotalPages = 1;

        UsersList = await query
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        AllPermissions = await _context.Permissions
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostCreateUserAsync(string username, string fullName, string password, string role, string email, string phoneNumber, string telegramId)
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
        {
            ErrorMessage = "Vui lòng nhập đầy đủ tên đăng nhập, họ tên và mật khẩu.";
            return RedirectToPage();
        }

        var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == username.Trim().ToLower());
        if (exists)
        {
            ErrorMessage = "Tên đăng nhập đã tồn tại trên hệ thống.";
            return RedirectToPage();
        }

        var newUser = new User
        {
            Username = username.Trim(),
            FullName = fullName.Trim(),
            PasswordHash = PasswordHasher.HashPassword(password),
            Role = role == "Admin" ? "Admin" : "Staff",
            IsLocked = false,
            Email = email?.Trim() ?? string.Empty,
            PhoneNumber = phoneNumber?.Trim() ?? string.Empty,
            TelegramId = telegramId?.Trim() ?? string.Empty
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        // Gán các quyền mặc định nếu là Staff hoặc tất cả nếu là Admin
        var allPerms = await _context.Permissions.ToListAsync();
        if (newUser.Role == "Admin")
        {
            foreach (var perm in allPerms)
            {
                _context.UserPermissions.Add(new UserPermission { UserId = newUser.Id, PermissionId = perm.Id });
            }
        }
        else
        {
            // Staff chỉ có quyền cơ bản
            var staffPerms = allPerms.Where(p => 
                p.Code != "REPORT_VIEW" && 
                p.Code != "CLOTHES_CREATE" && 
                p.Code != "SYSTEM_SETTINGS_VIEW"
            );
            foreach (var perm in staffPerms)
            {
                _context.UserPermissions.Add(new UserPermission { UserId = newUser.Id, PermissionId = perm.Id });
            }
        }
        await _context.SaveChangesAsync();

        SuccessMessage = $"Đã tạo thành công tài khoản: {newUser.Username}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateUserAsync(int userId, string username, string fullName, string role, string? newPassword, string email, string phoneNumber, string telegramId)
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            ErrorMessage = "Không tìm thấy thông tin người dùng.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
        {
            ErrorMessage = "Tên đăng nhập và họ tên không được để trống.";
            return RedirectToPage();
        }

        var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == username.Trim().ToLower() && u.Id != userId);
        if (exists)
        {
            ErrorMessage = "Tên đăng nhập đã được sử dụng bởi tài khoản khác.";
            return RedirectToPage();
        }

        user.Username = username.Trim();
        user.FullName = fullName.Trim();
        user.Role = role == "Admin" ? "Admin" : "Staff";
        user.Email = email?.Trim() ?? string.Empty;
        user.PhoneNumber = phoneNumber?.Trim() ?? string.Empty;
        user.TelegramId = telegramId?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        }

        await _context.SaveChangesAsync();

        // Nếu người dùng hiện tại tự sửa thông tin của mình, cập nhật lại Session
        var currentUsername = HttpContext.Session.GetString("Username");
        if (currentUsername != null && currentUsername.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
        {
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Role", user.Role);
        }

        SuccessMessage = "Cập nhật thông tin tài khoản thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleLockAsync(int userId)
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            ErrorMessage = "Không tìm thấy người dùng.";
            return RedirectToPage();
        }

        var currentUsername = HttpContext.Session.GetString("Username");
        if (currentUsername != null && user.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Bạn không thể tự khóa tài khoản của chính mình.";
            return RedirectToPage();
        }

        user.IsLocked = !user.IsLocked;
        await _context.SaveChangesAsync();

        SuccessMessage = user.IsLocked 
            ? $"Đã khóa tài khoản: {user.Username}." 
            : $"Đã mở khóa tài khoản: {user.Username}.";
            
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdatePermissionsAsync(int userId, List<int>? selectedPermissions)
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            ErrorMessage = "Không tìm thấy người dùng.";
            return RedirectToPage();
        }

        // Xóa tất cả quyền cũ
        var oldPermissions = _context.UserPermissions.Where(up => up.UserId == userId);
        _context.UserPermissions.RemoveRange(oldPermissions);

        // Thêm quyền mới
        if (selectedPermissions != null)
        {
            foreach (var permId in selectedPermissions)
            {
                _context.UserPermissions.Add(new UserPermission
                {
                    UserId = userId,
                    PermissionId = permId
                });
            }
        }

        await _context.SaveChangesAsync();
        SuccessMessage = $"Cập nhật phân quyền thành công cho tài khoản: {user.Username}.";
        return RedirectToPage();
    }

    private async Task<IActionResult?> VerifyAdminAccessAsync()
    {
        // 1. Kiểm tra đăng nhập qua Session
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        // 2. Kiểm tra quyền truy cập Quản lý người dùng (USER_MANAGEMENT_VIEW)
        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "USER_MANAGEMENT_VIEW"));

        if (!hasPermission)
        {
            return RedirectToPage("/Clothes/Index");
        }

        return null;
    }
}
