using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class SaleOrder
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // MD + yyMMdd + 4-digit sequence

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime SaleDate { get; set; }

    public decimal TotalPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Draft"; // Draft, Closed, Cancelled

    public string? Notes { get; set; }
    public string? AttachmentUrl { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SaleOrderDetail> SaleOrderDetails { get; set; } = new List<SaleOrderDetail>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
