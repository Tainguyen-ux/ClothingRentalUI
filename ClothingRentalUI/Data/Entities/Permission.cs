using System.Collections.Generic;

namespace ClothingRentalUI.Data.Entities;

public class Permission
{
    public int Id { get; set; }
    public required string Code { get; set; } // Ví dụ: CLOTHES_VIEW, ORDER_CREATE, REPORT_VIEW
    public required string Name { get; set; } // Xem trang phục, Tạo đơn hàng, Xem báo cáo
    public required string Type { get; set; } // "UI" hoặc "Action"
    public string? Description { get; set; } // mo ta

    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}
