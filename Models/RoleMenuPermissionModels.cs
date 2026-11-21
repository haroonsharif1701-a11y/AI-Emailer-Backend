namespace AiEmailer.Api.Models;

public record RoleMenuPermissionItem(int IdMenu, bool View, bool Add, bool Update, bool Delete);
public record RolePermissionSet(long PermissionVersion, List<RoleMenuPermissionItem> Permissions);
public record RoleMenuPermissionSaveRequest(int IdRole, long PermissionVersion, List<RoleMenuPermissionItem> Permissions);
