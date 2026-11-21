-- Apply to a backed-up AiEmailer database during a coordinated backend/frontend deployment.
-- Existing grants in the supplied export are View-only. Abort on ambiguous legacy values.
-- Administrator bootstrap is explicitly authorized by Haroon, 2026-11-15.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.skey_role', 'id_org') IS NOT NULL
    THROW 51000, 'RBAC migration is already installed; do not replay the data migration.', 1;
IF EXISTS (SELECT 1 FROM dbo.skey_role_menu_permission WHERE permission NOT IN (0,1))
    THROW 51000, 'Review legacy Add/Update/Delete grants before migration.', 1;
IF EXISTS (SELECT 1 FROM dbo.skey_user WHERE id_org IS NULL OR id_role IS NULL)
    THROW 51000, 'Resolve users without an organization or role before migration.', 1;
GO
ALTER TABLE dbo.skey_role ADD id_org INT NULL, permission_version BIGINT NOT NULL DEFAULT(1),
    is_org_admin BIT NOT NULL DEFAULT(0);
ALTER TABLE dbo.skey_role_menu_permission ADD id_org INT NULL,
    can_view BIT NOT NULL DEFAULT(0), can_add BIT NOT NULL DEFAULT(0),
    can_update BIT NOT NULL DEFAULT(0), can_delete BIT NOT NULL DEFAULT(0);
GO
-- Preserve each original role ID for the first organization; clone for other organizations.
SELECT * INTO #LegacyRoles FROM dbo.skey_role;
SELECT * INTO #LegacyPermissions FROM dbo.skey_role_menu_permission;
CREATE TABLE #RoleMap(old_id INT, id_org INT, new_id INT, PRIMARY KEY(old_id,id_org));
DECLARE @org INT, @old INT, @new INT, @name VARCHAR(200), @creator INT;
DECLARE role_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT r.id_role, o.id_organization, r.role_name, r.createdby
    FROM #LegacyRoles r CROSS JOIN dbo.skey_orginazations o ORDER BY r.id_role, o.id_organization;
OPEN role_cursor;
FETCH NEXT FROM role_cursor INTO @old,@org,@name,@creator;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM #RoleMap WHERE old_id=@old)
    BEGIN
        UPDATE dbo.skey_role SET id_org=@org, is_org_admin=IIF(@name='Administrator',1,0) WHERE id_role=@old;
        SET @new=@old;
    END
    ELSE
    BEGIN
        INSERT dbo.skey_role(role_name,id_org,is_org_admin,createdby,createddt)
        VALUES(@name,@org,IIF(@name='Administrator',1,0),@creator,GETDATE());
        SET @new=SCOPE_IDENTITY();
    END;
    INSERT #RoleMap VALUES(@old,@org,@new);
    FETCH NEXT FROM role_cursor INTO @old,@org,@name,@creator;
