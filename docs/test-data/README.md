# NLP Test Dataset — 500 Concern Test Cases

`concern_test_cases_500.csv` is a synthetic set of 500 realistic citizen-concern
messages for testing/training `ConcernClassificationService`
(`VoxAngelos/Services/ConcernClassificationService.cs`), and for LGU staff to
manually verify before feeding back into the classifier's learning loop
(`LearnedKeyword`, `ClassificationCorrection`).

## Columns

| Column | Meaning |
|---|---|
| `id` | Sequential row ID (1–500). |
| `text` | The concern text, ready to paste into the citizen concern-submission form. |
| `language` | One of `English`, `Tagalog`, `Kapampangan`, `Taglish` (English/Tagalog code-switched). |
| `expected_category` | The department this concern *should* route to: one of `SWDO`, `CEO`, `CENRO`, `ACDO`, `PPTRO`, `OSCA`, `PWDAO` (see `ConcernClassificationService.Departments`). |
| `expected_urgency` | `Low`, `Medium`, or `High` — the author's intended urgency tier, independent of the app's Location Density Score (see `docs/urgency-algorithm-location-density-score.md`); useful for manually judging whether triage/response prioritization looks reasonable. |
| `lgu_verified` | **Left blank on purpose.** An LGU reviewer fills this in (`Correct` / `Incorrect — should be <dept>`) after checking what the live classifier actually assigned, per row. This is the human verification step described below. |
| `notes` | A short English gloss of the scenario, so a reviewer who isn't fluent in Kapampangan/Tagalog can still judge intent quickly. |

## Coverage

500 rows = 7 departments × 4 languages × 3 urgency tiers × 6 barangay/scenario
variations (with `PWDAO` trimmed slightly to land on exactly 500). Distribution:

```
SWDO: 72   CEO: 72   CENRO: 72   ACDO: 72   PPTRO: 72   OSCA: 72   PWDAO: 68
```

Barangay names, street landmarks, and scenario framing are drawn from real Angeles
City/Pampanga locations already referenced elsewhere in the app (seed data in
`Program.cs`, the OCR barangay lists in the face-verification service) so submissions
look like plausible local reports rather than generic placeholder text.

## Why the Kapampangan entries need LGU review

The Kapampangan rows were AI-generated using documented Kapampangan vocabulary and
sentence structure, but Kapampangan is a lower-resource language for AI generation than
Tagalog or English — some entries may contain grammatical inaccuracies or unnatural
phrasing a native speaker would immediately catch. This is precisely why the
`lgu_verified` column exists blank: **an LGU reviewer (ideally a native Kapampangan
speaker on staff) should read each Kapampangan/Taglish/Tagalog row, correct the phrasing
if needed, and confirm the `expected_category` before it's trusted as ground truth.**
Only after that human verification pass should a row be used to judge classifier
accuracy or fed into the learning loop — treat unverified rows as a first draft, not a
gold-standard test set.

## Suggested LGU verification workflow

1. Submit each row's `text` through the live concern form (or seed it directly via a
   script against the `Concerns` table) and record what `ConcernClassificationService`
   actually assigned.
2. Compare against `expected_category`. If it matches, mark `lgu_verified = Correct`.
   If not, either (a) the classifier misrouted it — a good candidate to demonstrate the
   Manual Override Feature (`docs/manual-override-feature.md`) — or (b) the
   `expected_category` itself was wrong/ambiguous and should be corrected in this CSV.
3. For Kapampangan/Tagalog/Taglish rows specifically, also have a native/fluent speaker
   confirm the phrasing reads naturally; fix wording directly in the CSV if not.
4. Once verified, this dataset becomes a reusable regression set — re-run it any time
   `DepartmentKeywords` or the Google NLP mapping changes, to check accuracy hasn't
   regressed (compare against the accuracy target tracked in
   `VoxAngelos/Pages/Admin/NlpAccuracy.cshtml.cs`).

## Regenerating

`generate_dataset.cs` is the throwaway C# script (`dotnet run` as a top-level program)
used to produce the CSV — kept here for transparency/reproducibility, not part of the
compiled `VoxAngelos` project. Edit its template strings and re-run it if you want to
add more scenarios or rebalance the language/department distribution.
