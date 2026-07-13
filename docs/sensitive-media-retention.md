# Sensitive Media Retention (3-Day Auto-Delete)

## What

ID photos and live selfies uploaded during registration are the most sensitive data
the app collects (biometric + government-ID images). `SensitiveMediaRetentionService`
(`VoxAngelos/Services/SensitiveMediaRetentionService.cs`) is a `BackgroundService`
that runs on a timer (default: every 60 minutes, `MediaRetention:PollIntervalMinutes`
in `appsettings.json`) and, for any `UserIdentityDocument`/`UserFaceVerification` row
older than the retention window (default: **3 days**,
`MediaRetention:SensitiveDataRetentionDays`):

1. Deletes the physical file from `wwwroot/uploads/ids/` or `wwwroot/uploads/selfies/`.
2. Nulls out the DB path column (`IdPhotoPath` / `LiveSelfiePath`).

The verification **outcome** (match confidence, status, OCR-extracted fields,
timestamps) is intentionally kept — only the raw image files and their file
references are purged, which is what actually carries re-identification/biometric
risk. This keeps an auditable record that verification happened, without holding the
sensitive images indefinitely, per RA 10173 (Data Privacy Act) data-minimization
practice for sensitive personal information.

The deletion covers both image and video uploads — it operates on whatever file the
stored path points to, regardless of extension, so it also covers a future
video-format selfie without any code change.

Registered in `Program.cs` via `builder.Services.AddHostedService<...>()`, so it starts
automatically with the app and needs no external scheduler (cron/Hangfire).
