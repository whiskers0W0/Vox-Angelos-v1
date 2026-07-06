using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VoxAngelos.Data;

namespace VoxAngelos.Services
{
    /// <summary>
    /// Multi-category (Urgency / Relevance / Feasibility) star ratings for Recommendations,
    /// replacing the old upvote/downvote system. Two problems this solves:
    ///
    /// 1. Concurrency: many citizens can rate the same recommendation at once without
    ///    lost updates or lock contention. Both the per-citizen rating write and the
    ///    recommendation's cached aggregate recompute are single atomic SQL statements —
    ///    Postgres's own row-level locking on the UPDATE serializes concurrent raters of
    ///    the same recommendation, so no explicit application-level lock is needed.
    ///
    /// 2. Read cost: the leaderboard never aggregates raw rating rows at request time.
    ///    Aggregates are pre-computed into columns on Recommendation at write time, and
    ///    the top-N query on top of that is further cached in-memory with a short TTL,
    ///    so a page full of concurrent readers costs at most one DB round trip per TTL
    ///    window instead of one per request.
    /// </summary>
    public class RecommendationRatingService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;

        // Weighted toward Urgency/Relevance since the leaderboard's job is surfacing the
        // "most urgent / relevant" recommendations first; Feasibility still counts but
        // isn't the primary sort signal. Tune here only — nothing else needs to change.
        private const double UrgencyWeight = 0.45;
        private const double RelevanceWeight = 0.35;
        private const double FeasibilityWeight = 0.20;

        private const string CitizenLeaderboardCacheKey = "leaderboard:citizen";
        private const string LguLeaderboardCacheKey = "leaderboard:lgu";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);

        public RecommendationRatingService(ApplicationDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task SubmitRatingAsync(
            int recommendationId, string citizenId, int urgencyStars, int relevanceStars, int feasibilityStars)
        {
            ValidateStars(urgencyStars, relevanceStars, feasibilityStars);

            var now = DateTime.UtcNow;

            // Atomic upsert — a citizen re-rating overwrites their previous rating
            // instead of accumulating duplicate rows (matches the unique index on
            // (RecommendationId, CitizenId)).
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "RecommendationRatings"
                    ("RecommendationId", "CitizenId", "UrgencyStars", "RelevanceStars", "FeasibilityStars", "RatedAt", "UpdatedAt")
                VALUES ({recommendationId}, {citizenId}, {urgencyStars}, {relevanceStars}, {feasibilityStars}, {now}, NULL)
                ON CONFLICT ("RecommendationId", "CitizenId")
                DO UPDATE SET
                    "UrgencyStars" = {urgencyStars},
                    "RelevanceStars" = {relevanceStars},
                    "FeasibilityStars" = {feasibilityStars},
                    "UpdatedAt" = {now}
                """);

            // Recompute this recommendation's cached aggregates in one statement. Postgres
            // takes a row lock on the targeted Recommendations row for the duration of the
            // UPDATE, so two citizens rating the same recommendation at the same instant are
            // safely serialized by the database itself — the second writer's subquery simply
            // re-runs against the committed state of the first, no lost updates.
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Recommendations" r
                SET
                    "RatingCount" = agg."Count",
                    "AvgUrgency" = agg."AvgUrgency",
                    "AvgRelevance" = agg."AvgRelevance",
                    "AvgFeasibility" = agg."AvgFeasibility",
                    "CompositeScore" = (agg."AvgUrgency" * {UrgencyWeight})
                                      + (agg."AvgRelevance" * {RelevanceWeight})
                                      + (agg."AvgFeasibility" * {FeasibilityWeight})
                FROM (
                    SELECT
                        "RecommendationId",
                        COUNT(*) AS "Count",
                        AVG("UrgencyStars")::float AS "AvgUrgency",
                        AVG("RelevanceStars")::float AS "AvgRelevance",
                        AVG("FeasibilityStars")::float AS "AvgFeasibility"
                    FROM "RecommendationRatings"
                    WHERE "RecommendationId" = {recommendationId}
                    GROUP BY "RecommendationId"
                ) agg
                WHERE r."Id" = agg."RecommendationId"
                """);

            // Invalidate — the next leaderboard read recomputes the top-N instead of
            // serving a stale ranking for the rest of the TTL window.
            _cache.Remove(CitizenLeaderboardCacheKey);
            _cache.Remove(LguLeaderboardCacheKey);
        }

        public Task<RecommendationRating?> GetMyRatingAsync(int recommendationId, string citizenId) =>
            _db.RecommendationRatings.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RecommendationId == recommendationId && r.CitizenId == citizenId);

        public Task<Dictionary<int, RecommendationRating>> GetMyRatingsAsync(string citizenId) =>
            _db.RecommendationRatings.AsNoTracking()
                .Where(r => r.CitizenId == citizenId)
                .ToDictionaryAsync(r => r.RecommendationId);

        /// <summary>
        /// Top-N "most urgent/relevant" published recommendations, served from an
        /// in-memory cache when warm. Safe for a single-instance deployment (this app's
        /// current setup); if this ever scales to multiple instances, swap the cache
        /// for Redis (see class remarks in the project notes) so all instances share one
        /// view instead of each holding a slightly different cached ranking.
        /// </summary>
        public async Task<List<Recommendation>> GetTopRecommendationsAsync(bool forLgu, int count = 10)
        {
            var cacheKey = forLgu ? LguLeaderboardCacheKey : CitizenLeaderboardCacheKey;

            if (_cache.TryGetValue(cacheKey, out List<Recommendation>? cached) && cached != null)
                return cached;

            var top = await _db.Recommendations
                .Where(r => r.Status == "Published" && r.RatingCount > 0)
                .OrderByDescending(r => r.CompositeScore)
                .Take(count)
                .Include(r => r.Citizen).ThenInclude(u => u.UserProfile)
                .Include(r => r.Attachments)
                .AsNoTracking()
                .ToListAsync();

            _cache.Set(cacheKey, top, CacheTtl);
            return top;
        }

        private static void ValidateStars(params int[] values)
        {
            foreach (var v in values)
                if (v < 1 || v > 5)
                    throw new ArgumentOutOfRangeException(nameof(values), "Star rating must be between 1 and 5.");
        }
    }
}
