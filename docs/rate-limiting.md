# API Rate-Limiting ("Denial of Wallet" Mitigation)

## The threat

Two endpoints in Vox Angelos each trigger calls to paid/quota-limited external APIs
per request:

| Endpoint | External calls triggered |
|---|---|
| Registration → identity verification step (`Areas/Identity/Pages/Account/Register.cshtml.cs`, `OnPostVerifyIdentityAsync` / `OnPostCreateAccountAsync`) | Google Cloud Vision (OCR, via `OcrService`) **and** the Hugging-Face-hosted face/ID verification API (`FaceVerificationService` → `FaceApi:BaseUrl`, deployed at `huggingface.co/spaces/yojiyo/vox-angelos-api` — an InsightFace + Tesseract Flask service exposing `/verify`, `/validate-id`, `/ocr-id`) |
| Concern / recommendation submission (`Pages/User/Create.cshtml.cs`, `OnPostAsync` / `OnPostRecommendationAsync`) | Google Cloud Natural Language (`ConcernClassificationService.ClassifyAsync` → `LanguageServiceClient.ClassifyTextAsync`) |

Neither endpoint previously had any request throttling. A scripted bot could hit
either one in a tight loop — each hit consumes a unit of the app's Google Cloud and
Hugging Face Space quota — without ever needing a valid account. Since both quotas are
either paid (Google Cloud) or capped free-tier compute (Hugging Face Spaces), this is
a **Denial of Wallet** attack: the attacker doesn't need to bring the app down, just
exhaust the budget/quota so the app stops functioning for everyone else once the cap
is hit.

## Mitigation

ASP.NET Core's built-in rate limiter (`Microsoft.AspNetCore.RateLimiting`, part of the
shared framework — no extra package needed) is configured in `Program.cs` with two
named fixed-window policies:

```csharp
options.AddPolicy("registration", httpContext => RateLimitPartition.GetFixedWindowLimiter(
    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    factory: _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 5,
        Window = TimeSpan.FromMinutes(5),
        QueueLimit = 0
    }));

options.AddPolicy("concern-submission", httpContext => RateLimitPartition.GetFixedWindowLimiter(
    partitionKey: httpContext.User.Identity?.IsAuthenticated == true
        ? httpContext.User.Identity!.Name!
        : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    factory: _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(10),
        QueueLimit = 0
    }));
```

- **`registration`** — 5 requests per 5 minutes, partitioned by client IP (registration
  happens before a user has an account, so IP is the only available partition key).
  Covers both the OCR/face-verify step and account creation.
- **`concern-submission`** — 10 requests per 10 minutes, partitioned by the
  authenticated user's identity (falls back to IP if somehow unauthenticated). Covers
  both concern and recommendation submission, since both call the same
  `ConcernClassificationService.ClassifyAsync`.
- **`QueueLimit = 0`** — once the limit is hit, excess requests are rejected
  immediately (HTTP `429 Too Many Requests`) rather than queued, so a burst can't tie
  up server threads waiting to retry.
- A shared `OnRejected` handler returns a small JSON body
  (`{"success":false,"error":"Too many requests..."}`) so the existing AJAX handlers on
  both pages (which already expect JSON responses) degrade gracefully instead of
  showing a raw blank 429.

Applied via `[EnableRateLimiting("...")]` on the page model classes:
- `RegisterModel` (`Areas/Identity/Pages/Account/Register.cshtml.cs`) → `"registration"`
- `CreateModel` (`Pages/User/Create.cshtml.cs`) → `"concern-submission"`

`app.UseRateLimiter()` is wired into the pipeline in `Program.cs`, after
`UseAuthorization()` and before `MapRazorPages()`.

## Why fixed-window, and why these numbers

A fixed window (rather than a token bucket or sliding window) was chosen because the
threat model here is "a bot hammering one endpoint in a loop," not "smoothing out
legitimate bursty traffic" — a hard cap per IP/user per window is simplest to reason
about and cheapest to compute. The limits (5/5min for registration, 10/10min for
submission) are set well above any plausible legitimate usage (a real person does not
register 5 times or submit 10 concerns inside a few minutes) while still capping the
worst case a single bot instance can cost in Google Cloud / Hugging Face quota.

## Demonstrating it for defense

1. Script (or manually repeat) more than 5 POSTs to the registration verify step from
   the same machine within 5 minutes — the 6th receives `429` with the JSON rejection
   body instead of hitting Google Cloud/Hugging Face at all.
2. Same for concern submission: the 11th submission inside 10 minutes as the same
   logged-in user is rejected before `ConcernClassificationService.ClassifyAsync` runs.
