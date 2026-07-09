namespace ClothingRentalUI.Models.Clothes;

public class ClothesDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public decimal PricePerDay { get; set; }
    public decimal Deposit { get; set; } // Thêm tiền cọc
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public bool IsAvailable { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    
    // Nghiệp vụ bổ sung
    public decimal ImportPrice { get; set; }
    public decimal TotalRentRevenue { get; set; }
    public bool IsLiquidated { get; set; }
}
