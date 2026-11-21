# Organization-scoped menu RBAC

Applied to the existing AiEmailer database on 2026-11-15 with user approval.

- `001_rbac.sql`: one-time data/schema migration, role cloning per organization, independent View/Add/Update/Delete flags, initial full Administrator grants, revisions, audit, and guarded management procedures. Do not replay on a migrated database.
- `002_rbac_empty_audit.sql`: repeatable procedure correction for a role with no prior grants. The same correction is included in 001 for new installations.
- Apply with a SQL client configured to stop on errors (sqlcmd `-b`). Take a full backup first. Deploy backend and frontend together; legacy permission writes are deliberately rejected.
- Existing Administrator roles were explicitly authorized to receive all four actions for all registered menus. There is no permanent administrator bypass. Disabled menus remain unavailable to all roles.
- Other legacy grants must be 0/1; migration stops on ambiguous 2/3/4 values instead of assuming cumulative semantics.
- Roles are now owned by an organization. A shared role is duplicated for each organization and user role IDs are remapped. Users must have an organization and role before migration.
- `permission` is retained as a retired legacy column. Application code uses `can_view`, `can_add`, `can_update`, `can_delete` only.
- Global menu catalog changes are not exposed to organization administrators. Registration of new menus is a database/application deployment task.
- Runtime permissions are loaded per request from SQL, not JWT/session/cache. Frontend JSON is in memory only, refreshed every 15 seconds on visible tabs, on focus, and after 403 responses. Permission changes reload the page to clear stale data and dialogs.
- API mappings: reply generation and attachment analysis require Add; summary requires Inbox View. Lookup list endpoints allow the corresponding administration or consuming feature menu. User and attachment records remain organization-scoped.
- Existing sample-only pages receive navigation protection; this change does not implement their missing business APIs.

## Validation

`dotnet build --no-restore` compiles the backend. `python tests/rbac-api.py` checks the running local API at port 5089 using temporary test organizations and in-memory JWTs. It cleans up fixtures in a finally block; never use the script against a remote production deployment.
Frontend suites are `tests/ui-smoke.cjs` and `tests/rbac-ui.cjs` in the frontend project. They simulate API responses and do not alter business data.

## Recovery

Verified pre-change SQL backup: `backups/rbac-20261115-154138/AiEmailer-before-rbac.bak`.
Code backups: `backend.zip`, `frontend.zip` in the same directory. Backups are ignored by git and may contain configuration secrets; keep them local.
Restore database and code together if rollback is needed. A restored validation database `AiEmailer_RbacValidation_20261115` was created before the user requested direct migration of AiEmailer; no migration was applied to that copy.
