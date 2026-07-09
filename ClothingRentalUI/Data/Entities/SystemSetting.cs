using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClothingRentalUI.Data.Entities;

public class SystemSetting
{
    [Key]
    public string Key { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public string ValueJson { get; set; } = null!;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
