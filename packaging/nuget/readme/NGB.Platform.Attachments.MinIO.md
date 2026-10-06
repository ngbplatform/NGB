# NGB.Platform.Attachments.MinIO

MinIO object-storage adapter for NGB Platform attachments.

Provides short-lived upload/download URLs, uploaded-object verification and conditional server-side copy to completed attachment keys. Register it through the API's conditional storage callback so Notes and disabled Attachments do not require MinIO. The attachment lifecycle retains objects indefinitely.

See the [Attachments & Notes guide](https://github.com/ngbplatform/NGB/blob/main/docs/architecture/attachments-and-notes.md) for configuration, lifecycle, permissions and operations.
