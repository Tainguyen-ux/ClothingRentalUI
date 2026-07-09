using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClothingRentalUI.Data.Entities;

public class PriceList
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal PricePerDay { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Deposit { get; set; }

    public string? Description { get; set; }

    [Column(TypeName = "jsonb")]
    public string SystemLog { get; set; } = "[]";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
