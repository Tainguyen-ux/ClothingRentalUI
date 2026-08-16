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
    public List<RoleGroup> AllRoleGroups { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RoleFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

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
                    Icon = "🕒",
                    ParentId = parentMenu.Id,
                    DisplayOrder = 5,
                    RequiredPermissionId = permission.Id
                };
                _context.Menus.Add(menu);
                await _context.SaveChangesAsync();
            }
        }

        // --- Tự động khởi tạo quyền Voucher nếu chưa có ---
        var voucherCodes = new[] {
            ("VOUCHER_VIEW", "Xem Voucher", "Xem danh sách mã giảm giá"),
            ("VOUCHER_CREATE", "Thêm Voucher", "Tạo mã giảm giá mới"),
            ("VOUCHER_EDIT", "Sửa Voucher", "Chỉnh sửa thông tin mã giảm giá"),
            ("VOUCHER_DELETE", "Xóa Voucher", "Xóa mã giảm giá")
        };
        bool hasNewVoucherPerm = false;
        foreach (var (code, name, desc) in voucherCodes)
        {
            var existingPerm = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (existingPerm == null)
            {
                _context.Permissions.Add(new Permission { Code = code, Name = name, Type = "UI", Description = desc });
                hasNewVoucherPerm = true;
            }
        }
        if (hasNewVoucherPerm)
        {
            await _context.SaveChangesAsync();
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            var newPerms = await _context.Permissions.Where(p => p.Code.StartsWith("VOUCHER_")).ToListAsync();
            foreach (var admin in admins)
            {
                foreach (var np in newPerms)
                {
                    if (!await _context.UserPermissions.AnyAsync(up => up.UserId == admin.Id && up.PermissionId == np.Id))
                    {
                        _context.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = np.Id });
                    }
                }
            }
            await _context.SaveChangesAsync();
        }

        // Tự động khởi tạo Menu Voucher nếu chưa có
        var voucherMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Url == "/Products/Vouchers");
        if (voucherMenu == null)
        {
            var parentMenu = await _context.Menus.FirstOrDefaultAsync(m => m.Name.Contains("Hàng") && m.ParentId == null);
            if (parentMenu != null)
            {
                var viewPerm = await _context.Permissions.FirstOrDefaultAsync(p => p.Code == "VOUCHER_VIEW");
                if (viewPerm != null)
                {
                    voucherMenu = new Menu
                    {
                        Name = "Mã giảm giá",
                        Url = "/Products/Vouchers",
                        Icon = "🎟️",
                        ParentId = parentMenu.Id,
                        DisplayOrder = 6,
                        RequiredPermissionId = viewPerm.Id
                    };
                    _context.Menus.Add(voucherMenu);
                    await _context.SaveChangesAsync();
                }
            }
        }

        if (PageIndex < 1) PageIndex = 1;

        var query = _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .Include(u => u.RoleGroup)
            .AsQueryable();

        // 1. Lọc theo từ khóa tìm kiếm (Username, FullName, Email, SĐT, Telegram)
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim().ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)) ||
                (u.TelegramId != null && u.TelegramId.ToLower().Contains(term))
            );
        }

        // 2. Lọc theo vai trò (Role)
        if (!string.IsNullOrWhiteSpace(RoleFilter))
        {
            query = query.Where(u => u.Role == RoleFilter);
        }

        // 3. Lọc theo trạng thái (Status)
        if (!string.IsNullOrWhiteSpace(StatusFilter))
        {
            if (StatusFilter == "active")
            {
                query = query.Where(u => !u.IsLocked);
            }
            else if (StatusFilter == "locked")
            {
                query = query.Where(u => u.IsLocked);
            }
        }

        query = query.OrderBy(u => u.Username);

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

        AllRoleGroups = await _context.RoleGroups
            .Include(rg => rg.RoleGroupPermissions)
            .OrderBy(rg => rg.Id)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostCreateUserAsync(string username, string fullName, string password, string role, string email, string phoneNumber, string telegramId, int? roleGroupId)
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
            TelegramId = telegramId?.Trim() ?? string.Empty,
            RoleGroupId = (roleGroupId.HasValue && roleGroupId.Value > 0) ? roleGroupId.Value : null
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        // Nếu có chọn nhóm quyền mẫu (Role Group), gán toàn bộ quyền của nhóm quyền đó
        if (roleGroupId.HasValue && roleGroupId.Value > 0)
        {
            var groupPerms = await _context.RoleGroupPermissions
                .Where(rgp => rgp.RoleGroupId == roleGroupId.Value)
                .Select(rgp => rgp.PermissionId)
                .ToListAsync();

            foreach (var permId in groupPerms.Distinct())
            {
                _context.UserPermissions.Add(new UserPermission { UserId = newUser.Id, PermissionId = permId });
            }
        }
        else
        {
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
        }
        await _context.SaveChangesAsync();

        SuccessMessage = $"Đã tạo thành công tài khoản: {newUser.Username}.";
        return RedirectWithCurrentFilters();
    }

    public async Task<IActionResult> OnPostUpdateUserAsync(int userId, string username, string fullName, string role, string? newPassword, string email, string phoneNumber, string telegramId, int? roleGroupId)
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            ErrorMessage = "Không tìm thấy thông tin người dùng.";
            return RedirectWithCurrentFilters();
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
        {
            ErrorMessage = "Tên đăng nhập và họ tên không được để trống.";
            return RedirectWithCurrentFilters();
        }

        var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == username.Trim().ToLower() && u.Id != userId);
        if (exists)
        {
            ErrorMessage = "Tên đăng nhập đã được sử dụng bởi tài khoản khác.";
            return RedirectWithCurrentFilters();
        }

        var oldRoleGroupId = user.RoleGroupId;
        user.Username = username.Trim();
        user.FullName = fullName.Trim();
        user.Role = role == "Admin" ? "Admin" : "Staff";
        user.Email = email?.Trim() ?? string.Empty;
        user.PhoneNumber = phoneNumber?.Trim() ?? string.Empty;
        user.TelegramId = telegramId?.Trim() ?? string.Empty;
        user.RoleGroupId = (roleGroupId.HasValue && roleGroupId.Value > 0) ? roleGroupId.Value : null;

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        }

        // Nếu nhóm quyền thay đổi khi chỉnh sửa user, tự động cập nhật lại toàn bộ quyền theo nhóm mới
        if (user.RoleGroupId != oldRoleGroupId && user.RoleGroupId.HasValue)
        {
            var newGroupPerms = await _context.RoleGroupPermissions
                .Where(rgp => rgp.RoleGroupId == user.RoleGroupId.Value)
                .Select(rgp => rgp.PermissionId)
                .Distinct()
                .ToListAsync();

            var oldUserPerms = _context.UserPermissions.Where(up => up.UserId == user.Id);
            _context.UserPermissions.RemoveRange(oldUserPerms);

            foreach (var permId in newGroupPerms)
            {
                _context.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionId = permId });
            }
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
        return RedirectWithCurrentFilters();
    }

    public async Task<IActionResult> OnPostToggleLockAsync(int userId)
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            ErrorMessage = "Không tìm thấy người dùng.";
            return RedirectWithCurrentFilters();
        }

        var currentUsername = HttpContext.Session.GetString("Username");
        if (currentUsername != null && user.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Bạn không thể tự khóa tài khoản của chính mình.";
            return RedirectWithCurrentFilters();
        }

        user.IsLocked = !user.IsLocked;
        await _context.SaveChangesAsync();

        SuccessMessage = user.IsLocked 
            ? $"Đã khóa tài khoản: {user.Username}." 
            : $"Đã mở khóa tài khoản: {user.Username}.";
            
        return RedirectWithCurrentFilters();
    }

    public async Task<IActionResult> OnPostUpdatePermissionsAsync(int userId, int? roleGroupId, List<int>? selectedPermissions)
    {
        var authCheck = await VerifyAdminAccessAsync();
        if (authCheck != null) return authCheck;

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            ErrorMessage = "Không tìm thấy người dùng.";
            return RedirectWithCurrentFilters();
        }

        // Cập nhật nhóm quyền mà tài khoản thuộc về (nếu có)
        user.RoleGroupId = (roleGroupId.HasValue && roleGroupId.Value > 0) ? roleGroupId.Value : null;

        // Xóa tất cả quyền cũ
        var oldPermissions = _context.UserPermissions.Where(up => up.UserId == userId);
        _context.UserPermissions.RemoveRange(oldPermissions);

        // Thêm quyền mới
        if (selectedPermissions != null && selectedPermissions.Any())
        {
            foreach (var permId in selectedPermissions.Distinct())
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
        return RedirectWithCurrentFilters();
    }

    private IActionResult RedirectWithCurrentFilters()
    {
        return RedirectToPage(new
        {
            pageIndex = PageIndex > 1 ? (int?)PageIndex : null,
            searchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
            roleFilter = string.IsNullOrWhiteSpace(RoleFilter) ? null : RoleFilter,
            statusFilter = string.IsNullOrWhiteSpace(StatusFilter) ? null : StatusFilter
        });
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
