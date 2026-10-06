using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class LandmarkBookConfig
{
    public string id = "";
    // 什么时候写出来(二选一)：研究出某项技术 / 推行了带某特性的制度(特性值 ≥ min_value)
    public string tech = "";
    public string feature = "";
    public float min_value = 1f;
    // 理念名著依靠科技和社会条件产生；普通名著仍使用 tech/feature 触发。
    public List<string> required_techs = new();
    // prosperous / industrial / industrial_unrest / militarized / mass_politics
    public string environment = "";
    public float annual_chance = 1f;
    public string ideology = "";
    public float ideology_outbreak;
    // 这本书推动的技术：技术 id → 立刻增加的进度(占费用的比例)
    public Dictionary<string, float> techs = new();
    // 这本书推动的制度/理论：制度特性 → 改革永久加速(0.3 = +30%)；以 * 结尾表示前缀匹配(如 ideology_stage:Communism:*)
    public Dictionary<string, float> institutions = new();
    // 书出现时，本文化正在推行的相关改革立刻增加的进度(0~100)
    public float institution_progress = 10f;
    // 文化藏书窗口里的分类：politics / economy / military / science / history / literature
    public string category = "";
}

public sealed class LandmarkBookFileConfig
{
    public List<LandmarkBookConfig> books = new();
}

// 时代名著：技术和制度推进到某一步时，本文化最有学识的人写出这个时代的代表作
// (几何原本、天工开物、国富论、社会契约论……)。书名按文化给，以人名命名的名著换成不带人名的书名。
//
// 写出来的是真正的原版书，放进城里能藏书的建筑，可以被阅读；书上挂着扩展(BookExtension)，记得自己是哪部名著。
// 名著出现在哪个文化，就推动那个文化书上绑定的技术和制度：
//   · 写成时：本文化立刻生效，并按 landmark_book_multiplier 算几本书的著书科技点；
//   · 被别的文化读到时(比如城池易手后，图书馆里的书被新主人读到)：那个文化也受到启发，技术进度按一半计。
// 每个文化每本书只受一次启发。城里还没有能放书的建筑时先不写，等有了再写。
public static class LandmarkBookSystem
{
    private const float ForeignTechFactor = 0.5f;
    private static List<LandmarkBookConfig> _books;

    private static List<LandmarkBookConfig> Books
    {
        get
        {
            if (_books != null) return _books;
            _books = new List<LandmarkBookConfig>();
            try
            {
                string path = global::System.IO.Path.Combine(ModClass._declare.FolderPath,
                    TechnologySystem.FolderName, "LandmarkBooks.json");
                if (global::System.IO.File.Exists(path))
                    _books = JsonConvert.DeserializeObject<LandmarkBookFileConfig>(
                        global::System.IO.File.ReadAllText(path))?.books ?? _books;
            }
            catch (Exception exception)
            {
                LogService.LogError($"[EmpireCraft] 时代名著配置读取失败: {exception}");
            }
            _books.RemoveAll(book => book == null || string.IsNullOrWhiteSpace(book.id));
            foreach (LandmarkBookConfig book in _books)
            {
                book.techs ??= new Dictionary<string, float>();
                book.institutions ??= new Dictionary<string, float>();
                book.required_techs ??= new List<string>();
                book.annual_chance = Math.Max(0f, Math.Min(1f, book.annual_chance));
                book.ideology_outbreak = Math.Max(0f, Math.Min(1f, book.ideology_outbreak));
            }
            return _books;
        }
    }

    public static IEnumerable<LandmarkBookConfig> All => Books;

    public static bool TryGetBook(string bookId, out LandmarkBookConfig book)
    {
        book = Books.FirstOrDefault(candidate => candidate.id == bookId);
        return book != null;
    }

    public static string GetTitle(string bookId, string culture)
    {
        string key = $"landmark_book_{bookId}_{culture}";
        string title = LM.Get(key);
        if (!string.IsNullOrWhiteSpace(title) && title != key) return title;
        key = $"landmark_book_{bookId}";
        title = LM.Get(key);
        return string.IsNullOrWhiteSpace(title) || title == key ? bookId : title;
    }

