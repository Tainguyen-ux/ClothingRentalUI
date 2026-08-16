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

namespace ClothingRentalUI.Pages.Settings;

public class RoleGroupsModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public RoleGroupsModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public List<RoleGroup> RoleGroupsList { get; set; } = new();
    public List<Permission> AllPermissions { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.RoleGroups
            .Include(rg => rg.RoleGroupPermissions)
                .ThenInclude(rgp => rgp.Permission)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim().ToLower();
            query = query.Where(rg =>
                rg.Name.ToLower().Contains(term) ||
                (rg.Description != null && rg.Description.ToLower().Contains(term))
            );
        }

        RoleGroupsList = await query
            .OrderBy(rg => rg.Id)
            .ToListAsync();

        AllPermissions = await _context.Permissions
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostCreateRoleGroupAsync(string name, string? description, List<int>? selectedPermissions)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Tên nhóm quyền không được để trống.";
            return RedirectToPage();
        }

        var exists = await _context.RoleGroups.AnyAsync(rg => rg.Name.ToLower() == name.Trim().ToLower());
        if (exists)
        {
            ErrorMessage = "Tên nhóm quyền đã tồn tại. Vui lòng chọn tên khác.";
            return RedirectToPage();
        }

        var newGroup = new RoleGroup
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.RoleGroups.Add(newGroup);
        await _context.SaveChangesAsync();

        if (selectedPermissions != null && selectedPermissions.Any())
        {
            foreach (var permId in selectedPermissions.Distinct())
            {
                _context.RoleGroupPermissions.Add(new RoleGroupPermission
                {
                    RoleGroupId = newGroup.Id,
                    PermissionId = permId
                });
            }
            await _context.SaveChangesAsync();
        }

        SuccessMessage = $"Đã tạo thành công nhóm quyền: \"{newGroup.Name}\".";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateRoleGroupAsync(int roleGroupId, string name, string? description, List<int>? selectedPermissions)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var group = await _context.RoleGroups
            .Include(rg => rg.RoleGroupPermissions)
            .FirstOrDefaultAsync(rg => rg.Id == roleGroupId);

        if (group == null)
        {
            ErrorMessage = "Không tìm thấy nhóm quyền.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Tên nhóm quyền không được để trống.";
            return RedirectToPage();
        }

        var exists = await _context.RoleGroups.AnyAsync(rg => rg.Name.ToLower() == name.Trim().ToLower() && rg.Id != roleGroupId);
        if (exists)
        {
            ErrorMessage = "Tên nhóm quyền đã tồn tại trên hệ thống.";
            return RedirectToPage();
        }

        group.Name = name.Trim();
        group.Description = description?.Trim();

        // Xóa danh sách quyền cũ của nhóm
        _context.RoleGroupPermissions.RemoveRange(group.RoleGroupPermissions);

        // Thêm danh sách quyền mới cho nhóm
        var newPermIds = (selectedPermissions != null && selectedPermissions.Any())
            ? selectedPermissions.Distinct().ToList()
            : new List<int>();

        foreach (var permId in newPermIds)
        {
            _context.RoleGroupPermissions.Add(new RoleGroupPermission
            {
                RoleGroupId = group.Id,
                PermissionId = permId
            });
        }

        // Tự động đồng bộ lại quyền cho tất cả các tài khoản thuộc nhóm quyền này
        var usersInGroup = await _context.Users
            .Where(u => u.RoleGroupId == group.Id)
            .ToListAsync();

        foreach (var u in usersInGroup)
        {
            var oldUserPerms = _context.UserPermissions.Where(up => up.UserId == u.Id);
            _context.UserPermissions.RemoveRange(oldUserPerms);

            foreach (var permId in newPermIds)
            {
                _context.UserPermissions.Add(new UserPermission
                {
                    UserId = u.Id,
                    PermissionId = permId
                });
            }
        }

        await _context.SaveChangesAsync();
        SuccessMessage = $"Cập nhật nhóm quyền: \"{group.Name}\" và tự động đồng bộ cho {usersInGroup.Count} tài khoản thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteRoleGroupAsync(int roleGroupId)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var group = await _context.RoleGroups.FindAsync(roleGroupId);
        if (group == null)
        {
            ErrorMessage = "Không tìm thấy nhóm quyền cần xóa.";
            return RedirectToPage();
        }

        var groupName = group.Name;
        _context.RoleGroups.Remove(group);
        await _context.SaveChangesAsync();

        SuccessMessage = $"Đã xóa nhóm quyền: \"{groupName}\".";
        return RedirectToPage();
    }

    private async Task<IActionResult?> VerifyAccessAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           (u.Role == "Admin" || u.UserPermissions.Any(up => up.Permission != null && 
                            (up.Permission.Code == "USER_MANAGEMENT_VIEW" || up.Permission.Code == "ROLE_GROUP_VIEW"))));

        if (!hasPermission)
        {
            return RedirectToPage("/Clothes/Index");
        }

        return null;
    }
}
