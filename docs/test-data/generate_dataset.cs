using System.Globalization;
using System.Text;

var barangays = new[]
{
    "Balibago", "Malabanias", "Pulung Maragul", "Sto. Rosario", "Cutcut",
    "Anunas", "Lourdes Sur", "Pandan", "Capaya", "Amsic",
    "Marisol", "Sapalibutad", "Virgen Delos Remedios", "Salapungan", "Santo Domingo",
    "Ninoy Aquino", "Pampang", "Agapito Del Rosario", "Claro M. Recto", "Sto. Cristo"
};

var landmarks = new[]
{
    "malapit sa palengke", "sa tabi ng barangay hall", "malapit sa elementary school",
    "sa may covered court", "malapit sa simbahan", "sa tabi ng ilog",
    "sa harap ng basketball court", "malapit sa terminal ng tricycle",
    "sa may sari-sari store", "sa loob ng subdivision", "sa may waiting shed",
    "malapit sa health center"
};

int nextId = 1;
var rows = new List<(int Id, string Text, string Language, string Category, string Urgency, string Notes)>();

void Add(string dept, string lang, string urgency, string template, string noteEn, int count = 6)
{
    var rnd = new Random(unchecked(dept.GetHashCode() * 31 + lang.GetHashCode() * 7 + urgency.GetHashCode()));
    var usedBrgy = barangays.OrderBy(_ => rnd.Next()).Take(count).ToArray();
    var usedLandmark = landmarks.OrderBy(_ => rnd.Next()).Take(count).ToArray();

    for (int i = 0; i < count; i++)
    {
        var text = template
            .Replace("{brgy}", usedBrgy[i])
            .Replace("{landmark}", usedLandmark[i]);
        rows.Add((nextId++, text, lang, dept, urgency, noteEn));
    }
}

// ============================================================
// SWDO — Social Welfare and Development Office
// ============================================================
Add("SWDO", "English", "High",
    "Our family lost our house in the fire last night in Barangay {brgy}, we have small children and nowhere to sleep, we urgently need relief goods and temporary shelter.",
    "Fire victim family urgently needs relief goods and shelter.");
Add("SWDO", "English", "Medium",
    "I would like to request financial assistance for my mother's hospitalization, we are an indigent family from Barangay {brgy} and cannot afford the medical bills.",
    "Request for medical financial assistance for indigent family.");
Add("SWDO", "English", "Low",
    "Good day, I want to ask about the requirements and schedule for the 4Ps/Pantawid beneficiary application process in Barangay {brgy}.",
    "Inquiry about 4Ps/Pantawid application requirements.");
Add("SWDO", "Tagalog", "High",
    "Kagabi po nasunog ang bahay namin sa Barangay {brgy}, may mga bata po kami at wala kaming matulugan, kailangan po namin ng relief goods at tulong ngayon din.",
    "Fire victim family urgently needs relief goods and shelter.");
Add("SWDO", "Tagalog", "Medium",
    "Gusto ko po sanang humingi ng tulong pinansyal dahil naospital ang aking ina, mahirap lang po kami at wala kaming pambayad sa gastusin sa ospital, taga Barangay {brgy} po kami.",
    "Request for medical financial assistance for indigent family.");
Add("SWDO", "Tagalog", "Low",
    "Magandang araw po, gusto ko lang po itanong ang requirements at schedule para sa pag-apply bilang 4Ps beneficiary dito sa Barangay {brgy}.",
    "Inquiry about 4Ps/Pantawid application requirements.");
Add("SWDO", "Kapampangan", "High",
    "Mikasunug deng bale mi bengi king Barangay {brgy}, dakal kaming anak a ali makasulip, kailangan mi nong tulung at relief goods ngeni na.",
    "Fire victim family urgently needs relief goods and shelter.");
Add("SWDO", "Kapampangan", "Medium",
    "Buri ke pung manaya tulung pinansyal uling migkasakit ing indung ku, mangailangan kami pu keti Barangay {brgy} at ali ke misalese bayaran ing gastus king ospital.",
    "Request for medical financial assistance for indigent family.");
Add("SWDO", "Kapampangan", "Low",
    "Masanting a aldo pu, buri ke lang pung isaliksik deng requirements at schedule para king pag-apply bilang 4Ps beneficiary keti Barangay {brgy}.",
    "Inquiry about 4Ps/Pantawid application requirements.");
