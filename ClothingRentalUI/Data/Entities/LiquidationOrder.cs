using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class LiquidationOrder
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // TL + yyMMdd + 4-digit sequence

    public DateTime LiquidationDate { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Completed"; // Completed, Cancelled

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LiquidationOrderDetail> LiquidationOrderDetails { get; set; } = new List<LiquidationOrderDetail>();
}