END;
CLOSE role_cursor;
DEALLOCATE role_cursor;
UPDATE u SET id_role=m.new_id FROM dbo.skey_user u JOIN #RoleMap m ON m.old_id=u.id_role AND m.id_org=u.id_org;
DELETE FROM dbo.skey_role_menu_permission;
INSERT dbo.skey_role_menu_permission(id_org,id_role,id_menu,permission,can_view,createdby,createddt)
SELECT m.id_org,m.new_id,p.id_menu,0,CONVERT(BIT,p.permission),p.createdby,p.createddt
FROM #LegacyPermissions p JOIN #RoleMap m ON m.old_id=p.id_role;
-- The legacy column is retained for rollback/reference only; legacy procedures are disabled below.
UPDATE p SET can_view=1,can_add=1,can_update=1,can_delete=1
FROM dbo.skey_role_menu_permission p JOIN dbo.skey_role r ON r.id_role=p.id_role WHERE r.is_org_admin=1;
INSERT dbo.skey_role_menu_permission(id_org,id_role,id_menu,permission,can_view,can_add,can_update,can_delete,createdby,createddt)
SELECT r.id_org,r.id_role,m.id_menu,0,1,1,1,1,COALESCE(r.createdby,0),GETDATE()
FROM dbo.skey_role r CROSS JOIN dbo.skey_menu m
WHERE r.is_org_admin=1 AND NOT EXISTS(SELECT 1 FROM dbo.skey_role_menu_permission p WHERE p.id_role=r.id_role AND p.id_menu=m.id_menu);
ALTER TABLE dbo.skey_role ALTER COLUMN id_org INT NOT NULL;
ALTER TABLE dbo.skey_role_menu_permission ALTER COLUMN id_org INT NOT NULL;
ALTER TABLE dbo.skey_role ADD CONSTRAINT FK_role_org FOREIGN KEY(id_org) REFERENCES dbo.skey_orginazations(id_organization);
ALTER TABLE dbo.skey_role ADD CONSTRAINT UQ_role_org UNIQUE(id_role,id_org);
ALTER TABLE dbo.skey_role_menu_permission ADD CONSTRAINT FK_permission_role_org FOREIGN KEY(id_role,id_org) REFERENCES dbo.skey_role(id_role,id_org);
ALTER TABLE dbo.skey_role_menu_permission ADD CONSTRAINT CK_permission_view CHECK(can_view=1 OR (can_add=0 AND can_update=0 AND can_delete=0));
ALTER TABLE dbo.skey_user ADD CONSTRAINT FK_user_role_org FOREIGN KEY(id_role,id_org) REFERENCES dbo.skey_role(id_role,id_org);
CREATE TABLE dbo.skey_permission_audit(
    id_audit BIGINT IDENTITY PRIMARY KEY, id_org INT NOT NULL, id_role INT NOT NULL,
    modifiedby INT NOT NULL, modifieddt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    old_permissions NVARCHAR(MAX) NOT NULL, new_permissions NVARCHAR(MAX) NOT NULL);
DROP TABLE #RoleMap, #LegacyRoles, #LegacyPermissions;
COMMIT TRANSACTION;
GO
CREATE TYPE dbo.RoleMenuPermissionV2Type AS TABLE(
    id_menu INT PRIMARY KEY, can_view BIT NOT NULL, can_add BIT NOT NULL,
    can_update BIT NOT NULL, can_delete BIT NOT NULL);
GO
CREATE OR ALTER PROCEDURE dbo.rbac_current_access @id_user INT,@id_org INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @role INT;
    SELECT @role=id_role FROM dbo.skey_user WHERE id_user=@id_user AND id_org=@id_org AND is_active=1;
    SELECT id_role,permission_version FROM dbo.skey_role WHERE id_role=@role AND id_org=@id_org;
    SELECT m.menu_key,
        CONVERT(BIT,CASE WHEN m.is_enable=1 AND ISNULL(parent.is_enable,1)=1 THEN ISNULL(p.can_view,0) ELSE 0 END) can_view,
        CONVERT(BIT,CASE WHEN m.is_enable=1 AND ISNULL(parent.is_enable,1)=1 THEN ISNULL(p.can_add,0) ELSE 0 END) can_add,
        CONVERT(BIT,CASE WHEN m.is_enable=1 AND ISNULL(parent.is_enable,1)=1 THEN ISNULL(p.can_update,0) ELSE 0 END) can_update,
        CONVERT(BIT,CASE WHEN m.is_enable=1 AND ISNULL(parent.is_enable,1)=1 THEN ISNULL(p.can_delete,0) ELSE 0 END) can_delete
    FROM dbo.skey_menu m LEFT JOIN dbo.skey_menu parent ON parent.id_menu=m.id_parent_menu
    LEFT JOIN dbo.skey_role_menu_permission p ON p.id_menu=m.id_menu AND p.id_role=@role AND p.id_org=@id_org;
END;
GO
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
CREATE OR ALTER PROCEDURE dbo.role_menu_permission_savebulk
    @id_role INT,@modifiedby INT,@permissions dbo.RoleMenuPermissionType READONLY
AS THROW 51000,'Legacy permission writes are disabled. Use the organization-scoped RBAC API.',1;
GO