Add("SWDO", "Taglish", "High",
    "Sobrang urgent po, nasunog yung bahay namin last night sa Barangay {brgy}, need namin ng relief goods and shelter ASAP kasi may mga kids po kami na wala nang matulugan.",
    "Fire victim family urgently needs relief goods and shelter.");
Add("SWDO", "Taglish", "Medium",
    "Hi po, gusto ko sana mag-request ng financial assistance kasi na-hospitalize yung mommy ko, indigent family po kami sa Barangay {brgy} at di namin kaya yung medical bills.",
    "Request for medical financial assistance for indigent family.");
Add("SWDO", "Taglish", "Low",
    "Hello po, just want to ask about the requirements and schedule for 4Ps beneficiary application dito sa Barangay {brgy}, salamat po.",
    "Inquiry about 4Ps/Pantawid application requirements.");

// ============================================================
// CEO — City Engineer's Office
// ============================================================
Add("CEO", "English", "High",
    "The wooden bridge connecting Barangay {brgy} to the main road has partially collapsed after the heavy rain, it is very dangerous and someone could fall, please send engineers immediately.",
    "Partially collapsed bridge, immediate safety hazard.");
Add("CEO", "English", "Medium",
    "There are large potholes along the main road in Barangay {brgy} that have been there for almost a month, several motorcycles have already skidded because of them.",
    "Persistent large potholes causing motorcycle accidents.");
Add("CEO", "English", "Low",
    "Could you please look into repainting the faded road markings and repairing the broken streetlight near {landmark} in Barangay {brgy}?",
    "Faded road markings and broken streetlight, minor request.");
Add("CEO", "Tagalog", "High",
    "Halos gumuho na po yung tulay papuntang Barangay {brgy} matapos ang malakas na ulan, delikado na po ito at baka may mahulog, pakisuyo pong padalhan agad ng inhinyero.",
    "Partially collapsed bridge, immediate safety hazard.");
Add("CEO", "Tagalog", "Medium",
    "Marami pong malalaking lubak sa kalsada sa Barangay {brgy} na halos isang buwan na po dyan, nadulas na po ang ilang motorsiklo dahil dito.",
    "Persistent large potholes causing motorcycle accidents.");
Add("CEO", "Tagalog", "Low",
    "Pwede po bang pagpaayusin ang kupas na pintura sa kalsada at ang sirang ilaw sa kalye malapit sa {landmark} sa Barangay {brgy}?",
    "Faded road markings and broken streetlight, minor request.");
Add("CEO", "Kapampangan", "High",
    "Maglagub ne ing tete papunta king Barangay {brgy} matapus ing mabayung uran, delikadu ne ini at malyaring maragul ing masaktan, pakisuyu pong padalan agad kayung inhinyero.",
    "Partially collapsed bridge, immediate safety hazard.");
Add("CEO", "Kapampangan", "Medium",
    "Dakal deng maragul a bage king kalsada king Barangay {brgy} a masalese metung a bulan ne, mika-aksidente ne deng motorsiklu uling kareti.",
    "Persistent large potholes causing motorcycle accidents.");
Add("CEO", "Kapampangan", "Low",
    "Malyari wari pong ipaobra ing maluat a pintura at ing masirang ilaw king kalsada king {landmark} king Barangay {brgy}?",
    "Faded road markings and broken streetlight, minor request.");
Add("CEO", "Taglish", "High",
    "Emergency po, nag-collapse na yung part ng bridge papunta sa Barangay {brgy} after ng heavy rain, super delikado na po kasi baka may mahulog, please send engineers ASAP.",
    "Partially collapsed bridge, immediate safety hazard.");
Add("CEO", "Taglish", "Medium",
    "Grabe na po yung mga pothole sa main road ng Barangay {brgy}, halos isang buwan na po dyan, may mga motorcycle na na-accident na dahil dun.",
    "Persistent large potholes causing motorcycle accidents.");
Add("CEO", "Taglish", "Low",
    "Pwede po pa-check yung faded road markings and yung sirang streetlight malapit sa {landmark} sa Barangay {brgy}, minor lang po pero sana ma-fix rin.",
    "Faded road markings and broken streetlight, minor request.");

