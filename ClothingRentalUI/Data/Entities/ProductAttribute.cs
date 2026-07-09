using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class ProductAttribute
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Key { get; set; } = string.Empty; // VD: size, material

    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty; // VD: Kích thước, Chất liệu

    public string? Description { get; set; } // Ghi chú thêm nếu cần

    public bool IsActive { get; set; } = true;
}