CREATE OR ALTER PROCEDURE dbo.rbac_role_addupd @id_role INT,@role_name VARCHAR(200),@createdby INT,@modifiedby INT,@id_org INT
AS
BEGIN
    SET XACT_ABORT ON;
    IF @id_role=0
        INSERT dbo.skey_role(role_name,id_org,createdby,createddt) VALUES(@role_name,@id_org,@createdby,GETDATE());
    ELSE BEGIN
        UPDATE dbo.skey_role SET role_name=@role_name,modifiedby=@modifiedby,modifieddt=GETDATE() WHERE id_role=@id_role AND id_org=@id_org;
        IF @@ROWCOUNT=0 THROW 51003,'Role not found in your organization.',1;
    END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.rbac_role_delete @id_role INT,@id_org INT
AS
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    IF EXISTS(SELECT 1 FROM dbo.skey_user WHERE id_role=@id_role AND id_org=@id_org)
        THROW 51000,'Reassign users before deleting this role.',1;
    DELETE dbo.skey_role_menu_permission WHERE id_role=@id_role AND id_org=@id_org;
    DELETE dbo.skey_role WHERE id_role=@id_role AND id_org=@id_org AND is_org_admin=0;
    IF @@ROWCOUNT=0 THROW 51000,'Role not found or is a protected administrator role.',1;
    COMMIT TRANSACTION;
END;
GO
CREATE OR ALTER PROCEDURE dbo.rbac_check_user_change
    @actor INT,@org INT,@target INT,@role INT,@active BIT,@dept INT=NULL,@deleting BIT=0
AS
BEGIN
    -- Caller holds an organization lock throughout this check and the ensuing write.
    DECLARE @actorRole INT;
    SELECT @actorRole=id_role FROM dbo.skey_user WHERE id_user=@actor AND id_org=@org AND is_active=1;
    IF @actorRole IS NULL THROW 51001,'Active organization membership is required.',1;
    IF @target<>0 AND NOT EXISTS(SELECT 1 FROM dbo.skey_user WHERE id_user=@target AND id_org=@org)
        THROW 51003,'User not found in your organization.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.skey_role WHERE id_role=@role AND id_org=@org)
        THROW 51001,'Role does not belong to your organization.',1;
    IF @dept IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.skey_department WHERE id_dept=@dept AND id_org=@org)
        THROW 51001,'Department does not belong to your organization.',1;
    IF @target=@actor AND (@deleting=1 OR @role<>@actorRole OR @active=0)
        THROW 51001,'You cannot delete, deactivate, or change your own role.',1;
    IF EXISTS(SELECT 1 FROM dbo.skey_role WHERE id_role=@role AND is_org_admin=1)
       AND NOT EXISTS(SELECT 1 FROM dbo.skey_role WHERE id_role=@actorRole AND is_org_admin=1)
        THROW 51001,'Only organization administrators may assign an administrator role.',1;
    IF EXISTS(SELECT 1 FROM dbo.skey_user u JOIN dbo.skey_role r ON r.id_role=u.id_role WHERE u.id_user=@target AND r.is_org_admin=1)
       AND NOT EXISTS(SELECT 1 FROM dbo.skey_role WHERE id_role=@actorRole AND is_org_admin=1)
        THROW 51001,'Only organization administrators may modify administrator accounts.',1;
    IF EXISTS(SELECT 1 FROM dbo.skey_role_menu_permission target
        LEFT JOIN dbo.skey_role_menu_permission own ON own.id_role=@actorRole AND own.id_org=@org AND own.id_menu=target.id_menu
        WHERE target.id_role=@role AND target.id_org=@org AND (target.can_view>ISNULL(own.can_view,0) OR target.can_add>ISNULL(own.can_add,0)
            OR target.can_update>ISNULL(own.can_update,0) OR target.can_delete>ISNULL(own.can_delete,0)))
        THROW 51001,'You cannot assign a role with permissions you do not have.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.skey_user u JOIN dbo.skey_role r ON r.id_role=CASE WHEN u.id_user=@target THEN @role ELSE u.id_role END
        JOIN dbo.skey_role_menu_permission p ON p.id_role=r.id_role AND p.id_org=@org JOIN dbo.skey_menu m ON m.id_menu=p.id_menu
        WHERE u.id_org=@org AND r.is_org_admin=1 AND m.menu_key='settings.permissions' AND m.is_enable=1 AND p.can_view=1 AND p.can_update=1
          AND CASE WHEN u.id_user=@target THEN @active ELSE u.is_active END=1 AND (@deleting=0 OR u.id_user<>@target))
        THROW 51000,'Keep at least one active administrator with permission-management access.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.rbac_user_addupd(