// ============================================================
// CENRO — City Environment and Natural Resources Office
// ============================================================
Add("CENRO", "English", "High",
    "There is a large illegal dumping site {landmark} in Barangay {brgy} that has been burning for two days now, the thick smoke is causing breathing problems for residents nearby.",
    "Burning illegal dump causing smoke/health hazard.");
Add("CENRO", "English", "Medium",
    "Garbage has not been collected in our area in Barangay {brgy} for almost two weeks, the trash is piling up and attracting rats and mosquitoes.",
    "Garbage collection delay attracting pests.");
Add("CENRO", "English", "Low",
    "We would like to suggest additional recycling bins {landmark} in Barangay {brgy} to help encourage proper waste segregation.",
    "Suggestion for additional recycling bins.");
Add("CENRO", "Tagalog", "High",
    "May malaking iligal na dumpsite po {landmark} sa Barangay {brgy} na dalawang araw nang nagliliyab, nakakahirap huminga ang mga residente dahil sa usok.",
    "Burning illegal dump causing smoke/health hazard.");
Add("CENRO", "Tagalog", "Medium",
    "Halos dalawang linggo na pong hindi kinokolekta ang basura sa amin sa Barangay {brgy}, tumatambak na ito at umaakit ng daga at lamok.",
    "Garbage collection delay attracting pests.");
Add("CENRO", "Tagalog", "Low",
    "Gusto po naming imungkahi ang pagdagdag ng recycling bins {landmark} sa Barangay {brgy} para masuportahan ang tamang pag-segregate ng basura.",
    "Suggestion for additional recycling bins.");
Add("CENRO", "Kapampangan", "High",
    "Atin yung maragul a iligal a dumpsite {landmark} king Barangay {brgy} a adwang aldo ne makasulu, masakit lang huminga deng tau uling king aslam a asu.",
    "Burning illegal dump causing smoke/health hazard.");
Add("CENRO", "Kapampangan", "Medium",
    "Masalese adwang lingu ne ali dacal ing basura keti Barangay {brgy}, mika-abuk ne at manalili ne deng dagis at lamuk.",
    "Garbage collection delay attracting pests.");
Add("CENRO", "Kapampangan", "Low",
    "Buri mi pung imungkahi ing pamagdagdag da reng recycling bin {landmark} king Barangay {brgy} bang matulungan ing tamang pamag-segregate king basura.",
    "Suggestion for additional recycling bins.");
Add("CENRO", "Taglish", "High",
    "May nag-eeng illegal dumpsite po {landmark} sa Barangay {brgy}, two days na po nagbo-burn non-stop, nahihirapan na po mag-breathe yung mga residents dahil sa usok.",
    "Burning illegal dump causing smoke/health hazard.");
Add("CENRO", "Taglish", "Medium",
    "Almost two weeks na po hindi na-collect yung garbage namin sa Barangay {brgy}, nagtatambak na siya and nakaka-attract na ng daga at lamok.",
    "Garbage collection delay attracting pests.");
Add("CENRO", "Taglish", "Low",
    "Gusto lang po namin i-suggest na magdagdag ng recycling bins {landmark} sa Barangay {brgy} para ma-encourage yung proper waste segregation.",
    "Suggestion for additional recycling bins.");

// ============================================================
// ACDO — City Development / Urban Planning Office
// ============================================================
Add("ACDO", "English", "High",
    "A developer is illegally constructing a commercial building {landmark} in Barangay {brgy} on land that is zoned residential, without any visible permit posted, please investigate urgently.",
    "Illegal construction on residential-zoned land, urgent.");
Add("ACDO", "English", "Medium",
    "My business permit application for a small sari-sari store in Barangay {brgy} has been pending for over a month with no update.",
    "Delayed business permit application.");
Add("ACDO", "English", "Low",
    "I would like to ask about the process for requesting a zoning certificate for a lot I own in Barangay {brgy}.",
    "Inquiry about zoning certificate process.");
Add("ACDO", "Tagalog", "High",
    "May nagtatayo po ng commercial building {landmark} sa Barangay {brgy} sa lupa na residential ang zoning, wala pong nakalagay na permit, pakisuyo pong siyasatin agad.",
    "Illegal construction on residential-zoned land, urgent.");