    // 哪些名著推动这项技术(科技窗口显示用)
    public static IEnumerable<(LandmarkBookConfig book, float fraction)> BooksForTech(string techId) =>
        Books.Where(book => book.techs.ContainsKey(techId)).Select(book => (book, book.techs[techId]));

    private static bool IsTriggered(string culture, LandmarkBookConfig book)
    {
        bool ideological = TryGetIdeology(book, out PartyIdeology ideology);
        // 理念名著先产生思想，再推动制度。ideology:* 是书的主题，不能要求先有对应制度。
        bool ideaFeature = ideological && IdeologySpreadSystem.TryParseFeature(book.feature, out PartyIdeology featureIdeology) &&
                           featureIdeology == ideology;
        bool primary = !string.IsNullOrEmpty(book.tech)
            ? TechnologySystem.HasTech(culture, book.tech)
            : ideaFeature || !string.IsNullOrEmpty(book.feature) &&
              InstitutionSystem.GetFeature(culture, book.feature) >= book.min_value;
        if (!primary || book.required_techs.Any(tech => !TechnologySystem.HasTech(culture, tech))) return false;
        return !ideological || TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology));
    }

    #region 写书

    // 读档后第一次见到这个文化：已经达到的阶段直接记为写过、读过，不补写(否则旧存档会一下冒出一堆书)
    public static void MarkExisting(string culture, CultureTechState state)
    {
        state.landmark_books ??= new List<string>();
        state.known_books ??= new List<string>();
        foreach (LandmarkBookConfig book in Books)
        {
            // Ideology books are gameplay events and remain eligible in old saves.
            if (TryGetIdeology(book, out _)) continue;
            if (!IsTriggered(culture, book)) continue;
            if (!state.landmark_books.Contains(book.id)) state.landmark_books.Add(book.id);
            if (!state.known_books.Contains(book.id)) state.known_books.Add(book.id);
        }
    }

    public static void YearlyCheck(string culture, List<City> cities, CultureTechState state)
    {
        state.landmark_books ??= new List<string>();
        state.known_books ??= new List<string>();
        state.ideology_book_outbreaks ??= new List<string>();
        foreach (LandmarkBookConfig book in Books)
        {
            if (!IsTriggered(culture, book)) continue;
            if (state.known_books.Contains(book.id))
                TryApplyIdeologyEffect(culture, cities, state, book, 1f, GetTitle(book.id, culture));
            if (state.landmark_books.Contains(book.id)) continue;
            if (TryWrite(culture, cities, book)) state.landmark_books.Add(book.id);
        }
        WriteScholarlyBooks(cities);
    }

    #region 士人著述

    // 原版只有国王/城主/族人走"写书"剧情才会写书(要 10 级、200 金、进度 200，还要和一大堆剧情抢)，
    // 本模组剧情一多几乎写不出书，著作目录里只剩名著。这里补上日常著述：
    // 每年每座有空书位的城，按城里读书人(举人、贡士、官僚、市民)的多少有一定概率由最有学识的人写一本普通书(书种由原版决定)。
    // 每个文化每年最多写 城市数/4 本，免得刷屏。
    private const float ScholarBaseChance = 0.04f;
    private const float ScholarChancePerLiterate = 0.03f;
    private const int ScholarLiterateCap = 5;
    private const float ScholarMaxChance = 0.3f;

    private static void WriteScholarlyBooks(List<City> cities)
    {
        if (cities == null || World.world?.books == null) return;
        using var timing = new PerfTimer("士人著述");
        int budget = Math.Max(1, cities.Count / 4);
        foreach (City city in cities.OrderBy(_ => UnityEngine.Random.value))
        {
            if (budget <= 0) return;
            if (city == null || city.isRekt() || city.units == null || !city.hasBookSlots()) continue;
            // 先掷骰：概率最高 30%，掷不中就不必统计全城人口(概率分布与先算后掷相同)
            float roll = UnityEngine.Random.value;
            if (roll >= ScholarMaxChance) continue;
            List<Actor> adults = city.units.Where(actor => actor != null && actor.isAlive() && actor.isAdult() &&
                                                           actor.city == city && !actor.IsWarMachine()).ToList();
            // 无小人模式：城里的人口与识字人口按户计(实体单位只剩名人)
            bool abstracted = CityPopulationSystem.AbstractPopulationEnabled;
            int population = abstracted ? CityPopulationSystem.Households(city) : adults.Count;
            if (population < 15) continue;
            int literate = Math.Min(ScholarLiterateCap, adults.Count(IsLiterate) +
                                                        (abstracted ? CityPopulationSystem.LiterateHouseholds(city) : 0));
            float chance = Math.Min(ScholarMaxChance, ScholarBaseChance + ScholarChancePerLiterate * literate);
            if (roll >= chance) continue;
            Actor author = adults.Where(actor => actor.language != null && actor.culture != null)
                .OrderByDescending(actor => actor.stats["intelligence"]).FirstOrDefault();
            // 没有实体执笔人：从人口里请一位读书人
            if (author == null && abstracted) author = CityPopulationSystem.SpawnScholar(city);
            if (author == null) continue;
            try
            {
                if (World.world.books.generateNewBook(author) != null) budget--;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 士人著述失败: {exception.Message}");
            }
        }
    }

    private static bool IsLiterate(Actor actor) =>
        actor.hasTrait("juren") || actor.hasTrait("gongshi") ||
        actor.GetOrCreate().socialClass is SocialClass.Officer or SocialClass.Citizen;

    #endregion

    private static bool TryWrite(string culture, List<City> cities, LandmarkBookConfig config)
    {
        List<City> eligibleCities = cities?.Where(city => MeetsEnvironment(city, config)).ToList() ?? new List<City>();
        if (eligibleCities.Count == 0 || UnityEngine.Random.value > config.annual_chance) return false;
        Actor author = PickAuthor(eligibleCities);
        if (author == null) return false;
        Book book;
        try
        {
            book = World.world.books.generateNewBook(author);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 时代名著写作失败: {exception.Message}");
            return false;
        }
        if (book == null) return false;
        string title = GetTitle(config.id, culture);
        book.setName(title);
        BookNamingSystem.MarkUsed(title);
        BookExtension.BookExtraData data = book.GetOrCreate();
        data.id = book.getID();
        data.landmark_id = config.id;
        data.origin_culture = culture;
        data.inspired_cultures.Add(culture);

        if (TryGetIdeology(config, out PartyIdeology ideology)) IdeologyPopulationSystem.Set(author, ideology);

        TechnologySystem.AddLandmarkBookPoints(culture);
        Inspire(culture, config, 1f, title);
        string text = BuildAnnouncement(culture, author, title);
        Announce(culture, author.kingdom, text, author.getID());
        // 关键名著问世：屏幕上方提示
        WorldTip.showNow(text, false, "top", 5f);
        return true;
    }

    // 问世提示从 Books/LandmarkBookLogs.csv(或文化专属的 <文化>LandmarkBookLogs.csv)里随机抽一句：
    // {0} 文化  {1} 作者  {2} 书名  {3} 作者所在城市(作者没有城市时只抽不带 {3} 的句子)
    private static string BuildAnnouncement(string culture, Actor author, string title)
    {
        string cityName = author.city?.name;
        List<string> pool = BookNamingSystem.Pool(culture, "LandmarkBookLogs")
            .Where(line => !string.IsNullOrEmpty(cityName) || !line.Contains("{3}")).ToList();
        string pattern = pool.Count > 0 ? pool.GetRandom()
            : string.IsNullOrEmpty(cityName) ? LM.Get("landmark_book_log_plain") : LM.Get("landmark_book_log");
        try
        {
            return string.Format(pattern, culture.GetCultureTranslate(), author.getName(), title, cityName ?? "");
        }
        catch (FormatException)
        {
            return string.Format(LM.Get("landmark_book_log_plain"), culture.GetCultureTranslate(), author.getName(), title);
        }
    }

    // 本文化里最有学识的成年人，所在城市要有能放书的建筑
    private static Actor PickAuthor(List<City> cities)
    {
        Actor best = null;
        float bestScore = float.MinValue;
        foreach (City city in cities)
        {
            if (city == null || city.isRekt() || city.getBuildingWithBookSlot() == null) continue;
            foreach (Actor actor in city.units)
            {
                if (actor == null || !actor.isAlive() || !actor.isAdult() || actor.language == null ||
                    actor.IsWarMachine()) continue;
                float score = actor.stats["intelligence"] + actor.renown * 0.01f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = actor;
            }
        }
        // 无小人模式：没有实体执笔人时，从人口最多的那座有藏书处的城里请一位读书人
        if (best == null && CityPopulationSystem.AbstractPopulationEnabled)
        {
            City city = cities.Where(candidate => candidate != null && !candidate.isRekt() &&
                                                  candidate.getBuildingWithBookSlot() != null)
                .OrderByDescending(CityPopulationSystem.Households).FirstOrDefault();
            best = CityPopulationSystem.SpawnScholar(city);
        }
        return best;
    }

    #endregion

    #region 启发

    // 名著被读到：读者所在的文化如果还没受过这本书的启发，就受一次(外来的技术进度减半)
    public static void OnRead(Book book, string readerCulture)
    {
        if (!CultureService.IsValidCulture(readerCulture) || !book.TryGetLandmark(out BookExtension.BookExtraData data))
            return;
        if (data.inspired_cultures.Contains(readerCulture)) return;
        data.inspired_cultures.Add(readerCulture);
        if (!TryGetBook(data.landmark_id, out LandmarkBookConfig config)) return;
        CultureTechState state = TechnologySystem.GetState(readerCulture);
        state.known_books ??= new List<string>();
        if (state.known_books.Contains(config.id)) return; // 本文化已经有这部书的思想了
        Inspire(readerCulture, config, ForeignTechFactor, book.name);
        Announce(readerCulture, null, string.Format(LM.Get("landmark_book_spread_log"),
            book.name, readerCulture.GetCultureTranslate()), -1L);
    }

    private static void Inspire(string culture, LandmarkBookConfig config, float techFactor, string title)
    {
        CultureTechState state = TechnologySystem.GetState(culture);
        state.known_books ??= new List<string>();
        if (!state.known_books.Contains(config.id)) state.known_books.Add(config.id);
        foreach (KeyValuePair<string, float> pair in config.techs)
            TechnologySystem.AddTechProgressFraction(culture, pair.Key, pair.Value * techFactor);
        PushActiveReforms(culture, config);
        List<City> cities = World.world?.cities?.Where(city => city != null && !city.isRekt() &&
            CultureService.GetMainCulture(city) == culture).ToList() ?? new List<City>();
        TryApplyIdeologyEffect(culture, cities, state, config, techFactor, title);
    }

    // 理念的奠基名著(资本论之于共产主义、国富论之于资本主义……)：LandmarkBooks.json 里标了该理念的名著
    public static LandmarkBookConfig FoundingBook(PartyIdeology ideology) =>
        Books.FirstOrDefault(book => TryGetIdeology(book, out PartyIdeology own) && own == ideology);

    // 本文化是否已有这个理念的奠基名著(自己写出，或读到外来的而受启发)。没配奠基名著的理念不受限制
    public static bool HasFoundingBook(string culture, PartyIdeology ideology)
    {
        LandmarkBookConfig founding = FoundingBook(ideology);
        if (founding == null) return true;
        if (!CultureService.IsValidCulture(culture)) return false;
        CultureTechState state = TechnologySystem.GetState(culture);
        return state?.known_books?.Contains(founding.id) == true || state?.landmark_books?.Contains(founding.id) == true;
    }

    private static bool TryGetIdeology(LandmarkBookConfig config, out PartyIdeology ideology) =>
        Enum.TryParse(config?.ideology, true, out ideology) && Enum.IsDefined(typeof(PartyIdeology), ideology);

    private static bool MeetsEnvironment(City city, LandmarkBookConfig config)
    {
        if (city == null || city.isRekt() || city.kingdom == null || city.getBuildingWithBookSlot() == null)
            return false;
        string environment = config.environment?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(environment)) return true;
        List<Actor> adults = city.units?.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
            actor.isAdult() && actor.city == city).ToList() ?? new List<Actor>();
        if (adults.Count == 0) return false;
        UrbanEmploymentReport employment = UrbanEmploymentSystem.GetReport(city);
        bool prosperous = adults.Count >= 30 && city.kingdom.GetMoney() >= 0 &&
            (UrbanCitizenSystem.GetCapacity(city, adults.Count) > 0 ||
             adults.Count(actor => actor.GetOrCreate().is_economic_merchant) >= Math.Max(1, adults.Count / 20));
        bool industrial = employment.Stage >= 1 && (employment.Factories > 0 ||
            employment.Workers >= Math.Max(2, (int)Math.Ceiling(adults.Count * 0.08f)));
        float grievance = 0f;
        Empire empire = city.kingdom.GetEmpire();
        if (empire != null)
        {
            IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
            if (grievances.TryGetValue(SocialClass.Labour, out float labour)) grievance = Math.Max(grievance, labour);
            if (grievances.TryGetValue(SocialClass.Peasant, out float peasant)) grievance = Math.Max(grievance, peasant);
        }
        bool unrest = industrial && (grievance >= 25f ||
            LandEconomySystem.GetReport(city).LandlessPopulationRatio >= LandEconomySystem.RebellionLandlessThreshold);
        bool militarized = city.kingdom.hasEnemies() ||
            adults.Count(actor => actor.isWarrior()) >= Math.Max(3, (int)Math.Ceiling(adults.Count * 0.12f));
        bool massPolitics = adults.Count >= 40 && InstitutionSystem.GetFeature(
            CultureService.GetMainCulture(city), PartySystem.FeaturePartyPolitics) > 0f;
        return environment switch
        {
            "prosperous" => prosperous,
            "industrial" => industrial,
            "industrial_unrest" => unrest,
            "militarized" => militarized,
            "mass_politics" => massPolitics,
            _ => true
        };
    }

    private static void TryApplyIdeologyEffect(string culture, List<City> cities, CultureTechState state,
        LandmarkBookConfig config, float factor, string title)
    {
        if (!TryGetIdeology(config, out PartyIdeology ideology) || config.ideology_outbreak <= 0f) return;
        state.ideology_book_outbreaks ??= new List<string>();
        if (state.ideology_book_outbreaks.Contains(config.id) || !IsTriggered(culture, config) ||
            cities == null || !cities.Any(city => MeetsEnvironment(city, config))) return;
        int changed = IdeologyPopulationSystem.IntroduceToCulture(culture, ideology,
            config.ideology_outbreak * factor);
        if (changed <= 0) return;
        state.ideology_book_outbreaks.Add(config.id);
        Announce(culture, null, string.Format(LM.Get("landmark_book_ideology_outbreak_log"), title,
            culture.GetCultureTranslate(), PartySystem.GetIdeologyName(ideology), changed), -1L);
    }

    #region 藏书的持续理念影响

    // 藏在城里的理念名著每年持续说服本城成年人(强度 = ideology_outbreak × 本比例)，
    // 再由本城向相邻城市传播：邻城强度按本城该理念信众占比打折，信的人越多传得越远。
    // 刻意做得温和：每年只改变百分之一二的人，且某城信众达到 SaturationShare 后书就不再推动该城，
    // 只让思想在当地扎根、慢慢外溢，不会把整片地区洗成同一种理念
    private const float CityInfluenceFactor = 0.08f;
    private const float NeighbourSpreadFactor = 0.35f;
    private const float MaxCityIntensity = 0.05f;
    private const float SaturationShare = 0.4f;

    // 循序渐进：新书只有 25% 的影响力，随成书年数在 MaturityYears 年内逐渐增至满额；
    // 城市社会条件(书的 environment：繁荣/工业化/工潮/军事化/大众政治)不成熟时，只有 UnripeSocietyFactor 的影响力。
    // 于是思想先在少数城市缓慢扎根，等社会发展到相应阶段才真正流行起来
    private const float MaturityYears = 40f;
    private const float InitialMaturity = 0.25f;
    private const float UnripeSocietyFactor = 0.35f;

    private static float Maturity(Book book)
    {
        double created = book?.data?.created_time ?? 0d;
        if (created <= 0d || World.world == null) return 1f;
        float years = Math.Max(0f, Date.getYearsSince(created));
        return InitialMaturity + (1f - InitialMaturity) * Math.Min(1f, years / MaturityYears);
    }

    private static float Damped(City city, PartyIdeology ideology, float intensity) =>
        intensity * Math.Max(0f, 1f - IdeologyPopulationSystem.GetCityShare(city, ideology) / SaturationShare);

    public readonly struct CityIdeologyBook
    {
        public readonly Book Book;
        public readonly PartyIdeology Ideology;
        public readonly float Maturity;       // 0.25~1：成书越久越高
        public readonly bool SocietyReady;    // 本城社会条件是否符合这本书
        public readonly float Strength;

        public CityIdeologyBook(Book book, PartyIdeology ideology, float maturity, bool societyReady, float strength)
        {
            Book = book;
            Ideology = ideology;
            Maturity = maturity;
            SocietyReady = societyReady;
            Strength = strength;
        }
    }

    // 城里藏着的理念名著(连同发酵程度、社会条件是否成熟)
    public static List<CityIdeologyBook> GetCityIdeologyBooks(City city)
    {
        var result = new List<CityIdeologyBook>();
        if (city?.buildings == null || World.world?.books == null) return result;
        var environmentFit = new Dictionary<string, bool>();
        foreach (Building building in city.buildings)
        {
            List<long> ids = building?.data?.books?.list_books;
            if (ids == null) continue;
            foreach (long id in ids)
            {
                Book book = World.world.books.get(id);
                if (book == null || book.isRekt()) continue;
                // 理念著作(见 SpeechFreedomSystem)：影响力比名著小，不看社会条件
                if (book.TryGetIdeologyTreatise(out BookExtension.BookExtraData treatise) &&
                    Enum.TryParse(treatise.ideology, out PartyIdeology treatiseIdeology))
                {
                    float treatiseMaturity = Maturity(book);
                    result.Add(new CityIdeologyBook(book, treatiseIdeology, treatiseMaturity, true,
                        SpeechFreedomSystem.TreatiseOutbreak * CityInfluenceFactor * treatiseMaturity *
                        SpeechFreedomSystem.BookReach(city, treatiseIdeology)));
                    continue;
                }
                if (!book.TryGetLandmark(out BookExtension.BookExtraData data) ||
                    !TryGetBook(data.landmark_id, out LandmarkBookConfig config) ||
                    !TryGetIdeology(config, out PartyIdeology ideology) || config.ideology_outbreak <= 0f) continue;
                if (!environmentFit.TryGetValue(config.id, out bool fits))
                    environmentFit[config.id] = fits = MeetsEnvironmentCached(city, config);
                float maturity = Maturity(book);
                // 言论自由对名著同样适用：严格时非立国理念的名著被查禁，宽松时流传更广
                result.Add(new CityIdeologyBook(book, ideology, maturity, fits,
                    config.ideology_outbreak * CityInfluenceFactor * maturity * (fits ? 1f : UnripeSocietyFactor) *
                    SpeechFreedomSystem.BookReach(city, ideology)));
            }
        }
        return result;
    }

    // 理念 → 强度(多本同理念的书叠加，有上限)
    public static Dictionary<PartyIdeology, float> GetCityBookInfluences(City city)
    {
        var result = new Dictionary<PartyIdeology, float>();
        foreach (CityIdeologyBook book in GetCityIdeologyBooks(city))
        {
            result.TryGetValue(book.Ideology, out float current);
            result[book.Ideology] = Math.Min(MaxCityIntensity, current + book.Strength);
        }
        return result;
    }

    // 以前全图一次算完要 400 ms 左右(每本书都要算城市的就业/土地报告，邻城再算一遍)，年年卡一下。
    // 现在由年度理念交往(IdeologyPopulationSystem.TickContact)处理完后排队，每帧只花约 2 ms；
    // 城市经济形态、书的社会条件每轮各算一次。
    private static readonly FrameBudgetQueue<City> InfluenceQueue = new(2d, InfluenceFromCity, "藏书理念影响");
    private static readonly Dictionary<(long city, string environment), bool> EnvironmentCache = new();

    public static void ResetRuntimeState()
    {
        InfluenceQueue.Cancel();
        EnvironmentCache.Clear();
    }

    public static void StartCityInfluence()
    {
        if (World.world?.cities == null) return;
        EnvironmentCache.Clear();
        InfluenceQueue.Start(World.world.cities);
    }

    public static void TickCityInfluence() => InfluenceQueue.Tick();

    private static void InfluenceFromCity(City city)
    {
        if (city == null || city.isRekt() || city.units == null || city.units.Count == 0) return;
        Dictionary<PartyIdeology, float> influences = GetCityBookInfluences(city);
        foreach (KeyValuePair<PartyIdeology, float> pair in influences)
        {
            float radiance = pair.Value * NeighbourSpreadFactor *
                             Math.Min(1f, IdeologyPopulationSystem.GetCityShare(city, pair.Key) / SaturationShare);
            IdeologyPopulationSystem.InfluenceCity(city, pair.Key, Damped(city, pair.Key, pair.Value));
            if (radiance <= 0f) continue;
            foreach (City neighbour in city.neighbours_cities ?? Enumerable.Empty<City>())
            {
                if (neighbour == null || neighbour.isRekt() || neighbour.units == null) continue;
                IdeologyPopulationSystem.InfluenceCity(neighbour, pair.Key, Damped(neighbour, pair.Key, radiance));
            }
        }
    }

    // 书的社会条件(繁荣/工业化/工潮……)要算城市的就业和土地报告，很贵；同一轮里同一城同一条件只算一次
    private static bool MeetsEnvironmentCached(City city, LandmarkBookConfig config)
    {
        var key = (city.id, config.environment ?? "");
        if (EnvironmentCache.TryGetValue(key, out bool cached)) return cached;
        bool result = MeetsEnvironment(city, config);
        EnvironmentCache[key] = result;
        return result;
    }

    #endregion

    private static bool MatchesFeature(InstitutionNodeConfig node, string pattern)
    {
        if (node?.features == null || string.IsNullOrEmpty(pattern)) return false;
        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            string prefix = pattern.Substring(0, pattern.Length - 1);
            return node.features.Any(pair => pair.Value > 0f && pair.Key.StartsWith(prefix, StringComparison.Ordinal));
        }
        return node.features.TryGetValue(pattern, out float value) && value > 0f;
    }

    // 本文化已有(写出或读到)的名著对某个制度节点的推动力
    public static float GetInstitutionPush(InstitutionNodeConfig node, CultureTechState state)
    {
        if (state?.known_books == null || state.known_books.Count == 0) return 0f;
        float push = 0f;
        foreach (LandmarkBookConfig book in Books)
        {
            if (!state.known_books.Contains(book.id)) continue;
            foreach (KeyValuePair<string, float> pair in book.institutions)
                if (MatchesFeature(node, pair.Key)) push += pair.Value;
        }
        return push;
    }

    // 书一出现，本文化正在推行的相关改革立刻往前推一截
    private static void PushActiveReforms(string culture, LandmarkBookConfig book)
    {
        if (book.institutions.Count == 0 || book.institution_progress <= 0f) return;
        foreach (Layer.Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Layer.Empire>())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            if (InstitutionSystem.GetPrimaryCulture(empire) != culture) continue;
            InstitutionReformState reform = empire.data.institution_state?.active_reform;
            InstitutionNodeConfig node = reform == null ? null : InstitutionDefinitionRegistry.Get(reform.node_id);
            if (node == null || !book.institutions.Keys.Any(pattern => MatchesFeature(node, pattern))) continue;
            reform.progress = Math.Min(100f, reform.progress + book.institution_progress);
        }
    }

    #endregion

    private static void Announce(string culture, Kingdom kingdom, string text, long actorId)
    {
        TranslateHelper.LogEventMessage(text, kingdom);
        foreach (Layer.Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Layer.Empire>())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            if (InstitutionSystem.GetPrimaryCulture(empire) != culture) continue;
            EmpireCraft.Scripts.System.HistoryRecordSystem.RecordHistory(empire, directContent: text,
                actorId: actorId, kingdomId: empire.CoreKingdom?.id ?? -1L);
        }
    }
}
