namespace VoxAngelos.Services
{
    public class ConcernClassificationService
    {
        // Each entry: department name → keywords that suggest that department
        private static readonly Dictionary<string, string[]> DepartmentKeywords =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["SWDO"] =
            [
                // English — Social Services, Public Welfare, Community Development,
                // Human Services, Social Protection
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

            ["Engineering Office"] =
            [
                // English — Infrastructure, Public Works, Civil Engineering,
                // Urban Infrastructure, Construction & Maintenance
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
                "tulay", "altu", "kalsada"
            ],

            ["Environment"] =
            [
                // English — Environment, Waste Management, Environmental Protection,
                // Sustainability, Sanitation, Natural Resources
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
                "polusyon", "polusyong",
                "puno", "punutin", "nagputol", "nagpaputol",
                "ilog", "lawa", "tubig", "maputik", "putik",
                "lamok", "ipis", "daga", "langaw",
                "baha", "pagbabaha", "binabaha",

                // Kapampangan
                "dumi", "masamit", "ambu", "maligpit", "kalikasan",
                "basura", "sukal", "balu"
            ],

            ["ACDO"] =
            [
                // English — Urban Planning, Economic Development, Land Use Planning,
                // Policy Planning, City Development, Strategic Planning
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
                "pamintuan", "kalungsoran", "planu", "lupa"
            ],

            ["Pptro"] =
            [
                // English — Traffic Management, Transportation, Road Safety,
                // Parking Management, Traffic Enforcement, Urban Mobility
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
                "sasakyan", "motorsiklu", "trapiku", "aksidente",
                "nakaparada", "drayber"
            ],

            ["OSCA"] =
            [
                // English — Elderly Care, Senior Citizen Services,
                // Public Welfare, Community Support
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
                "lolo", "lola", "ingkong", "impo",
                "card ng senior", "diskwento ng matanda",
                "benepisyo ng matanda", "pensyon",
                "retirado", "retirada",
                "pag-aalaga ng matanda", "pangit ang pakikitungo sa matanda",

                // Kapampangan
                "matatua", "lolo", "lola", "magtua",
                "senior", "matatanda"
            ],

            ["PWDAO"] =
            [
                // English — Disability Services, Accessibility,
                // Inclusion, Public Welfare, Community Support
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
                "gulong-gulong", "wheelchair", "ramp",
                "espesyal na pangangailangan", "nangangailangan",
                "id ng pwd", "card ng pwd", "diskwento ng pwd",
                "hadlang", "accessibility", "may sakit",
                "may depekto", "lumpo", "paralysado",

                // Kapampangan
                "kapansanan", "pwd", "bulag", "bungul",
                "di makatupad", "masakit", "espesyal"
            ],
        };

        /// <summary>
        /// Returns the best-matching department name for the given concern description,
        /// or null if no keywords match.
        /// </summary>
        public string? Classify(string description)
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

            return scores.Count == 0
                ? null
                : scores.MaxBy(s => s.Value).Key;
        }
    }
}