Add("ACDO", "Tagalog", "Medium",
    "Mahigit isang buwan na pong naka-pending ang aplikasyon ko para sa business permit ng maliit na sari-sari store sa Barangay {brgy}, wala pa pong update.",
    "Delayed business permit application.");
Add("ACDO", "Tagalog", "Low",
    "Gusto ko po sanang itanong ang proseso para sa paghingi ng zoning certificate para sa lote na akin sa Barangay {brgy}.",
    "Inquiry about zoning certificate process.");
Add("ACDO", "Kapampangan", "High",
    "Atin metung a nagtatayu commercial building {landmark} king Barangay {brgy} king lupa a residential ing zoning, alang permit a mit lagyan, pakisuyu pong siasatan agad.",
    "Illegal construction on residential-zoned land, urgent.");
Add("ACDO", "Kapampangan", "Medium",
    "Masalese metung a bulan ne naka-pending ing aplikasyon ku para king business permit ning malating sari-sari store keti Barangay {brgy}, alang pa update.",
    "Delayed business permit application.");
Add("ACDO", "Kapampangan", "Low",
    "Buri ke pu sanang isaliksik ing proseso para king pamanaya na king zoning certificate para king lupa ku keti Barangay {brgy}.",
    "Inquiry about zoning certificate process.");
Add("ACDO", "Taglish", "High",
    "May nagtatayo po ng commercial building {landmark} sa Barangay {brgy} kahit residential zone yun, wala pong visible permit, please investigate agad.",
    "Illegal construction on residential-zoned land, urgent.");
Add("ACDO", "Taglish", "Medium",
    "Over a month na po pending yung business permit application ko for a small sari-sari store sa Barangay {brgy}, wala pa pong update.",
    "Delayed business permit application.");
Add("ACDO", "Taglish", "Low",
    "Gusto ko lang po i-ask yung process for requesting zoning certificate for my lot sa Barangay {brgy}.",
    "Inquiry about zoning certificate process.");

// ============================================================
// PPTRO — Public Safety, Traffic and Transport Regulation Office
// ============================================================
Add("PPTRO", "English", "High",
    "There was a serious vehicular accident {landmark} in Barangay {brgy} because the traffic light has been broken for days, several vehicles collided at the intersection.",
    "Accident caused by broken traffic light, urgent.");
Add("PPTRO", "English", "Medium",
    "Tricycles are illegally parked all along the road {landmark} in Barangay {brgy}, causing heavy traffic congestion every afternoon.",
    "Illegal tricycle parking causing congestion.");
Add("PPTRO", "English", "Low",
    "Could a traffic enforcer be assigned during rush hour {landmark} in Barangay {brgy} to help manage the pedestrian crossing?",
    "Request for traffic enforcer at pedestrian crossing.");
Add("PPTRO", "Tagalog", "High",
    "Nagkaroon po ng malubhang aksidente {landmark} sa Barangay {brgy} dahil ilang araw nang sira ang traffic light, nagbanggaan ang ilang sasakyan sa interseksyon.",
    "Accident caused by broken traffic light, urgent.");
Add("PPTRO", "Tagalog", "Medium",
    "Iligal na nakaparada ang mga tricycle sa tabi ng kalsada {landmark} sa Barangay {brgy}, nagdudulot ito ng matinding trapiko tuwing hapon.",
    "Illegal tricycle parking causing congestion.");
Add("PPTRO", "Tagalog", "Low",
    "Pwede po bang magtalaga ng traffic enforcer tuwing rush hour {landmark} sa Barangay {brgy} para matulungan ang mga tumatawid?",
    "Request for traffic enforcer at pedestrian crossing.");
Add("PPTRO", "Kapampangan", "High",
    "Mika-aksidenteng maragul {landmark} king Barangay {brgy} uling pilan alang aldo neng masira ing traffic light, mika-bunggan deng dakal a sasakyan king interseksyon.",
    "Accident caused by broken traffic light, urgent.");
Add("PPTRO", "Kapampangan", "Medium",
    "Iligal a mikaparada deng tricycle king gilid ning kalsada {landmark} king Barangay {brgy}, mika-trapiku maragul kada gatpanapon.",
    "Illegal tricycle parking causing congestion.");
