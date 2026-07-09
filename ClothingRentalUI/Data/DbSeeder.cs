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
        context.Database.EnsureDeleted();
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
                new Permission { Code = "CATEGORY_EDIT", Name = "Chỉnh sửa loại hàng hoá", Type = "Action", Description = "Quyền thêm, sửa, khóa loại hàng hoá" }
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
                },
                new User
                {
                    Username = "staff",
                    PasswordHash = PasswordHasher.HashPassword("staff123"),
                    FullName = "Nhân viên cửa hàng",
                    Role = "Staff",
                    IsLocked = false,
                    Email = "staff@rental.com",
                    PhoneNumber = "0912345678",
                    TelegramId = "987654321"
                }
            );
            context.SaveChanges();
        }

        // 3. Seed UserPermissions (Many-to-Many)
        if (!context.UserPermissions.Any())
        {
            var adminUser = context.Users.First(u => u.Username == "admin");
            var staffUser = context.Users.First(u => u.Username == "staff");

            var allPermissions = context.Permissions.ToList();

            // Gán tất cả quyền cho admin
            foreach (var perm in allPermissions)
            {
                context.UserPermissions.Add(new UserPermission { UserId = adminUser.Id, PermissionId = perm.Id });
            }

            // Gán các quyền cơ bản cho staff (không bao gồm xem báo cáo, cấu hình chung, quản lý người dùng, tham số hệ thống)
            var staffPerms = allPermissions.Where(p => 
                p.Code != "REPORT_VIEW" && 
                p.Code != "CLOTHES_CREATE" && 
                p.Code != "SYSTEM_SETTINGS_VIEW" &&
                p.Code != "USER_MANAGEMENT_VIEW" &&
                p.Code != "SYSTEM_PARAMETERS_VIEW" &&
                p.Code != "SYSTEM_PARAMETERS_EDIT"
            );
            foreach (var perm in staffPerms)
            {
                context.UserPermissions.Add(new UserPermission { UserId = staffUser.Id, PermissionId = perm.Id });
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
            context.SystemSettings.Add(new SystemSetting
            {
                Key = "TelegramBot",
                ValueJson = "{\"BotToken\":\"\",\"ChatId\":\"\",\"Enabled\":false}"
            });
            context.SaveChanges();
        }

        // Seed Categories
        if (!context.Categories.Any())
        {
            context.Categories.AddRange(
                new Category { Name = "Áo dài", CodePrefix = "AD" },
                new Category { Name = "Vest", CodePrefix = "VS" },
                new Category { Name = "Váy cưới", CodePrefix = "VC" },
                new Category { Name = "Dạ hội", CodePrefix = "DH" }
            );
            context.SaveChanges();
        }

        // Seed PriceLists
        if (!context.PriceLists.Any())
        {
            context.PriceLists.AddRange(
                new PriceList { Name = "Áo dài phổ thông", PricePerDay = 250000, Deposit = 1000000 },
                new PriceList { Name = "Vest cao cấp", PricePerDay = 350000, Deposit = 1500000 },
                new PriceList { Name = "Váy cưới hoàng gia", PricePerDay = 1500000, Deposit = 5000000 },
                new PriceList { Name = "Đầm dạ hội sang trọng", PricePerDay = 500000, Deposit = 2000000 }
            );
            context.SaveChanges();
        }

        // Seed Products
        if (!context.Products.Any())
        {
            var categoryAoDai = context.Categories.First(c => c.CodePrefix == "AD");
            var categoryVest = context.Categories.First(c => c.CodePrefix == "VS");
            var categoryVayCuoi = context.Categories.First(c => c.CodePrefix == "VC");

            var priceAoDai = context.PriceLists.First(p => p.Name == "Áo dài phổ thông");
            var priceVest = context.PriceLists.First(p => p.Name == "Vest cao cấp");
            var priceVayCuoi = context.PriceLists.First(p => p.Name == "Váy cưới hoàng gia");

            var todayStr = DateTime.Now.ToString("yyyyMMdd");

            context.Products.AddRange(
                new Product
                {
                    Code = $"AD{todayStr}0001",
                    Name = "Áo dài truyền thống gấm đỏ",
                    StockQuantity = 5,
                    Color = "Đỏ",
                    Size = "M",
                    Description = "Áo dài chất liệu gấm cao cấp thêu họa tiết chim phượng tinh xảo, thích hợp cho lễ hội và đám hỏi.",
                    ImportPrice = 1200000,
                    PriceListId = priceAoDai.Id,
                    ImageUrl = "https://images.unsplash.com/photo-1621184455862-c163dfb30e0f?q=80&w=600",
                    IsAvailable = true,
                    TotalRentRevenue = 0
                },
                new Product
                {
                    Code = $"VS{todayStr}0002",
                    Name = "Vest nam hoàng gia lịch lãm",
                    StockQuantity = 3,
                    Color = "Xanh Navy",
                    Size = "L",
                    Description = "Bộ vest nam màu xanh Navy kiểu dáng Slim-fit hiện đại phong cách châu Âu quý phái.",
                    ImportPrice = 1800000,
                    PriceListId = priceVest.Id,
                    ImageUrl = "https://images.unsplash.com/photo-1594938298603-c8148c4dae35?q=80&w=600",
                    IsAvailable = true,
                    TotalRentRevenue = 0
                },
                new Product
                {
                    Code = $"VC{todayStr}0003",
                    Name = "Váy cưới công chúa trễ vai",
                    StockQuantity = 2,
                    Color = "Trắng",
                    Size = "S",
                    Description = "Váy cưới phủ kim sa lấp lánh đính đá quý cao cấp nâng dáng cô dâu trong ngày trọng đại.",
                    ImportPrice = 8000000,
                    PriceListId = priceVayCuoi.Id,
                    ImageUrl = "https://images.unsplash.com/photo-1594552072238-b8a33785b261?q=80&w=600",
                    IsAvailable = true,
                    TotalRentRevenue = 0
                }
            );
            context.SaveChanges();
        }
    }
}
