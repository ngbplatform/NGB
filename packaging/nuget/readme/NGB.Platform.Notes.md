# NGB.Platform.Notes

Provider-neutral plain-text note service contracts, metadata records, limits and exceptions for NGB Platform.

Notes use an independent feature flag and require no object storage. Runtime enforces permissions and optimistic concurrency, and records creation, edits and logical deletion in the parent's standard AuditLog.

See the [Attachments & Notes guide](https://github.com/ngbplatform/NGB/blob/main/docs/guides/attachments-and-notes.md) for configuration, lifecycle, permissions and operations.