Add("PPTRO", "Kapampangan", "Low",
    "Malyari wari pong magtalaga metung a traffic enforcer kada rush hour {landmark} king Barangay {brgy} bang matulungan deng tau a manabuk?",
    "Request for traffic enforcer at pedestrian crossing.");
Add("PPTRO", "Taglish", "High",
    "May nangyaring serious accident po {landmark} sa Barangay {brgy} kasi ilang araw na pong sira yung traffic light, nagbanggaan yung several vehicles sa intersection.",
    "Accident caused by broken traffic light, urgent.");
Add("PPTRO", "Taglish", "Medium",
    "Illegally parked po yung mga tricycle sa tabi ng road {landmark} sa Barangay {brgy}, nagiging cause ito ng heavy traffic every afternoon.",
    "Illegal tricycle parking causing congestion.");
Add("PPTRO", "Taglish", "Low",
    "Pwede po ba mag-assign ng traffic enforcer during rush hour {landmark} sa Barangay {brgy} to help manage yung pedestrian crossing?",
    "Request for traffic enforcer at pedestrian crossing.");

// ============================================================
// OSCA — Office of Senior Citizens Affairs
// ============================================================
Add("OSCA", "English", "High",
    "My 82-year-old grandfather in Barangay {brgy} has been abandoned by his caregiver and has not eaten properly in days, he urgently needs assistance.",
    "Abandoned elderly senior needing urgent assistance.");
Add("OSCA", "English", "Medium",
    "Several senior citizens in Barangay {brgy} have not received their monthly pension for two months now, please look into the delay.",
    "Delayed senior citizen pension payments.");
Add("OSCA", "English", "Low",
    "My mother would like to ask about the renewal process for her senior citizen ID in Barangay {brgy}.",
    "Inquiry about senior citizen ID renewal.");
Add("OSCA", "Tagalog", "High",
    "Naiwan po ng caregiver niya ang 82 anyos na lolo ko sa Barangay {brgy} at ilang araw na po siyang hindi nakakakain nang maayos, kailangan niya po ng tulong agad.",
    "Abandoned elderly senior needing urgent assistance.");
Add("OSCA", "Tagalog", "Medium",
    "Dalawang buwan na pong hindi nakakatanggap ng buwanang pensyon ang ilang senior citizen sa Barangay {brgy}, pakisuyo pong tignan ang dahilan ng pagkaantala.",
    "Delayed senior citizen pension payments.");
Add("OSCA", "Tagalog", "Low",
    "Gusto po sanang itanong ng nanay ko ang proseso ng pag-renew ng kanyang senior citizen ID dito sa Barangay {brgy}.",
    "Inquiry about senior citizen ID renewal.");
Add("OSCA", "Kapampangan", "High",
    "Mikatuknangan ne pu ning caregiver ne ing 82 banua nang ingkong ku king Barangay {brgy} at pilan alang aldo neng ali makakan a mayap, kailangan ne pung tulung ngeni.",
    "Abandoned elderly senior needing urgent assistance.");
Add("OSCA", "Kapampangan", "Medium",
    "Adwang bulan neng ali makatanggap da reng senior citizen king Barangay {brgy} king bulanan a pensyon da, pakisuyu pong lawe ing dahilan ning pamagkaantala.",
    "Delayed senior citizen pension payments.");
Add("OSCA", "Kapampangan", "Low",
    "Buri neng isaliksik ning indu ku ing proseso king pamag-renew king senior citizen ID na keti Barangay {brgy}.",
    "Inquiry about senior citizen ID renewal.");
Add("OSCA", "Taglish", "High",
    "Na-abandon po siya ng caregiver niya, yung 82-year-old lolo ko sa Barangay {brgy}, ilang days na po siyang hindi nakaka-eat properly, need niya po ng urgent help.",
    "Abandoned elderly senior needing urgent assistance.");
Add("OSCA", "Taglish", "Medium",
    "Two months na po hindi nakaka-receive ng monthly pension yung ilang senior citizens sa Barangay {brgy}, please po i-check yung delay.",
    "Delayed senior citizen pension payments.");
Add("OSCA", "Taglish", "Low",
    "Gusto lang po itanong ng mommy ko yung renewal process for her senior citizen ID dito sa Barangay {brgy}.",
    "Inquiry about senior citizen ID renewal.");

