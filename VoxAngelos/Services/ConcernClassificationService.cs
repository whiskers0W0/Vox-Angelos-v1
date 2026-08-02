using Microsoft.EntityFrameworkCore;
using Npgsql;
using VoxAngelos.Data;

namespace VoxAngelos.Services
{
    // Thrown when two LGU staff try to log a verdict on the same concern at once —
    // the unique index on ClassificationCorrection.ConcernId lets only one win.
    public class ConcernAlreadyReviewedException(int concernId, Exception inner)
        : Exception($"Concern {concernId} was already reviewed by another staff member.", inner)
    {
        public int ConcernId { get; } = concernId;
    }

    // Which tier of the classifier chain produced a result — surfaced to the LGU/citizen
    // UI (browser console) so it's obvious whether the HF model or a local fallback answered.
    public record ClassificationResult(string? Department, string Tier);

    public class ConcernClassificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ConcernClassificationService> _logger;
        private readonly HfConcernClassifierService _hfClassifier;

        public ConcernClassificationService(
            ApplicationDbContext db,
            ILogger<ConcernClassificationService> logger,
            HfConcernClassifierService hfClassifier)
        {
            _db = db;
            _logger = logger;
            _hfClassifier = hfClassifier;
        }

        // Departments this classifier can route to — used to validate corrections.
        public static readonly string[] Departments =
            ["SWDO", "CEO", "CENRO", "ACDO", "PTRO", "OSCA", "PWDAO"];

        // The 17 fine-grained categories the HF model predicts. Which department (if any)
        // each one routes to is admin-editable (Admin → Office Management → Categories) rather
        // than fixed in the HF Space's office_map.json, so a category can be reassigned —
        // including one with no real office yet — without touching the model or its host.
        public static readonly string[] Categories =
        [
            "infrastructure", "urban_planning", "business_permits",
            "environment", "sanitation", "waste_management",
            "public_safety", "traffic",
            "pwd_affairs", "senior_citizen", "social_services",
            "health", "education", "employment", "animal_welfare",
            "disaster_risk_reduction", "utilities"
        ];

