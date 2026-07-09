using System;
using System.Linq;
using System.Collections.Generic;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Helpers;

namespace ClothingRentalUI.Data;

public static class DbSeeder
{
    public static void Seed(ClothingRentalDbContext context)
    {
        // context.Database.EnsureDeleted(); // Removed to prevent data reset
        context.Database.EnsureCreated();

        // 1. Seed Permissions
        if (!context.Permissions.Any())
        {
            context.Permissions.AddRange(
                new Permission { Code = "CLOTHES_VIEW", Name = "Xem trang phục", Type = "UI", Description = "Hiển thị trang phục trên danh mục" },
                new Permission { Code = "CLOTHES_CREATE", Name = "Thêm trang phục", Type = "Action", Description = "Quyền nhập kho trang phục mới" },
                new Permission { Code = "ORDER_CREATE", Name = "Tạo đơn thuê", Type = "Action", Description = "Quyền lập đơn thuê đồ" },
                new Permission { Code = "ORDER_CLOSE", Name = "Đóng đơn hàng", Type = "Action", Description = "Quyền trả đồ và đóng đơn hàng" },
                new Permission { Code = "REPORT_VIEW", Name = "Xem báo cáo", Type = "UI", Description = "Hiển thị menu báo cáo doanh thu" },
                new Permission { Code = "SYSTEM_SETTINGS_VIEW", Name = "Cấu hình hệ thống", Type = "UI", Description = "Hiển thị menu cấu hình hệ thống" },
                new Permission { Code = "USER_MANAGEMENT_VIEW", Name = "Xem quản lý người dùng", Type = "UI", Description = "Hiển thị menu quản lý người dùng" },
                new Permission { Code = "SYSTEM_PARAMETERS_VIEW", Name = "Xem tham số hệ thống", Type = "UI", Description = "Hiển thị menu tham số hệ thống" },
                new Permission { Code = "SYSTEM_PARAMETERS_EDIT", Name = "Chỉnh sửa tham số hệ thống", Type = "Action", Description = "Quyền lưu cấu hình và test kết nối Telegram Bot" },
                new Permission { Code = "CATEGORY_VIEW", Name = "Xem loại hàng hoá", Type = "UI", Description = "Hiển thị menu quản lý loại hàng hoá" },
                new Permission { Code = "CATEGORY_EDIT", Name = "Chỉnh sửa loại hàng hoá", Type = "Action", Description = "Quyền thêm, sửa, khóa loại hàng hoá" },
                new Permission { Code = "PRICELIST_VIEW", Name = "Xem loại giá", Type = "UI", Description = "Hiển thị menu quản lý loại giá" },
                new Permission { Code = "PRICELIST_EDIT", Name = "Chỉnh sửa loại giá", Type = "Action", Description = "Quyền thêm, sửa, khóa loại giá" }
            );
            context.SaveChanges();
        }

        // 2. Seed Users
        if (!context.Users.Any())
        {
            context.Users.AddRange(
                new User
                {
                    Username = "admin",
                    PasswordHash = PasswordHasher.HashPassword("admin123"),
                    FullName = "Quản trị viên",
                    Role = "Admin",
                    IsLocked = false,
                    Email = "admin@rental.com",
                    PhoneNumber = "0987654321",
                    TelegramId = "123456789"
                }
            );
            context.SaveChanges();
        }

        // 3. Seed UserPermissions (Many-to-Many)
        if (!context.UserPermissions.Any())
        {
            var adminUser = context.Users.First(u => u.Username == "admin");
            var allPermissions = context.Permissions.ToList();

            // Gán tất cả quyền cho admin
            foreach (var perm in allPermissions)
            {
                context.UserPermissions.Add(new UserPermission { UserId = adminUser.Id, PermissionId = perm.Id });
            }

            context.SaveChanges();
        }

        // 4. Seed Menus
        if (!context.Menus.Any())
        {
            var reportPerm = context.Permissions.First(p => p.Code == "REPORT_VIEW");
            var settingsPerm = context.Permissions.First(p => p.Code == "SYSTEM_SETTINGS_VIEW");
            var userMgmtPerm = context.Permissions.First(p => p.Code == "USER_MANAGEMENT_VIEW");
            var sysParamsPerm = context.Permissions.First(p => p.Code == "SYSTEM_PARAMETERS_VIEW");
            var categoryViewPerm = context.Permissions.First(p => p.Code == "CATEGORY_VIEW");
            var priceListViewPerm = context.Permissions.First(p => p.Code == "PRICELIST_VIEW");

            // Tạo các menu gốc
            var homeMenu = new Menu { Name = "Trang chủ", Url = "/Clothes/Index", Icon = "👕", DisplayOrder = 1 };
            var productMenu = new Menu { Name = "Hàng hoá", Url = "/Products/Categories", Icon = "📦", DisplayOrder = 2, RequiredPermissionId = categoryViewPerm.Id };
            var orderMenu = new Menu { Name = "Đơn thuê đồ", Url = "/Orders/Index", Icon = "📋", DisplayOrder = 3 };
            var reportMenu = new Menu { Name = "Báo cáo thống kê", Url = "/Reports/Index", Icon = "📊", DisplayOrder = 4, RequiredPermissionId = reportPerm.Id };
            var settingsMenu = new Menu { Name = "Cấu hình hệ thống", Url = "/Settings/Users", Icon = "⚙️", DisplayOrder = 5, RequiredPermissionId = settingsPerm.Id };

            context.Menus.AddRange(homeMenu, productMenu, orderMenu, reportMenu, settingsMenu);
            context.SaveChanges();

            // Thêm menu con "Quản lý loại hàng" dưới "Hàng hoá"
            context.Menus.Add(new Menu 
            { 
                Name = "Quản lý loại hàng", 
                Url = "/Products/Categories", 
                Icon = "🏷️", 
                DisplayOrder = 1, 
                RequiredPermissionId = categoryViewPerm.Id,
                ParentId = productMenu.Id 
            });
            
            // Thêm menu con "Quản lý loại giá" dưới "Hàng hoá"
            context.Menus.Add(new Menu 
            { 
                Name = "Quản lý loại giá", 
                Url = "/Products/PriceLists", 
                Icon = "💲", 
                DisplayOrder = 2, 
                RequiredPermissionId = priceListViewPerm.Id,
                ParentId = productMenu.Id 
            });
            context.SaveChanges();

            // Thêm menu con "Quản lý người dùng" dưới "Cấu hình hệ thống"
            context.Menus.Add(new Menu 
            { 
                Name = "Quản lý người dùng", 
                Url = "/Settings/Users", 
                Icon = "👥", 
                DisplayOrder = 1, 
                RequiredPermissionId = userMgmtPerm.Id,
                ParentId = settingsMenu.Id 
            });
            context.SaveChanges();

            // Thêm menu con "Tham số hệ thống" dưới "Cấu hình hệ thống"
            context.Menus.Add(new Menu 
            { 
                Name = "Tham số hệ thống", 
                Url = "/Settings/SystemSettings", 
                Icon = "⚙️", 
                DisplayOrder = 2, 
                RequiredPermissionId = sysParamsPerm.Id,
                ParentId = settingsMenu.Id 
            });
            context.SaveChanges();
        }

        // 5. Seed SystemSettings
        if (!context.SystemSettings.Any())
        {
            context.SystemSettings.AddRange(
                new SystemSetting { Key = "Telegram_BotToken", ValueJson = "{\"value\":\"\",\"description\":\"Token của Telegram Bot\"}" },
                new SystemSetting { Key = "Telegram_ChatId", ValueJson = "{\"value\":\"\",\"description\":\"ID của nhóm chat Telegram\"}" },
                new SystemSetting { Key = "Telegram_Enabled", ValueJson = "{\"value\":\"false\",\"description\":\"Kích hoạt gửi thông báo qua Telegram (true/false)\"}" },
                new SystemSetting { Key = "GoogleDrive_FolderId", ValueJson = "{\"value\":\"\",\"description\":\"ID của thư mục Google Drive để lưu trữ hình ảnh\"}" }
            );
            context.SaveChanges();
        }

    }
    }
}
