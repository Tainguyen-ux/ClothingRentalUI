using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClothingRentalUI.Data.Entities;

public class Voucher
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // Mã voucher: VN250710, SALE50...

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty; // Tên voucher: Giảm 50k cho đơn trên 200k

    [Required]
    [MaxLength(20)]
    public string DiscountType { get; set; } = "FIXED"; // FIXED (giảm cố định), PERCENT (giảm %)

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountValue { get; set; } // Giá trị giảm: 50000 hoặc 10 (%)

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MaxDiscountAmount { get; set; } // Giới hạn giảm tối đa (chỉ dùng cho PERCENT)

    [Column(TypeName = "decimal(18,2)")]
    public decimal MinOrderAmount { get; set; } = 0; // Đơn tối thiểu để áp dụng

    public int? MaxUsageCount { get; set; } // Tổng số lần sử dụng tối đa (null = không giới hạn)
    public int UsedCount { get; set; } = 0; // Số lần đã sử dụng

    public DateTime StartDate { get; set; } = DateTime.UtcNow; // Ngày bắt đầu hiệu lực
    public DateTime EndDate { get; set; } = DateTime.UtcNow.AddMonths(1); // Ngày hết hạn

    public bool IsActive { get; set; } = true;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
