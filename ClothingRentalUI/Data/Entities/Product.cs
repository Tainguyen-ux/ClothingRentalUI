namespace ClothingRentalUI.Data.Entities;

public class Product
{
    public int Id { get; set; }
    public required string Code { get; set; } // [Prefix] + yyyyMMdd + 4-digit sequence
    public required string Name { get; set; }
    public int StockQuantity { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; } // Size: S, M, L, XL...
    public string? Description { get; set; }
    public decimal ImportPrice { get; set; }
    
    public int PriceListId { get; set; }
    public PriceList? PriceList { get; set; }
    
    public string? ImageUrl { get; set; }
    
    public bool IsAvailable { get; set; } = true;
    public bool IsLiquidated { get; set; } = false; // Đã thanh lý (ngừng sử dụng)
    
    // Tích lũy doanh thu thuê để phục vụ báo cáo thanh lý (Giá nhập vs Tổng tiền cho thuê)
    public decimal TotalRentRevenue { get; set; } = 0;
}
