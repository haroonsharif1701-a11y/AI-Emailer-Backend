SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.skey_login_session') IS NULL
CREATE TABLE dbo.skey_login_session (
 id_session UNIQUEIDENTIFIER PRIMARY KEY, id_user INT NOT NULL, id_org INT NOT NULL,
 expires_at DATETIME2 NOT NULL, revoked_at DATETIME2 NULL,
 created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.skey_oauth_handoff') IS NULL
CREATE TABLE dbo.skey_oauth_handoff (
 code_hash BINARY(32) PRIMARY KEY, challenge VARCHAR(43) NOT NULL,
 id_user INT NOT NULL, id_org INT NOT NULL,
 expires_at DATETIME2 NOT NULL);
COMMIT TRANSACTION;
