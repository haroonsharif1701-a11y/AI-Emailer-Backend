SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.skey_mailbox_connection') IS NULL
CREATE TABLE dbo.skey_mailbox_connection (
 id_user INT NOT NULL, id_org INT NOT NULL, provider VARCHAR(20) NOT NULL DEFAULT 'Gmail',
 account_id NVARCHAR(255) NOT NULL, email NVARCHAR(320) NOT NULL,
 refresh_token NVARCHAR(MAX) NOT NULL, scope NVARCHAR(2000) NOT NULL,
 version UNIQUEIDENTIFIER NOT NULL, history_id VARCHAR(100) NULL,
 synced_at DATETIME2 NULL, reconnect_required BIT NOT NULL DEFAULT 0,
 CONSTRAINT PK_mailbox_connection PRIMARY KEY(id_user,id_org),
 CONSTRAINT UQ_mailbox_account UNIQUE(id_org,provider,account_id));
IF OBJECT_ID('dbo.skey_mailbox_authorization') IS NULL
CREATE TABLE dbo.skey_mailbox_authorization (
 state_hash BINARY(32) NOT NULL PRIMARY KEY, browser_hash BINARY(32) NOT NULL,
 id_user INT NOT NULL, id_org INT NOT NULL, id_session UNIQUEIDENTIFIER NOT NULL,
 verifier NVARCHAR(MAX) NOT NULL, expires_at DATETIME2 NOT NULL);
COMMIT TRANSACTION;
