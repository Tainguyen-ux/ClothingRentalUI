using System;
using System.Collections.Generic;

namespace ClothingRentalUI.Data.Entities;

public class RoleGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RoleGroupPermission> RoleGroupPermissions { get; set; } = new List<RoleGroupPermission>();
}
