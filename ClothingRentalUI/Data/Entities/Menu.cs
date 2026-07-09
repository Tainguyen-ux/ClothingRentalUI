using System.Collections.Generic;

namespace ClothingRentalUI.Data.Entities;

public class Menu
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? Icon { get; set; }
    
    public int? ParentId { get; set; }
    public Menu? Parent { get; set; }
    
    public int DisplayOrder { get; set; } = 0;
    
    public int? RequiredPermissionId { get; set; }
    public Permission? RequiredPermission { get; set; }

    public ICollection<Menu> SubMenus { get; set; } = new List<Menu>();
}
