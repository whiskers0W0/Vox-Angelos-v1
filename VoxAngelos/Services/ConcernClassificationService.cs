using Google.Cloud.Language.V1;
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

    public class ConcernClassificationService
    {
        private readonly ApplicationDbContext _db;

        public ConcernClassificationService(ApplicationDbContext db)
        {
            _db = db;
        }

        // Departments this classifier can route to — used to validate corrections.
        public static readonly string[] Departments =
            ["SWDO", "CEO", "CENRO", "ACDO", "PPTRO", "OSCA", "PWDAO"];

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "and", "or", "but",
            "of", "to", "in", "on", "at", "for", "with", "this", "that", "it", "as", "by",
            "there", "here", "have", "has", "had", "not", "no", "yes", "please", "very",
            "ang", "ng", "sa", "at", "na", "po", "ay", "mga", "ito", "yan", "yung", "dahil",
            "kasi", "para", "may", "wala", "din", "rin", "kami", "kayo", "sila", "ako", "ikaw"
        };
        // Maps Google NLP category path segments → our 7 departments
        private static readonly (string Keyword, string Department)[] GoogleCategoryMap =
        [
            // SWDO
            ("Social",      "SWDO"),
            ("Welfare",     "SWDO"),
            ("Community",   "SWDO"),
            ("Poverty",     "SWDO"),
            ("Family",      "SWDO"),
            ("Charity",     "SWDO"),
            // Engineering Office
            ("Infrastructure",  "CEO"),
            ("Construction",    "CEO"),
            ("Road",            "CEO"),
            ("Building",        "CEO"),
            ("Civil",           "CEO"),
            ("Public Works",    "CEO"),
            // Environment
            ("CENRO",  "CENRO"),
            ("Waste",        "CENRO"),
            ("Nature",       "CENRO"),
            ("Pollution",    "CENRO"),
            ("Sanitation",   "CENRO"),
            ("Ecology",      "CENRO"),
            // ACDO
            ("Planning",     "ACDO"),
            ("Development",  "ACDO"),
            ("Urban",        "ACDO"),
            ("Economic",     "ACDO"),
            ("Land Use",     "ACDO"),
            // PPTRO
            ("Traffic",         "PPTRO"),
            ("Transportation",  "PPTRO"),
            ("Parking",         "PPTRO"),
            ("Vehicle",         "PPTRO"),
            ("Transit",         "PPTRO"),
            // OSCA
            ("Senior",      "OSCA"),
            ("Elderly",     "OSCA"),
            ("Aging",       "OSCA"),
            ("Retirement",  "OSCA"),
            // PWDAO
            ("Disability",      "PWDAO"),
            ("Accessibility",   "PWDAO"),
            ("Disabled",        "PWDAO"),
            ("Inclusion",       "PWDAO"),
        ];

        // Local keyword lists — fallback when Google NLP is unavailable
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

            ["PPTRO"] =
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

        // A department must clear this net learned-weight score, with no tie, before
        // it's trusted enough to skip Google/static classification entirely.
        private const int MinConfidentLearnedScore = 2;

        /// <summary>
        /// Primary: LGU-verified corrections win outright once they have a confident
        /// signal for this text. Otherwise tries Google NLP (≥ 20 words), then falls
        /// back to local keyword scoring blended with learned weights.
        /// </summary>
        public async Task<string?> ClassifyAsync(string description, string? credentialsPath = null)
        {
            if (string.IsNullOrWhiteSpace(description))
                return null;

            var learned = await LoadLearnedWeightsAsync();

            var confidentLearnedResult = ClassifyFromLearnedWeightsOnly(description, learned);
            if (confidentLearnedResult != null)
                return confidentLearnedResult;

            // Google NLP ClassifyText requires ≥ 20 tokens
            var wordCount = description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= 20)
            {
                var googleResult = await TryGoogleNlpAsync(description, credentialsPath);
                if (googleResult != null)
                    return googleResult;
            }

            return Classify(description, learned);
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
            var isConfident = ranked[0].Value >= MinConfidentLearnedScore
                && (ranked.Count == 1 || ranked[0].Value > ranked[1].Value);

            return isConfident ? ranked[0].Key : null;
        }

        /// <summary>
        /// Local keyword-only classifier — no network call, always available.
        /// Scores the static keyword lists plus any learned weights from LGU feedback.
        /// </summary>
        public string? Classify(string description, IReadOnlyDictionary<string, Dictionary<string, int>>? learnedWeights = null)
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

            return scores.Count == 0
                ? null
                : scores.MaxBy(s => s.Value).Key;
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
            var concern = await _db.Concerns.FindAsync(concernId)
                ?? throw new InvalidOperationException($"Concern {concernId} not found.");

            var previousCategory = concern.Category;

            await using var transaction = await _db.Database.BeginTransactionAsync();

            _db.ClassificationCorrections.Add(new ClassificationCorrection
            {
                ConcernId = concernId,
                PreviousCategory = previousCategory,
                CorrectedCategory = correctedCategory,
                WasCorrect = wasCorrect,
                ReviewedByUserId = reviewedByUserId,
                ReviewedAt = DateTime.UtcNow
            });

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

        // ── Private helpers ───────────────────────────────────────────────────

        private static async Task<string?> TryGoogleNlpAsync(string description, string? credentialsPath)
        {
            try
            {
                var builder = new LanguageServiceClientBuilder();

                if (!string.IsNullOrWhiteSpace(credentialsPath) && File.Exists(credentialsPath))
                    builder.CredentialsPath = credentialsPath;

                var client = await builder.BuildAsync();

                var doc = new Document
                {
                    Content = description,
                    Type = Document.Types.Type.PlainText
                };

                var response = await client.ClassifyTextAsync(doc);
                var best = response.Categories
                    .OrderByDescending(c => c.Confidence)
                    .FirstOrDefault();

                return best == null ? null : MapGoogleCategory(best.Name);
            }
            catch
            {
                return null; // silently fall back to local classifier
            }
        }

        private static string? MapGoogleCategory(string googleCategory)
        {
            foreach (var (keyword, department) in GoogleCategoryMap)
            {
                if (googleCategory.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return department;
            }
            return null;
        }
    }
}
