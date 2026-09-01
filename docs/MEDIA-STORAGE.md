# Local Media Storage (Phase 7)

Customer panel-label photographs are stored on the **local private filesystem**. They are **not** in Git, **not** publicly served, and **not** uploaded to cloud object storage in this phase.

## Location

Configure via environment variable or `appsettings.json`:

```text
MEDIA_STORAGE_ROOT=./data/media
```

Relative paths resolve from the ASP.NET content root. The directory is created automatically on first write.

Additional settings:

| Variable | Purpose | Default |
|----------|---------|---------|
| `MEDIA_MAX_FILE_SIZE_BYTES` | Maximum inbound image size | `10485760` (10 MB) |
| `MEDIA_RETENTION_DAYS` | Automatic deletion (0 = disabled) | `0` |

## File naming

Stored objects use **opaque keys** — never customer phone numbers:

```text
cases/{technicalSupportCaseId}/{randomGuid}.jpg
cases/{technicalSupportCaseId}/{randomGuid}.png
cases/{technicalSupportCaseId}/{randomGuid}.webp
```

- `PanelLabelPhoto.StorageKey` holds this key.
- `Message.StorageKey` is set to the same key for inbound images.
- Files are written atomically (`.tmp` then rename).

## Database mapping

| Table / field | Role |
|---------------|------|
| `messages.storage_key` | Links inbound WhatsApp message to stored binary |
| `panel_label_photos.storage_key` | Private object key (not a URL) |
| `panel_label_photos.sort_order` | Order within a support case |
| `panel_label_photos.content_type` | Validated MIME (`image/jpeg`, `image/png`, `image/webp`) |
| `panel_label_photos.size_bytes` | Stored file size |
| `messages.external_message_id` | Idempotency for duplicate webhook delivery |

Only rows with a **validated stored image** (`size_bytes > 0`, key prefix `cases/`) satisfy the workflow photo requirement.

## Backup

Media is **local data**. Back up `./data/media` separately (e.g. copy to external drive or backup tool). Database rows without matching files are orphaned references.

## Deletion

No automatic cleanup runs when `MEDIA_RETENTION_DAYS=0` (default). To remove media manually:

1. Delete the file under `MEDIA_STORAGE_ROOT` using the `storage_key` path.
2. Remove or archive the corresponding `panel_label_photos` row if required.

Retention automation is **deferred** — do not expect silent deletion during normal workflow.

## New computer / clone

Cloning the repository does **not** include media files (`data/media/` is gitignored). Copy `data/media` manually if you need historical photos on another machine.

## Security

- No static file middleware serves this directory.
- Admin UI does not expose raw media URLs in Phase 7.
- Logs must not contain image bytes, phone numbers, or session material.

## WhatsApp download flow

```text
Baileys (connector) → metadata webhook → ASP.NET
ASP.NET → POST /download/media { externalMessageId } → connector
Connector → Baileys downloadMediaMessage (cached stub) → bytes
ASP.NET → validate → LocalMediaStorage → PostgreSQL reference
```

Vision / OCR analysis is **deferred**; photo receipt is determined solely by the media pipeline.
