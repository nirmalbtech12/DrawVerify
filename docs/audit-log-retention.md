# Audit log retention

`AuditLog.RetentionDays` is read from `appsettings.json`. A value of `0` means indefinite retention; a positive value permits the internal system retention operation to archive or purge entries older than that many days. Negative values are invalid and fail configuration validation at startup.

Audit entries have append/query behavior only in application-facing services. Retention is a system operation and is not exposed through user or administrator mutation APIs. The local SQLite file remains an operational trust boundary: direct filesystem/database access can bypass application authorization. Retention failures and configuration failures are logged centrally and surfaced to the invoking operation.
