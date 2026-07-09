using System;
using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class StockHistory
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Required]
    [MaxLength(50)]
    public string ActionType { get; set; } = string.Empty; // IMPORT, LIQUIDATE, LOSS_DAMAGE, MAINTENANCE

    public int QuantityChange { get; set; } // + or -
    
    public int RemainingTotal { get; set; } // Tổng vật lý còn lại

    [MaxLength(100)]
    public string? ReferenceCode { get; set; } // Hóa đơn nhập, mã đơn thuê khách đền...

    public string? Note { get; set; }

    [Required]
    [MaxLength(100)]
    public string PerformedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
