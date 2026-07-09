namespace ClothingRentalUI.Data.Entities;

public class PriceList
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal PricePerDay { get; set; }
    public decimal Deposit { get; set; }
}
