# Workflow role assignment

Administrators can assign the three workflow roles: **Maker**, **Checker**, and **Approver**. The role editor does not assign `Admin` or `Viewer`; those roles remain outside this workflow assignment surface.

Permissions are additive: a user with multiple workflow roles receives the union of each role's permissions. Assignments are canonicalized and persisted without duplicate rows. Saving an unchanged set is idempotent.

Role changes are available only to authenticated users with the `Admin` role. The service enforces this boundary in addition to hiding the administration navigation for other users.

Role replacement uses the user's `RoleVersion` for optimistic concurrency. A stale save, invalid role, missing user, or persistence failure is rejected and cannot partially update roles or audit data. Removing the last `Admin` or last `Approver` is rejected transactionally; the WPF editor warns and requires explicit confirmation before attempting a critical-role removal.

Every successful change records the administrator, affected username, role snapshots before and after the change, UTC timestamp, and one of `RoleAssigned`, `RoleRemoved`, or `RolesUpdated` in the authentication audit log.

When an administrator changes their own roles, the current session and authorization/menu state are refreshed immediately. Changes to other users are observed when their authenticated state is loaded next.

The role assignment regression suite covers the catalog, canonicalization, permission union, service authorization and session refresh, SQLite versioning/critical-role protection/audit rollback, and WPF editor and navigation behavior. The documentation check remains a manual review against these tests and the implementation.
