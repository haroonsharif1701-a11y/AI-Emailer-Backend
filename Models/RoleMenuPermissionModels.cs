namespace AiEmailer.Api.Models
{
    public record RoleMenuPermissionItem(int IdMenu, int Permission);

    public record RoleMenuPermissionSaveRequest(int IdRole, List<RoleMenuPermissionItem> Permissions);
}
