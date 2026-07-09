namespace ClothingRentalUI.Data.Entities;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string CodePrefix { get; set; } // Ví dụ: VC, VS, AD...
}
