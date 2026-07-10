using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class Order
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // HD + yyMMdd + 4-digit sequence

    // Liên kết khách hàng
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Thời gian thuê
    public DateTime RentDate { get; set; } // Ngày bắt đầu thuê
    public DateTime DueDate { get; set; } // Hạn trả đồ
    public DateTime? ActualReturnDate { get; set; } // Ngày thực tế trả hết

    // Tài chính
    public decimal TotalPrice { get; set; } // Tổng tiền thuê gốc
    public decimal TotalDeposit { get; set; } // Tổng tiền cọc
    public decimal TotalPenalty { get; set; } // Tổng tiền phạt phát sinh
    public decimal FinalAmount { get; set; } // Số tiền thanh toán cuối cùng

    // Trạng thái
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Draft"; // Draft, Rented, PartiallyReturned, Closed, Overdue

    [Required]
    [MaxLength(20)]
    public string DepositStatus { get; set; } = "None"; // None, Holding, Refunded, Charged

    // Đính kèm
    public string? AttachmentUrl { get; set; }
    public string? Notes { get; set; }
    public bool IsIdCardReceived { get; set; } = false; // Đã nhận CCCD

    // Ghi nhận nhân viên thao tác
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public int? ClosedByUserId { get; set; }
    public User? ClosedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
