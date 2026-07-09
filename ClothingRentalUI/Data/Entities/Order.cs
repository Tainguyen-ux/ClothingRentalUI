using System;
using System.Collections.Generic;

namespace ClothingRentalUI.Data.Entities;

public class Order
{
    public int Id { get; set; }
    public required string Code { get; set; } // yyyyMMdd + 4-digit sequence
    public required string CustomerName { get; set; }
    public required string PhoneNumber { get; set; }
    public bool HasIdCard { get; set; }
    public string? IdCardNumber { get; set; }
    
    // Đính kèm hình ảnh đơn hàng (chụp ảnh sản phẩm khi giao hoặc upload)
    public string? AttachmentUrl { get; set; }

    public decimal TotalPrice { get; set; } // Tổng tiền thuê gốc của các mặt hàng
    public decimal TotalDeposit { get; set; } // Tổng tiền cọc giữ của các mặt hàng
    public decimal TotalPenalty { get; set; } // Tổng tiền phát sinh trễ hạn / phạt
    public decimal FinalAmount { get; set; } // Số tiền thanh toán cuối cùng (Tổng thuê + phạt)
    
    public string Status { get; set; } = "Draft"; // "Draft", "Rented", "Closed"
    
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? ClosedDate { get; set; }
    
    // Ghi nhận nhân viên thao tác
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    
    public int? PenaltyByUserId { get; set; }
    public User? PenaltyByUser { get; set; }
    
    public int? ClosedByUserId { get; set; }
    public User? ClosedByUser { get; set; }

    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
