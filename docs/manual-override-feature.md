# Manual Override Feature (NLP Automated Routing)

## Why this exists

Vox Angelos routes every submitted concern to one of 7 LGU departments (`SWDO`,
`CEO`, `CENRO`, `ACDO`, `PPTRO`, `OSCA`, `PWDAO`) using `ConcernClassificationService`
(`VoxAngelos/Services/ConcernClassificationService.cs`). Classification comes from two
layers, tried in order:

1. **Google Cloud Natural Language API** (`ClassifyTextAsync`) — used when the
   description is at least 20 words long. Its general-purpose category taxonomy is
   mapped to our 7 departments via `GoogleCategoryMap`.
2. **A bilingual/trilingual keyword scorer** (`DepartmentKeywords`, English/Tagalog/
   Kapampangan) — used as a fallback for short text or when Google NLP is unavailable,
   and continuously tuned by `LearnedKeyword` weights (see the feedback loop below).

Both layers are heuristic and will occasionally misroute a concern — e.g. a report
about an elderly person being denied a business permit could plausibly read as either
`OSCA` (senior citizen) or `ACDO` (permits). Since an LGU department only sees concerns
assigned to it (`LGU/Index.cshtml.cs`, `OnGetAsync`, filters by `userDepartment`), a
misrouted concern would otherwise sit unseen in the wrong department's queue
indefinitely. The Manual Override Feature exists so an LGU staff member can correct
this at the point they notice it, without needing developer/admin intervention.

## Where it lives

| Piece | File |
|---|---|
| UI — concern card + reassign modal | `VoxAngelos/Pages/LGU/Index.cshtml` (lines ~76–95, ~197–230) |
| Handler | `VoxAngelos/Pages/LGU/Index.cshtml.cs` → `OnPostReassignCategoryAsync` |
| "Confirm as correct" counterpart | `VoxAngelos/Pages/LGU/Index.cshtml.cs` → `OnPostConfirmCategoryAsync` |
| Business logic + audit log write | `VoxAngelos/Services/ConcernClassificationService.cs` → `RecordCorrectionAsync` |
| Audit trail table | `VoxAngelos/Data/ClassificationCorrection.cs` |
| Accuracy dashboard (uses this data) | `VoxAngelos/Pages/Admin/NlpAccuracy.cshtml.cs` |

## How it works, end to end

1. Every concern card in the LGU feed shows its NLP-assigned category
   (`Category: <dept>`) and, if not yet reviewed, two buttons:
   **"✓ Category Correct"** and **"✕ Wrong Department"**.
2. Clicking **"✕ Wrong Department"** opens the *Reassign to Correct Department* modal,
   which lists all 7 departments in a dropdown (`Model.Departments`,
   `ConcernClassificationService.Departments`).
3. Submitting the modal posts to `OnPostReassignCategoryAsync(concernId, newCategory)`,
   which:
   - Validates `newCategory` is one of the 7 known departments (rejects anything else
     with `400 Bad Request` — an LGU admin cannot free-text a department name).
   - Calls `ConcernClassificationService.RecordCorrectionAsync(concernId, newCategory,
     wasCorrect: false, reviewerUserId)`.
4. `RecordCorrectionAsync` runs the override + audit write as one transaction:
   - Inserts a `ClassificationCorrection` row recording `PreviousCategory`,
     `CorrectedCategory`, `WasCorrect = false`, `ReviewedByUserId`, `ReviewedAt` — a
     permanent, queryable audit trail of every override, by whom and when.
   - Updates `Concern.Category` to the corrected department, so the concern now shows
     up in the correct department's queue immediately (and — see
     `docs/realtime-updates.md` — every LGU dashboard viewing that department's queue
     is pushed a live refresh over SignalR, no manual page reload needed).
   - Feeds the correction back into the classifier's learning weights
     (`LearnedKeyword`, `UpsertLearnedWeightAsync`): keywords from the concern's
     description gain weight toward the corrected department and lose weight toward
     the wrong one, so future similar concerns are less likely to repeat the mistake.
5. **Concurrency safety**: `ClassificationCorrection.ConcernId` has a unique DB index
   (`ApplicationDbContext.cs`), so if two LGU staff try to review the same concern at
   once, only the first write succeeds — the second gets a friendly "already reviewed
   by another staff member" message (`ConcernAlreadyReviewedException`) instead of a
   silent double-correction.

## Why this satisfies "kung sakaling mag-misclassify ang Google Natural Language API"

- The override is **manual and immediate** — no redeploy, no admin console, no waiting
  for a batch job. Any authenticated LGU staffer in `Department` X can act the moment
  they see a wrongly-routed concern in their queue.
- It is **auditable** — every override is logged with who did it and when
  (`ClassificationCorrection`), which is what a panel or a future audit would need to
  see to trust the system isn't silently reassigning things.
- It **closes the loop with the NLP itself** — corrections directly adjust
  `LearnedKeyword` weights, so the override doesn't just fix one concern, it also
  reduces the odds of the same misclassification recurring (see
  `VoxAngelos/Pages/Admin/NlpAccuracy.cshtml.cs` for the resulting accuracy metric,
  tracked against a 75% target).

## Demonstrating it for defense

1. Submit a concern whose description is ambiguous across two departments (see the
   generated test dataset, `docs/test-data/concern_test_cases_500.csv`, for ready-made
   examples per department/language).
2. Log in as the LGU account for the department it *was* routed to.
3. Click **"✕ Wrong Department"**, pick the correct department, submit.
4. Show the concern now appears (instantly, via the realtime push) in the correct
   department's LGU dashboard, and that `ClassificationCorrection` recorded the
   reviewer and timestamp (queryable directly, or via `Admin/NlpAccuracy`).
