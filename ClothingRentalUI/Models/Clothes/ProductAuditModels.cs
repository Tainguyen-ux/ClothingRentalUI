using System;
using System.Collections.Generic;

namespace ClothingRentalUI.Models.Clothes;

public class ProductFieldChange
{
    public string Field { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

public class ProductAuditLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Action { get; set; } = "UPDATE"; // CREATE, UPDATE, TOGGLE_STATUS, UPDATE_IMAGE, UPDATE_STOCK
    public string Description { get; set; } = string.Empty;
    public List<ProductFieldChange> Changes { get; set; } = new();
}
