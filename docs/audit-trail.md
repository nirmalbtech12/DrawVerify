# Document audit trail

Document audit entries are append-only records scoped to a document. Each new record stores the actor identifier and role when available, specific action, offset-aware timestamp, affected revision/version, structured event details, workflow transition fields, and a predecessor/hash integrity link.

The existing `AuditLog.RetentionDays` setting controls retention. `0` means indefinite retention; a positive value allows the system retention process to purge entries older than the configured number of days. Negative values fail configuration validation. Regular users and administrators cannot mutate or delete audit entries, and retention is a system operation.

Authorized Approvers and Administrators can query and export a document trail. Queries support date range, action, actor, workflow status, revision, and text search. CSV export includes the persisted audit fields and uses CSV escaping for structured details.
