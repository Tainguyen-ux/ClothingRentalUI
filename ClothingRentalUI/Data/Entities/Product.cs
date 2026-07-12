using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClothingRentalUI.Data.Entities;

public class Product
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // [Prefix] + yyyyMMdd + 4-digit sequence

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int PriceListId { get; set; }
    public PriceList? PriceList { get; set; }

    [MaxLength(50)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string? Size { get; set; } // Size: S, M, L, XL...

    [MaxLength(100)]
    public string? Material { get; set; }

    [MaxLength(100)]
    public string? Condition { get; set; } // Mới 100%, 90%, v.v.

    [Column(TypeName = "jsonb")]
    public string DynamicAttributes { get; set; } = "[]"; // Lưu mảng thuộc tính: [{key, display, value}]

    public string? Description { get; set; }

    public decimal ImportPrice { get; set; }

    public string? ImageUrl { get; set; }

    public int StockQuantity { get; set; } // Hàng đang có trong cửa hàng
    public int RentedQuantity { get; set; } = 0; // Hàng đang cho thuê
    public int WarningStockLevel { get; set; } = 0; // Mức tồn kho tối thiểu để cảnh báo

    // Tích lũy doanh thu thuê để phục vụ báo cáo thanh lý (Giá nhập vs Tổng tiền cho thuê)
    public decimal TotalRentRevenue { get; set; } = 0;

    public bool IsAvailable { get; set; } = true;
    public bool IsLiquidated { get; set; } = false; // Đã thanh lý (ngừng sử dụng)

    [Column(TypeName = "jsonb")]
    public string SystemLog { get; set; } = "[]";

    public ICollection<StockHistory> StockHistories { get; set; } = new List<StockHistory>();
}
