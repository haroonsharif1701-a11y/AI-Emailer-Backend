namespace AiEmailer.Api.Models;

// What the list view needs — deliberately excludes the password hash.
public record UserListItem(
    int IdUser,
    string Username,
    string FullName,
    int IdRole,
    string RoleName,
    bool IsActive,
    string OrgName,
    int? IdDept,
    string? DeptName
);

// What the edit modal needs to pre-fill the form, PLUS the fields the form
// doesn't expose (Os/RegId/DeviceToken) — these are carried through here
// solely so AddUpdateAsync can round-trip them unchanged on update. The
// existing user_addupd proc's UPDATE branch overwrites these columns
// unconditionally (no COALESCE), so passing NULL for "fields the form
// doesn't touch" would silently wipe them on every save.
public record UserDetail(
    int IdUser,
    string Username,
    string FullName,
    int IdRole,
    string RoleName,
    string OrgName,
    int IdOrg,
    int? IdDept,
    string? DeptName,
    string? Os,
    string? RegId,
    string? DeviceToken,
    bool IsActive
);

// IdUser = 0 means create. Password is required by the controller when
// IdUser == 0, and optional (null/blank = "keep existing") when editing.
public record UserSaveRequest(
    int IdUser,
    string Username,
    string FullName,
    string? Password,
    int IdRole,
    int? IdDept,
    bool? IsActive
);

public record RoleListItem(int IdRole, string RoleName);
