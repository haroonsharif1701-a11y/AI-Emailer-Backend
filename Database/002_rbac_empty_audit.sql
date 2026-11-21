-- Repair empty permission history handling; safe to reapply.
CREATE OR ALTER PROCEDURE dbo.rbac_permission_save
    @id_user INT,@id_org INT,@id_role INT,@version BIGINT,@permissions dbo.RoleMenuPermissionV2Type READONLY
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    -- Serialize administrative changes within an organization, including last-admin checks.
    DECLARE @lock INT;
    SELECT @lock=id_organization FROM dbo.skey_orginazations WITH(UPDLOCK,HOLDLOCK) WHERE id_organization=@id_org;
    IF NOT EXISTS(SELECT 1 FROM dbo.skey_user u JOIN dbo.skey_role_menu_permission p ON p.id_role=u.id_role AND p.id_org=u.id_org
        JOIN dbo.skey_menu m ON m.id_menu=p.id_menu WHERE u.id_user=@id_user AND u.id_org=@id_org AND u.is_active=1
        AND m.menu_key='settings.permissions' AND m.is_enable=1 AND p.can_view=1 AND p.can_update=1)
        THROW 51001,'Permission management access is required.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.skey_role WHERE id_role=@id_role AND id_org=@id_org)
        THROW 51003,'Role not found in your organization.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.skey_role WITH(UPDLOCK,HOLDLOCK) WHERE id_role=@id_role AND permission_version=@version)
        THROW 51002,'Permissions changed since you opened this role. Reload before saving.',1;
    IF EXISTS(SELECT 1 FROM @permissions WHERE can_view=0 AND (can_add=1 OR can_update=1 OR can_delete=1))
        THROW 51000,'View is required for Add, Update, or Delete.',1;
    IF EXISTS(SELECT 1 FROM @permissions p LEFT JOIN dbo.skey_menu m ON p.id_menu=m.id_menu WHERE m.id_menu IS NULL)
        THROW 51000,'Unknown menu.',1;
    -- Granting beyond the acting user's own permissions would be privilege escalation.
    IF EXISTS(SELECT 1 FROM @permissions incoming JOIN dbo.skey_user u ON u.id_user=@id_user
        LEFT JOIN dbo.skey_role_menu_permission own ON own.id_role=u.id_role AND own.id_org=@id_org AND own.id_menu=incoming.id_menu
        WHERE incoming.can_view>ISNULL(own.can_view,0) OR incoming.can_add>ISNULL(own.can_add,0)
           OR incoming.can_update>ISNULL(own.can_update,0) OR incoming.can_delete>ISNULL(own.can_delete,0))
        THROW 51001,'You cannot grant permissions you do not have.',1;
    DECLARE @before NVARCHAR(MAX)=(SELECT id_menu,can_view,can_add,can_update,can_delete FROM dbo.skey_role_menu_permission WHERE id_role=@id_role AND id_org=@id_org FOR JSON PATH);
    DELETE dbo.skey_role_menu_permission WHERE id_role=@id_role AND id_org=@id_org;
    INSERT dbo.skey_role_menu_permission(id_org,id_role,id_menu,permission,can_view,can_add,can_update,can_delete,createdby,createddt,modifiedby,modifieddt)
    SELECT @id_org,@id_role,id_menu,0,can_view,can_add,can_update,can_delete,@id_user,GETDATE(),@id_user,GETDATE() FROM @permissions;
    IF NOT EXISTS(SELECT 1 FROM dbo.skey_user u JOIN dbo.skey_role r ON r.id_role=u.id_role
        JOIN dbo.skey_role_menu_permission p ON p.id_role=u.id_role AND p.id_org=u.id_org JOIN dbo.skey_menu m ON m.id_menu=p.id_menu
        WHERE u.id_org=@id_org AND u.is_active=1 AND r.is_org_admin=1 AND m.menu_key='settings.permissions' AND m.is_enable=1 AND p.can_view=1 AND p.can_update=1)
        THROW 51000,'Keep at least one active organization administrator with permission-management access.',1;
    UPDATE dbo.skey_role SET permission_version=permission_version+1 WHERE id_role=@id_role AND id_org=@id_org;
    INSERT dbo.skey_permission_audit(id_org,id_role,modifiedby,old_permissions,new_permissions)
    VALUES(@id_org,@id_role,@id_user,COALESCE(@before,N'[]'),COALESCE((SELECT * FROM @permissions FOR JSON PATH),N'[]'));
    COMMIT TRANSACTION;
END;
GO
