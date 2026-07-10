using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Data.Entities;

public class Customer
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(15)]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? IdentityCard { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Active"; // Active, Blacklisted

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