        // Read-only view of the built-in keyword lists, keyed by department — used to
        // seed an office's admin-editable NLP tags (Office Management) with a sensible
        // starting point instead of an empty list.
        public static IReadOnlyDictionary<string, string[]> DefaultKeywordsByDepartment => DepartmentKeywords;

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "and", "or", "but",
            "of", "to", "in", "on", "at", "for", "with", "this", "that", "it", "as", "by",
            "there", "here", "have", "has", "had", "not", "no", "yes", "please", "very",
            "ang", "ng", "sa", "at", "na", "po", "ay", "mga", "ito", "yan", "yung", "dahil",
            "kasi", "para", "may", "wala", "din", "rin", "kami", "kayo", "sila", "ako", "ikaw"
        };
        // Local keyword lists — fallback when the HF classifier is unavailable
        private static readonly Dictionary<string, string[]> DepartmentKeywords =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["SWDO"] =
            [
                // English
                "welfare", "social welfare", "social service", "social services",
                "financial aid", "financial assistance", "disaster relief", "relief goods",
                "relief", "calamity", "evacuation", "evacuee", "shelter",
                "vulnerable", "indigent", "poor", "poverty",
                "children", "child", "minors", "minor", "orphan", "orphans",
                "women", "woman", "single mother", "abused",
                "homeless", "squatter", "informal settler",
                "livelihood", "subsidy", "stipend", "allowance", "cash aid",
                "community development", "beneficiary", "4ps", "pantawid",
                "dswd", "social protection", "human services",
                "family assistance", "crisis", "humanitarian", "charity",
                "senior benefits", "burial", "burial assistance",
                // Filipino / Tagalog
                "tulong", "ayuda", "benepisyo", "mahirap", "pamilya",
                "bata", "batang", "kabataan", "kababaihan", "babae",
                "biyudo", "biyuda", "ulila", "walang tirahan", "walang matirhan",
                "gutom", "pagkain", "likas", "lindol", "bagyo", "sakuna", "kalamidad",
                "lumikas", "pabahay", "subsidyo", "hanapbuhay",
                "nasunog", "nawala", "nawalan", "biktima", "nasalanta",
                // Kapampangan
                "saup", "tutulungan", "masalat", "pengari", "anak", "babai",
                "kekatamu", "kapamilya"
            ],

            ["CEO"] =
            [
                // English
                "road", "roads", "street", "streets", "highway",
                "bridge", "bridges", "overpass", "underpass",
                "drainage", "drain", "canal", "culvert", "sewer", "sewage",
                "flood drain", "storm drain", "waterway",
                "building", "structure", "public building", "government building",
                "construction", "reconstruct", "construct",
                "maintenance", "repair", "dilapidated", "deteriorating", "damaged",
                "infrastructure", "urban infrastructure", "public works",
                "pavement", "asphalt", "concrete", "cement",
                "sidewalk", "footpath", "walkway",
                "pothole", "potholes", "bumpy", "rough road",
                "street light", "streetlight", "lamp post", "light post",
                "retaining wall", "dike", "levee", "embankment",
                "demolition", "renovation", "reblocking",
                "civil engineering", "engineer", "engineer office",
                // Filipino / Tagalog
                "kalsada", "daan", "tulay", "imburnal", "alkantarila", "estero",
                "gusali", "konstruksyon", "pagkukumpuni", "sira", "nasira",
                "bulok", "may sira", "humukay", "butas", "bangketa",
                "poste", "ilaw ng daan", "poste ng ilaw",
                "patubig", "linya ng tubig", "graba", "semento",
                "nabasa ang daan", "gumuho", "guho", "nabasag",
                // Kapampangan
                "dalan", "labuad", "tultul", "sira ing", "kapag-ula",
                "altu"
            ],

            ["CENRO"] =
            [
                // English
                "environment", "environmental",
                "waste", "garbage", "trash", "rubbish", "litter", "littering",
                "waste management", "waste disposal", "illegal dumping", "dump site",
                "recycling", "recycle", "composting",
                "pollution", "air pollution", "water pollution", "noise pollution",
                "smoke", "smokebelching", "smoke belching",
                "sanitation", "cleanliness", "unsanitary",
                "stagnant water", "standing water", "mosquito", "dengue", "rats", "pests",
                "trees", "tree cutting", "illegal logging", "deforestation",
                "forest", "vegetation", "plants",
                "flooding", "flood", "waterlogging",
                "sustainability", "natural resources", "soil", "erosion",
                "water quality", "clean water", "contamination",
                "cenro", "environment office", "green",
                // Filipino / Tagalog
                "basura", "kalat", "dumi", "maruming", "tambak",
                "nagtatapon", "nagtapon", "nagtatambak",
                "usok", "amoy", "mabaho", "masamang amoy",
                "polusyon",
                "puno", "punutin", "nagputol", "nagpaputol",
                "ilog", "lawa", "tubig", "maputik", "putik",
                "lamok", "ipis", "daga", "langaw",
                "baha", "pagbabaha", "binabaha",
                // Kapampangan
                "masamit", "ambu", "maligpit", "kalikasan",
                "sukal", "balu"
            ],

            ["ACDO"] =
            [
                // English
                "urban planning", "city planning", "land use", "land use plan",
                "development plan", "master plan", "comprehensive plan",
                "zoning", "rezoning", "zone", "land conversion",
                "permit", "permits", "building permit", "business permit",
                "economic development", "economic growth", "investment",
                "city development", "strategic planning", "policy",
                "subdivision", "commercial", "land", "property",
                "urban development", "acdo", "city development office",
                "development office", "city planner",
                // Filipino / Tagalog
                "plano", "pagpaplano", "lupa", "titulo ng lupa",
                "lisensya", "pahintulot", "negosyo", "pamumuhunan",
                "komersyal", "pagpapaunlad", "proyekto ng lungsod",
                "lungsod", "bayan", "siyudad",
                // Kapampangan
                "pamintuan", "kalungsoran", "planu"
            ],

            ["PTRO"] =
            [
                // English
                "traffic", "traffic flow", "traffic congestion", "traffic jam",
                "traffic signal", "traffic light", "traffic sign", "road sign",
                "signage", "road safety",
                "parking", "no parking", "illegal parking", "parking violation",
                "transportation", "public transportation", "public transport",
                "vehicle", "vehicles", "motor vehicle",
                "enforcement", "traffic enforcer", "traffic officer",
                "congestion", "bottleneck",
                "accident", "road accident", "vehicular accident", "collision",
                "speeding", "reckless driving", "overspeeding",
                "counterflow", "overloading",
                "jeepney", "tricycle", "motorcycle", "bus", "truck",
                "commute", "commuter", "urban mobility",
                "pptro", "traffic regulation", "traffic office",
                // Filipino / Tagalog
                "trapik", "trapiko", "sasakyan", "motorsiklo",
                "traysikel", "dyipni", "dyipney",
                "parkir", "nakaparada", "iligal na pagpaparada",
                "aksidente", "banggaan", "mabilis na pagmamaneho",
                "pasahero", "drayber", "driver", "semaporo", "senyales",
                "kontra-agos", "kontraagos", "labis na pasahero",
                "biyahe", "ruta", "lulan",
                // Kapampangan
                "motorsiklu", "trapiku", "nakaparada"
            ],

            ["OSCA"] =
            [
                // English
                "senior", "senior citizen", "senior citizens", "elderly",
                "old age", "aged", "aging",
                "retirement", "retired", "pensioner",
                "osca", "senior id", "osca id", "senior card",
                "benefits for senior", "senior discount", "senior privileges",
                "geriatric", "nursing home", "care home", "assisted living",
                "lolo", "lola", "grandparent", "grandparents",
                "manong", "manang", "elderly care",
                // Filipino / Tagalog
                "matanda", "matatanda", "nakatatanda",
                "ingkong", "impo",
                "card ng senior", "diskwento ng matanda",
                "benepisyo ng matanda", "pensyon",
                "retirado", "retirada",
                // Kapampangan
                "matatua", "magtua"
            ],

            ["PWDAO"] =
            [
                // English
                "disability", "disabled", "person with disability", "persons with disability",
                "pwd", "pwd id", "disability card", "disability benefits",
                "wheelchair", "wheelchair ramp", "ramp", "ramps",
                "accessibility", "accessible", "barrier", "barrier free",
                "inclusion", "inclusive", "special needs",
                "blind", "visually impaired", "vision impaired",
                "deaf", "hearing impaired", "mute",
                "amputee", "cerebral palsy", "autism", "autistic",
                "mental health", "differently abled", "handicapped",
                "sign language", "pwdao", "disability office",
                // Filipino / Tagalog
                "may kapansanan", "kapansanan", "may difabilidad",
                "hindi makalakad", "bulag", "bingi", "pipi",
                "gulong-gulong",
                "espesyal na pangangailangan", "nangangailangan",
                "id ng pwd", "card ng pwd", "diskwento ng pwd",
                "hadlang", "may depekto", "lumpo", "paralysado",
                // Kapampangan
                "bungul", "di makatupad", "espesyal"
            ],
        };

        // ── Public API ────────────────────────────────────────────────────────

        // A department must clear this score, with no tie against the runner-up, before
        // a classifier result (learned weights, or the static keyword fallback) is
        // trusted — otherwise a single incidental keyword hit (e.g. "anak" matching a
        // curse word, or a nonsense phrase) would confidently mislabel the concern
        // instead of falling through to Uncategorized for a human to triage.
        private const int MinConfidentClassificationScore = 2;

        /// <summary>
        /// Primary: our self-trained TF-IDF + SVM concern classifier (HF Space). Falls
        /// back to local keyword scoring when the model is unavailable or predicts a
        /// category with no real office yet (FUTURE_EXPANSION): LGU-verified corrections
        /// win outright if they have a confident signal, otherwise local keyword scoring
        /// blended with learned weights.
        /// </summary>
        public async Task<ClassificationResult> ClassifyAsync(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return new ClassificationResult(null, "None");

            var hfPrediction = await _hfClassifier.ClassifyAsync(description);
            if (hfPrediction?.Category != null)
            {
                // Admin-assigned category→department mapping (Office Management) wins over
                // the HF Space's own office_map.json — this is what lets a FUTURE_EXPANSION
                // category get a real office later without redeploying the Space.
                var categoryDepartments = await LoadCategoryDepartmentsAsync();
                if (categoryDepartments.TryGetValue(hfPrediction.Category, out var mappedDepartment))
                {
                    _logger.LogInformation(
                        "HF classifier CLASSIFIED: category={Category} department={Department} (admin mapping)",
                        hfPrediction.Category, mappedDepartment);
                    return new ClassificationResult(mappedDepartment, "HF Model");
                }

                var office = hfPrediction.Office;
                if (!string.IsNullOrWhiteSpace(office) && office != "FUTURE_EXPANSION" && Departments.Contains(office))
                {
                    _logger.LogInformation(
                        "HF classifier CLASSIFIED: category={Category} department={Department}",
                        hfPrediction.Category, office);
                    return new ClassificationResult(office, "HF Model");
                }

                _logger.LogInformation(
                    "HF classifier predicted category={Category} office={Office} — no usable department, falling back",
                    hfPrediction.Category, office ?? "(none)");
            }

            var learned = await LoadLearnedWeightsAsync();

            var confidentLearnedResult = ClassifyFromLearnedWeightsOnly(description, learned);
            if (confidentLearnedResult != null)
            {
                _logger.LogInformation(
                    "FALLBACK (learned weights only) CLASSIFIED: department={Department}", confidentLearnedResult);
                return new ClassificationResult(confidentLearnedResult, "Learned Weights");
            }

            var departmentTags = await LoadDepartmentTagsAsync();
            var keywordResult = Classify(description, learned, departmentTags);
            _logger.LogInformation(
                "FALLBACK (keyword scoring) CLASSIFIED: department={Department}", keywordResult ?? "(Uncategorized)");
            return new ClassificationResult(keywordResult, "Keyword Scoring");
        }

        /// <summary>
        /// Scores only human-verified learned keywords (no static list). Returns a
        /// department solely when the top score clearly beats the runner-up and meets
        /// a minimum bar — a single stray correction shouldn't override everything.
        /// </summary>
        private static string? ClassifyFromLearnedWeightsOnly(
            string description, IReadOnlyDictionary<string, Dictionary<string, int>> learnedWeights)
        {
            if (string.IsNullOrWhiteSpace(description) || learnedWeights.Count == 0)
                return null;

            var lower = description.ToLowerInvariant();
            var scores = new Dictionary<string, int>();

            foreach (var (department, wordWeights) in learnedWeights)
            {
                int score = wordWeights
                    .Where(ww => ww.Value > 0 && lower.Contains(ww.Key))
                    .Sum(ww => ww.Value);
                if (score > 0)
                    scores[department] = score;
            }

            if (scores.Count == 0)
                return null;

            var ranked = scores.OrderByDescending(s => s.Value).ToList();
            var isConfident = ranked[0].Value >= MinConfidentClassificationScore
                && (ranked.Count == 1 || ranked[0].Value > ranked[1].Value);

            return isConfident ? ranked[0].Key : null;
        }

        /// <summary>
        /// Local keyword-only classifier — no network call, always available.
        /// Scores the static keyword lists plus any learned weights from LGU feedback,
        /// plus admin-curated tags entered per office in Office Management. Returns null
        /// (Uncategorized) unless the top department clears <see cref="MinConfidentClassificationScore"/>
        /// with no tie, so a single stray keyword match can't confidently mislabel a concern.
        /// </summary>
        public string? Classify(
            string description,
            IReadOnlyDictionary<string, Dictionary<string, int>>? learnedWeights = null,
            IReadOnlyDictionary<string, List<string>>? departmentTags = null)
        {
            if (string.IsNullOrWhiteSpace(description))
                return null;

            var lower = description.ToLowerInvariant();
            var scores = new Dictionary<string, int>();

            foreach (var (department, keywords) in DepartmentKeywords)
            {
                int score = keywords.Count(kw => lower.Contains(kw.ToLowerInvariant()));
                if (score > 0)
                    scores[department] = score;
            }

            if (learnedWeights != null)
            {
                foreach (var (department, wordWeights) in learnedWeights)
                {
                    int score = wordWeights
                        .Where(ww => ww.Value != 0 && lower.Contains(ww.Key))
                        .Sum(ww => ww.Value);

                    if (score == 0) continue;
                    scores[department] = scores.GetValueOrDefault(department) + score;
                    if (scores[department] <= 0) scores.Remove(department);
                }
            }

            if (departmentTags != null)
            {
                foreach (var (department, tags) in departmentTags)
                {
                    // Admin-curated tags are a deliberate routing signal, so they outweigh
                    // a single incidental hit from the static keyword list.
                    int score = tags.Count(t => !string.IsNullOrWhiteSpace(t) && lower.Contains(t.ToLowerInvariant())) * 2;
                    if (score == 0) continue;
                    scores[department] = scores.GetValueOrDefault(department) + score;
                }
            }

            if (scores.Count == 0)
                return null;

            var ranked = scores.OrderByDescending(s => s.Value).ToList();
            var isConfident = ranked[0].Value >= MinConfidentClassificationScore
                && (ranked.Count == 1 || ranked[0].Value > ranked[1].Value);

            return isConfident ? ranked[0].Key : null;
        }

        /// <summary>
        /// Loads admin-curated NLP tags per department from LGU office accounts
        /// (set in Admin → Office Management), merging tags across accounts that
        /// share a department.
        /// </summary>
        private async Task<Dictionary<string, List<string>>> LoadDepartmentTagsAsync()
        {
            var rows = await _db.Users
                .Where(u => u.Department != null)
                .Select(u => new { u.Department, u.Tags })
                .ToListAsync();

            return rows
                .Where(r => r.Tags.Count > 0)
                .GroupBy(r => r.Department!)
                .ToDictionary(
                    g => g.Key,
                    g => g.SelectMany(r => r.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        /// <summary>
        /// Loads the admin-assigned category→department mapping (Admin → Office Management →
        /// Categories) — each LGU account's Categories list names the fine-grained HF categories
        /// it should receive. If two accounts somehow claim the same category, the last one read
        /// wins; the UI is expected to prevent that by hiding categories already assigned elsewhere.
        /// </summary>
        private async Task<Dictionary<string, string>> LoadCategoryDepartmentsAsync()
        {
            var rows = await _db.Users
                .Where(u => u.Department != null && u.Categories.Count > 0)
                .Select(u => new { u.Department, u.Categories })
                .ToListAsync();

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
                foreach (var category in row.Categories)
                    map[category] = row.Department!;

            return map;
        }

        private async Task<Dictionary<string, Dictionary<string, int>>> LoadLearnedWeightsAsync()
        {
            var rows = await _db.LearnedKeywords.AsNoTracking().ToListAsync();
            return rows
                .GroupBy(r => r.Department)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Word, r => r.Weight));
        }

        /// <summary>
        /// Logs an LGU verdict on a concern's classification and, when it was wrong,
        /// reassigns the concern and nudges keyword weights toward the correct department.
        /// A concern can only be reviewed once (enforced by a unique DB index); a second
        /// concurrent reviewer gets <see cref="ConcernAlreadyReviewedException"/> instead
        /// of a crash or a silently-lost verdict.
        /// </summary>
        public async Task RecordCorrectionAsync(int concernId, string correctedCategory, bool wasCorrect, string reviewedByUserId)
        {
            var executionStrategy = _db.Database.CreateExecutionStrategy();

            await executionStrategy.ExecuteAsync(async () =>
            {
                // A retry must start with a clean tracker so it does not reuse entities
                // left over from the failed database attempt.
                _db.ChangeTracker.Clear();

                var concern = await _db.Concerns.FindAsync(concernId)
                    ?? throw new InvalidOperationException($"Concern {concernId} not found.");

                // Most repeat requests can be rejected before attempting an INSERT. The
                // database unique index remains the final guard for truly concurrent requests.
                var alreadyReviewed = await _db.ClassificationCorrections
                    .AsNoTracking()
                    .AnyAsync(correction => correction.ConcernId == concernId);

                if (alreadyReviewed)
                {
                    throw new ConcernAlreadyReviewedException(
                        concernId,
                        new InvalidOperationException("A classification review already exists for this concern."));
                }

                var previousCategory = concern.Category;

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var correction = new ClassificationCorrection
                {
                    ConcernId = concernId,
                    PreviousCategory = previousCategory,
                    CorrectedCategory = correctedCategory,
                    WasCorrect = wasCorrect,
                    ReviewedByUserId = reviewedByUserId,
                    ReviewedAt = DateTime.UtcNow
                };
                _db.ClassificationCorrections.Add(correction);

                if (!wasCorrect)
                {
                    concern.Category = correctedCategory;
                    concern.UpdatedAt = DateTime.UtcNow;
                }

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    await transaction.RollbackAsync();

                    // SaveChanges leaves failed entities in the change tracker. Without
                    // detaching and reloading them, a later unrelated SaveChanges in the same
                    // request retries this duplicate INSERT and produces another 23505 error.
                    _db.Entry(correction).State = EntityState.Detached;
                    await _db.Entry(concern).ReloadAsync();

                    throw new ConcernAlreadyReviewedException(concernId, ex);
                }

                // Each upsert is a single atomic statement (INSERT ... ON CONFLICT), so two
                // reviewers touching the same word/department at once can't race each other.
                var words = ExtractKeywords(concern.Description);
                foreach (var word in words)
                {
                    await UpsertLearnedWeightAsync(word, correctedCategory, +1);
                    if (!wasCorrect && !string.IsNullOrEmpty(previousCategory) && previousCategory != correctedCategory)
                        await UpsertLearnedWeightAsync(word, previousCategory, -1);
                }

                await transaction.CommitAsync();
            });
        }

        private async Task UpsertLearnedWeightAsync(string word, string department, int delta)
        {
            var now = DateTime.UtcNow;
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "LearnedKeywords" ("Word", "Department", "Weight", "LastUpdatedAt")
                VALUES ({word}, {department}, {delta}, {now})
                ON CONFLICT ("Word", "Department")
                DO UPDATE SET "Weight" = "LearnedKeywords"."Weight" + {delta}, "LastUpdatedAt" = {now}
                """);
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: "23505" };

        private static List<string> ExtractKeywords(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return [];

            return description
                .ToLowerInvariant()
                .Split([' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 4 && !StopWords.Contains(w))
                .Distinct()
                .Take(15)
                .ToList();
        }

    }
}
