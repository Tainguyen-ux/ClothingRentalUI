namespace ClothingRentalUI.Data.Entities;

public class RoleGroupPermission
{
    public int RoleGroupId { get; set; }
    public RoleGroup? RoleGroup { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
}
