using System.Collections.Generic;

namespace ClothingRentalUI.Data.Entities;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Staff"; // "Admin" or "Staff"
    public bool IsLocked { get; set; } = false;

    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}
