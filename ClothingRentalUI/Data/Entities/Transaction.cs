using System;
using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class Transaction
{
    [Key]
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    [Required]
    [MaxLength(30)]
    public string Type { get; set; } = string.Empty; // DEPOSIT_RECEIVED, DEPOSIT_REFUNDED, RENTAL_PAYMENT, PENALTY_PAYMENT

    [Required]
    [MaxLength(20)]
    public string PaymentMethod { get; set; } = "CASH"; // CASH, TRANSFER, CARD

    public decimal Amount { get; set; }

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(100)]
    public string PerformedBy { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ReferenceCode { get; set; }

    [MaxLength(250)]
    public string? Notes { get; set; }
}
