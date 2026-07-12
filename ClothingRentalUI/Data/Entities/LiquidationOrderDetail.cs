using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class LiquidationOrderDetail
{
    public int Id { get; set; }

    public int LiquidationOrderId { get; set; }
    public LiquidationOrder? LiquidationOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}