// ============================================================
// PWDAO — Persons With Disability Affairs Office
// ============================================================
Add("PWDAO", "English", "High",
    "The public health center {landmark} in Barangay {brgy} has no wheelchair ramp at all, my brother who uses a wheelchair could not get inside for his urgent checkup.",
    "No wheelchair ramp blocking urgent medical access.");
Add("PWDAO", "English", "Medium",
    "My PWD ID application in Barangay {brgy} has been pending for more than a month even though I already submitted all the requirements.",
    "Delayed PWD ID application.");
Add("PWDAO", "English", "Low",
    "We would like to suggest installing a ramp and handrail {landmark} in Barangay {brgy} for the benefit of elderly and disabled residents.",
    "Suggestion for accessibility ramp/handrail.");
Add("PWDAO", "Tagalog", "High",
    "Walang wheelchair ramp ang health center {landmark} sa Barangay {brgy}, hindi po nakapasok ang kapatid ko na naka-wheelchair para sa kanyang urgent na checkup.",
    "No wheelchair ramp blocking urgent medical access.");
Add("PWDAO", "Tagalog", "Medium",
    "Mahigit isang buwan na pong naka-pending ang aplikasyon ko para sa PWD ID sa Barangay {brgy} kahit naisumite ko na po ang lahat ng requirements.",
    "Delayed PWD ID application.");
Add("PWDAO", "Tagalog", "Low",
    "Gusto po naming imungkahi ang paglalagay ng ramp at hawakan {landmark} sa Barangay {brgy} para sa kapakinabangan ng matatanda at may kapansanan.",
    "Suggestion for accessibility ramp/handrail.");
Add("PWDAO", "Kapampangan", "High",
    "Alang wheelchair ramp ing health center {landmark} king Barangay {brgy}, ali ne makalub ing kapatad ku a maki-wheelchair para king kanyang urgent a checkup.",
    "No wheelchair ramp blocking urgent medical access.");
Add("PWDAO", "Kapampangan", "Medium",
    "Masalese metung a bulan ne naka-pending ing aplikasyon ku para king PWD ID keti Barangay {brgy} agyaman mi ne yang mibye deng requirements.",
    "Delayed PWD ID application.");
Add("PWDAO", "Kapampangan", "Low",
    "Buri mi pung imungkahi ing pamaglagyan ramp at hawakan {landmark} king Barangay {brgy} para king pakinabang da reng matua at may kapansanan.",
    "Suggestion for accessibility ramp/handrail.");
Add("PWDAO", "Taglish", "High",
    "Wala pong wheelchair ramp yung health center {landmark} sa Barangay {brgy}, hindi po nakapasok yung brother ko na naka-wheelchair for his urgent checkup.",
    "No wheelchair ramp blocking urgent medical access.");
Add("PWDAO", "Taglish", "Medium",
    "Over a month na po pending yung PWD ID application ko sa Barangay {brgy} kahit na-submit ko na po lahat ng requirements.",
    "Delayed PWD ID application.");
Add("PWDAO", "Taglish", "Low",
    "Gusto lang po namin i-suggest na maglagay ng ramp and handrail {landmark} sa Barangay {brgy} para sa benefit ng elderly and disabled residents.",
    "Suggestion for accessibility ramp/handrail.");

// Trim/pad to exactly 500 rows, then re-number sequentially.
rows = rows.Take(500).ToList();
for (int i = 0; i < rows.Count; i++)
    rows[i] = (i + 1, rows[i].Text, rows[i].Language, rows[i].Category, rows[i].Urgency, rows[i].Notes);

string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

var sb = new StringBuilder();
sb.AppendLine("id,text,language,expected_category,expected_urgency,lgu_verified,notes");
foreach (var r in rows)
{
    sb.AppendLine(string.Join(",",
        r.Id.ToString(CultureInfo.InvariantCulture),
        Csv(r.Text),
        Csv(r.Language),
        Csv(r.Category),
        Csv(r.Urgency),
        "", // lgu_verified — left blank for LGU review
        Csv(r.Notes)));
}

File.WriteAllText("concern_test_cases_500.csv", sb.ToString());
Console.WriteLine($"Wrote {rows.Count} rows.");

// Quick language/department distribution summary for sanity-checking.
foreach (var g in rows.GroupBy(r => r.Category))
    Console.WriteLine($"{g.Key}: {g.Count()}");
