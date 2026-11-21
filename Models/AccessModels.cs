using System.ComponentModel.DataAnnotations;

namespace AiEmailer.Api.Models;

public record MenuPermissions(bool View, bool Add, bool Update, bool Delete)
{
    public static readonly MenuPermissions None = new(false, false, false, false);

    public bool Allows(string action) => View && action switch
    {
        "view" => true,
        "add" => Add,
        "update" => Update,
        "delete" => Delete,
        _ => false
    };
}

public record AccessSnapshot(int OrganizationId, int RoleId, long PermissionVersion,
    Dictionary<string, MenuPermissions> Menus)
{
    public bool Allows(string menu, string action) =>
        Menus.TryGetValue(menu, out var permissions) && permissions.Allows(action);
}

public record ProfileUpdateRequest(
    [Required, StringLength(200)] string Username,
    [Required, StringLength(300)] string FullName);

