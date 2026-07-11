using System;

namespace ClothingRentalUI.Data.Entities;

public class SaleOrderDetail
{
    public int Id { get; set; }

    public int SaleOrderId { get; set; }
    public SaleOrder? SaleOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
}