@id_user INT,
@username VARCHAR(200),
@fullname VARCHAR(300),
@password VARBINARY(200) = NULL,
@uuqid UNIQUEIDENTIFIER,
@id_role INT,
@id_org INT,
@id_dept INT,
@isactive BIT,
@os VARCHAR(150) = NULL,
@regid VARCHAR(200) = NULL,
@device_token VARCHAR(250) = NULL,
@createdby INT,
@modifiedby INT
)
AS
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @lock INT;
    SELECT @lock=id_organization FROM dbo.skey_orginazations WITH(UPDLOCK,HOLDLOCK) WHERE id_organization=@id_org;
    EXEC dbo.rbac_check_user_change @modifiedby,@id_org,@id_user,@id_role,@isactive,@id_dept,0;
	IF @id_user != 0
	BEGIN
		UPDATE skey_user
		SET
			username = @username,
			fullname = @fullname,
			password = COALESCE(@password, password), -- keep existing hash if not supplied
			id_role = @id_role,
			id_org = @id_org,
			id_dept = @id_dept,
			os = @os,
			regid = @regid,
			is_active = @isactive,
			device_token = @device_token,
			modifiedby = @modifiedby,
			modifieddt = GETDATE()
		WHERE id_user = @id_user
	END
	ELSE
	BEGIN
		INSERT INTO skey_user(
			username, fullname, password, uuqid, id_role, id_org, id_dept,
			os, regid, device_token, is_active, createdby, createddt
		)
		VALUES(
			@username, @fullname, @password, @uuqid, @id_role, @id_org, @id_dept,
			@os, @regid, @device_token,@isactive, @createdby, GETDATE()
		)
	END
    COMMIT TRANSACTION;
END



GO
CREATE OR ALTER PROCEDURE dbo.rbac_user_delete @id_user INT,@actor INT,@id_org INT
AS
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @lock INT,@role INT;
    SELECT @lock=id_organization FROM dbo.skey_orginazations WITH(UPDLOCK,HOLDLOCK) WHERE id_organization=@id_org;
    SELECT @role=id_role FROM dbo.skey_user WHERE id_user=@id_user AND id_org=@id_org;
    EXEC dbo.rbac_check_user_change @actor,@id_org,@id_user,@role,0,NULL,1;
    DELETE dbo.skey_user WHERE id_user=@id_user AND id_org=@id_org;
    COMMIT TRANSACTION;
END;
GO
CREATE OR ALTER PROCEDURE [dbo].[org_card_counts] 
(
@uuqid VARCHAR(200)
)
AS 
BEGIN

DECLARE @id_org INT;
DECLARE @id_dept INT;
DECLARE @id_user INT;

SELECT @id_org = id_org, @id_dept = id_dept, @id_user = id_user FROM skey_user where uuqid = @uuqid

SELECT

  (
            SELECT COUNT(*)
            FROM skey_user su
            WHERE su.id_org = @id_org
  ) AS total_user,

 (
    SELECT COUNT(sd.id_dept)
    FROM skey_department sd
    WHERE sd.id_org = @id_org
 )  AS total_dept,

 (
    SELECT COUNT(et.id_team)
    FROM ekey_team et
    WHERE (et.id_org = @id_org)
 )  AS total_teams,

 (
    SELECT COUNT(sa.id_user)
    FROM skey_user sa
    WHERE (sa.id_org = @id_org) AND (EXISTS (SELECT 1 FROM dbo.skey_role r WHERE r.id_role=sa.id_role AND r.id_org=@id_org AND r.is_org_admin=1))
 )  AS total_admins
END;

GO
