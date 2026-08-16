using System;
using System.Collections.Generic;
using System.Linq;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Data;

public class PermissionTreeItem
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public Permission? MainPermission { get; set; }
    public List<Permission> ActionPermissions { get; set; } = new();
    public List<PermissionTreeItem> Children { get; set; } = new();
}

public static class PermissionGrouper
{
    public static List<PermissionTreeItem> BuildSidebarTree(IEnumerable<Permission> allPerms)
    {
        var permMap = allPerms.ToDictionary(p => p.Code.ToUpperInvariant(), p => p);
        var usedCodes = new HashSet<string>();

        Permission? GetPerm(string code)
        {
            if (permMap.TryGetValue(code.ToUpperInvariant(), out var p))
            {
                usedCodes.Add(code.ToUpperInvariant());
                return p;
            }
            return null;
        }

        List<Permission> GetPerms(params string[] codes)
        {
            var list = new List<Permission>();
            foreach (var c in codes)
            {
                var p = GetPerm(c);
                if (p != null) list.Add(p);
            }
            return list;
        }

        var tree = new List<PermissionTreeItem>();

        // 1. Hàng hoá (Products)
        var productGroup = new PermissionTreeItem
        {
            Key = "products",
            Name = "Hàng hoá",
            Icon = "📦",
            Children = new List<PermissionTreeItem>
            {
                new PermissionTreeItem
                {
                    Key = "products-list",
                    Name = "Danh sách sản phẩm",
                    Icon = "📋",
                    MainPermission = GetPerm("CLOTHES_VIEW"),
                    ActionPermissions = GetPerms("CLOTHES_CREATE", "CLOTHES_EDIT", "CLOTHES_LOCK", "CLOTHES_DELETE")
                },
                new PermissionTreeItem
                {
                    Key = "products-categories",
                    Name = "Loại hàng hóa",
                    Icon = "🗂️",
                    MainPermission = GetPerm("CATEGORY_VIEW"),
                    ActionPermissions = GetPerms("CATEGORY_CREATE", "CATEGORY_EDIT", "CATEGORY_LOCK")
                },
                new PermissionTreeItem
                {
                    Key = "products-attributes",
                    Name = "Thuộc tính sản phẩm",
                    Icon = "🏷️",
                    MainPermission = GetPerm("PRODUCT_ATTRIBUTE_VIEW"),
                    ActionPermissions = GetPerms("PRODUCT_ATTRIBUTE_CREATE", "PRODUCT_ATTRIBUTE_EDIT", "PRODUCT_ATTRIBUTE_LOCK")
                },
                new PermissionTreeItem
                {
                    Key = "products-pricelists",
                    Name = "Bảng giá thuê",
                    Icon = "💰",
                    MainPermission = GetPerm("PRICELIST_VIEW"),
                    ActionPermissions = GetPerms("PRICELIST_CREATE", "PRICELIST_EDIT", "PRICELIST_LOCK", "PRICELIST_DELETE")
                },
                new PermissionTreeItem
                {
                    Key = "products-import",
                    Name = "Lịch sử nhập hàng",
                    Icon = "📥",
                    MainPermission = GetPerm("CLOTHES_IMPORT_HISTORY")
                },
                new PermissionTreeItem
                {
                    Key = "products-liquidate",
                    Name = "Thanh lý sản phẩm",
                    Icon = "🗑️",
                    MainPermission = GetPerm("CLOTHES_LIQUIDATE_VIEW"),
                    ActionPermissions = GetPerms("CLOTHES_LIQUIDATE_CREATE", "CLOTHES_LIQUIDATE_CANCEL", "CLOTHES_LIQUIDATE")
                }
            }
        };
        tree.Add(productGroup);

        // 2. Đơn hàng (Orders)
        var orderGroup = new PermissionTreeItem
        {
            Key = "orders",
            Name = "Đơn hàng",
            Icon = "📋",
            Children = new List<PermissionTreeItem>
            {
                new PermissionTreeItem
                {
                    Key = "orders-rental-list",
                    Name = "Danh sách đơn thuê",
                    Icon = "📝",
                    MainPermission = GetPerm("ORDER_VIEW"),
                    ActionPermissions = GetPerms(
                        "ORDER_DETAIL", "ORDER_CONFIRM", "ORDER_RETURN", "ORDER_CLOSE", 
                        "ORDER_DELETE", "ORDER_REOPEN", "TRANSACTION_CANCEL", "TRANSACTION_CANCEL_ANY"
                    )
                },
                new PermissionTreeItem
                {
                    Key = "orders-rental-create",
                    Name = "Đơn thuê",
                    Icon = "➕",
                    MainPermission = GetPerm("ORDER_CREATE")
                },
                new PermissionTreeItem
                {
                    Key = "orders-sale-list",
                    Name = "Danh sách đơn mua",
                    Icon = "🛍️",
                    MainPermission = GetPerm("ORDER_VIEW")
                },
                new PermissionTreeItem
                {
                    Key = "orders-sale-create",
                    Name = "Đơn mua",
                    Icon = "💵",
                    MainPermission = GetPerm("SALE_CREATE")
                }
            }
        };
        tree.Add(orderGroup);

        // 3. Khuyến mãi (Vouchers)
        var voucherGroup = new PermissionTreeItem
        {
            Key = "vouchers",
            Name = "Khuyến mãi & Voucher",
            Icon = "🎟️",
            Children = new List<PermissionTreeItem>
            {
                new PermissionTreeItem
                {
                    Key = "vouchers-manager",
                    Name = "Quản lý Voucher",
                    Icon = "🏷️",
                    MainPermission = GetPerm("VOUCHER_VIEW"),
                    ActionPermissions = GetPerms("VOUCHER_CREATE", "VOUCHER_EDIT", "VOUCHER_DELETE")
                }
            }
        };
        tree.Add(voucherGroup);

        // 4. Báo cáo thống kê (Reports)
        var reportGroup = new PermissionTreeItem
        {
            Key = "reports",
            Name = "Báo cáo thống kê",
            Icon = "📊",
            Children = new List<PermissionTreeItem>
            {
                new PermissionTreeItem { Key = "rep-index", Name = "Tổng quan báo cáo", Icon = "📊", MainPermission = GetPerm("REPORT_VIEW") },
                new PermissionTreeItem { Key = "rep-transactions", Name = "Thống kê giao dịch", Icon = "💸", MainPermission = GetPerm("REPORT_TRANSACTIONS") },
                new PermissionTreeItem { Key = "rep-closed", Name = "Doanh thu đơn đã đóng", Icon = "🔒", MainPermission = GetPerm("REPORT_CLOSED_ORDERS") },
                new PermissionTreeItem { Key = "rep-open", Name = "Doanh thu đơn chưa đóng", Icon = "🔓", MainPermission = GetPerm("REPORT_OPEN_ORDERS") },
                new PermissionTreeItem { Key = "rep-sales", Name = "Doanh thu mặt hàng bán", Icon = "🛍️", MainPermission = GetPerm("REPORT_PRODUCT_SALES") },
                new PermissionTreeItem { Key = "rep-idcards", Name = "Danh sách nhận CCCD", Icon = "🪪", MainPermission = GetPerm("REPORT_ID_CARDS") },
                new PermissionTreeItem { Key = "rep-staff", Name = "Doanh thu nhân viên", Icon = "👥", MainPermission = GetPerm("REPORT_STAFF_REVENUE") },
                new PermissionTreeItem { Key = "rep-lowstock", Name = "Cảnh báo tồn kho", Icon = "⚠️", MainPermission = GetPerm("REPORT_LOW_STOCK") }
            }
        };
        tree.Add(reportGroup);

        // 5. Cài đặt hệ thống (Settings)
        var settingsGroup = new PermissionTreeItem
        {
            Key = "settings",
            Name = "Cài đặt hệ thống",
            Icon = "⚙️",
            Children = new List<PermissionTreeItem>
            {
                new PermissionTreeItem
                {
                    Key = "set-users",
                    Name = "Quản lý người dùng",
                    Icon = "👥",
                    MainPermission = GetPerm("USER_MANAGEMENT_VIEW")
                },
                new PermissionTreeItem
                {
                    Key = "set-rolegroups",
                    Name = "Nhóm quyền & Mẫu",
                    Icon = "🛡️",
                    MainPermission = GetPerm("ROLE_GROUP_VIEW"),
                    ActionPermissions = GetPerms("ROLE_GROUP_CREATE", "ROLE_GROUP_EDIT", "ROLE_GROUP_DELETE")
                },
                new PermissionTreeItem
                {
                    Key = "set-menumanager",
                    Name = "Quản lý Menu & Quyền",
                    Icon = "📑",
                    MainPermission = GetPerm("USER_MANAGEMENT_VIEW")
                },
                new PermissionTreeItem
                {
                    Key = "set-system",
                    Name = "Cấu hình chung",
                    Icon = "🛠️",
                    MainPermission = GetPerm("SYSTEM_SETTINGS_VIEW")
                }
            }
        };
        tree.Add(settingsGroup);

        // 6. Remaining uncategorized permissions (if any)
        var remainingPerms = allPerms.Where(p => !usedCodes.Contains(p.Code.ToUpperInvariant())).ToList();
        if (remainingPerms.Any())
        {
            var otherGroup = new PermissionTreeItem
            {
                Key = "other",
                Name = "Quyền hạn mở rộng khác",
                Icon = "📁",
                Children = remainingPerms.Select(p => new PermissionTreeItem
                {
                    Key = "other-" + p.Id,
                    Name = p.Name,
                    Icon = "🔑",
                    MainPermission = p
                }).ToList()
            };
            tree.Add(otherGroup);
        }

        return tree;
    }
}
