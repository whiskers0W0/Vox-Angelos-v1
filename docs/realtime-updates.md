# Realtime Updates (SignalR)

## What

Three things previously required a manual page refresh to see: a new concern arriving
on the LGU dashboard, a new citizen recommendation being published to Discover, and a
rating average changing after someone rates a post. All three now push instantly to
every affected connected client via `FeedHub` (`VoxAngelos/Hubs/FeedHub.cs`), a thin
SignalR hub mapped at `/hubs/feed` (`Program.cs`).

## How it's wired

- On connect, `FeedHub.OnConnectedAsync` joins each client to a group based on role:
  citizens (`User` role) join `discover-feed`; LGU staff join `lgu-{Department}` (their
  own department, looked up via `UserManager`).
- The hub has no client-callable methods — it's push-only. Each Razor Page handler
  that already owns the relevant data change sends the broadcast itself, via
  `IHubContext<FeedHub>`:
  - `Pages/User/Create.cshtml.cs` → new concern submitted → `ConcernFeedChanged` to the
    assigned department's group (or all departments, if NLP left it unclassified).
  - `Pages/LGU/Index.cshtml.cs` → category reassigned / status updated / concern
    claimed → `ConcernFeedChanged` to the relevant department group(s).
  - `Pages/LGU/ReviewRecommendations.cshtml.cs` → recommendation approved →
    `PostPublished` to `discover-feed`.
  - `Pages/User/Index.cshtml.cs` → rating submitted → `RatingUpdated` (with the fresh
    aggregate numbers) to `discover-feed`.
- Client side: `wwwroot/js/realtime.js` opens one shared connection per page and
  forwards each event to whatever handlers the current page registered on
  `window.VoxAngelosRealtime`. `LGU/Index.cshtml` re-fetches and swaps in the concern
  feed HTML on `ConcernFeedChanged`; `User/Index.cshtml` patches the specific rating
  widget's DOM on `RatingUpdated`, and re-fetches the proposals feed on `PostPublished`.
- Both `_CitizenLayout.cshtml` and `_LGULayout.cshtml` load the SignalR JS client
  (`wwwroot/lib/signalr/signalr.min.js`) and `realtime.js` ahead of each page's own
  script section.

## Why push instead of polling

`LGU/Index.cshtml` previously polled every 15 seconds and re-fetched the whole page to
diff card counts — up to a 15s delay, and it couldn't detect a status/category change
that didn't add or remove a card. The SignalR push fires the moment the underlying
data actually changes, for any kind of change, with no polling delay. A slow poll
(45s) is kept as a fallback only, in case a client's socket connection drops.
