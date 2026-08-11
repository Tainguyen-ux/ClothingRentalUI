namespace ClothingRentalUI.Models.Report;

public class ProductSaleReportDto
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int TotalSoldQuantity { get; set; }
    public decimal TotalRevenue { get; set; }
    public int CurrentStockQuantity { get; set; }
    public bool IsAvailable { get; set; }
    public int WarningStockLevel { get; set; }

    public string StockStatus
    {
        get
        {
            if (CurrentStockQuantity <= 0) return "Hết hàng";
            if (WarningStockLevel > 0 && CurrentStockQuantity <= WarningStockLevel) return "Sắp hết";
            return "Còn hàng";
        }
    }
}
