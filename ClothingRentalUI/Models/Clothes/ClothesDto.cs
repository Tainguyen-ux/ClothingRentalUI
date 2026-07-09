namespace ClothingRentalUI.Models.Clothes;

public class ClothesDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrls { get; set; } = "[]";
    public string ImageUrl 
    { 
        get 
        {
            try 
            {
                if(string.IsNullOrEmpty(ImageUrls) || ImageUrls == "[]") return "";
                var list = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(ImageUrls);
                return list != null && list.Count > 0 ? list[0] : "";
            } 
            catch { return ""; }
        }
        set {}
    }
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
