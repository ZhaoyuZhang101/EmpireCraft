using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
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

    private static bool IsTriggered(string culture, LandmarkBookConfig book) =>
        !string.IsNullOrEmpty(book.tech)
            ? TechnologySystem.HasTech(culture, book.tech)
            : !string.IsNullOrEmpty(book.feature) &&
              InstitutionSystem.GetFeature(culture, book.feature) >= book.min_value;

    #region 写书

    // 读档后第一次见到这个文化：已经达到的阶段直接记为写过、读过，不补写(否则旧存档会一下冒出一堆书)
    public static void MarkExisting(string culture, CultureTechState state)
    {
        state.landmark_books ??= new List<string>();
        state.known_books ??= new List<string>();
        foreach (LandmarkBookConfig book in Books)
        {
            if (!IsTriggered(culture, book)) continue;
            if (!state.landmark_books.Contains(book.id)) state.landmark_books.Add(book.id);
            if (!state.known_books.Contains(book.id)) state.known_books.Add(book.id);
        }
    }

    public static void YearlyCheck(string culture, List<City> cities, CultureTechState state)
    {
        state.landmark_books ??= new List<string>();
        foreach (LandmarkBookConfig book in Books)
        {
            if (state.landmark_books.Contains(book.id) || !IsTriggered(culture, book)) continue;
            if (TryWrite(culture, cities, book)) state.landmark_books.Add(book.id);
        }
    }

    private static bool TryWrite(string culture, List<City> cities, LandmarkBookConfig config)
    {
        Actor author = PickAuthor(cities);
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

        TechnologySystem.AddLandmarkBookPoints(culture);
        Inspire(culture, config, 1f);
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
        Inspire(readerCulture, config, ForeignTechFactor);
        Announce(readerCulture, null, string.Format(LM.Get("landmark_book_spread_log"),
            book.name, readerCulture.GetCultureTranslate()), -1L);
    }

    private static void Inspire(string culture, LandmarkBookConfig config, float techFactor)
    {
        CultureTechState state = TechnologySystem.GetState(culture);
        state.known_books ??= new List<string>();
        if (!state.known_books.Contains(config.id)) state.known_books.Add(config.id);
        foreach (KeyValuePair<string, float> pair in config.techs)
            TechnologySystem.AddTechProgressFraction(culture, pair.Key, pair.Value * techFactor);
        PushActiveReforms(culture, config);
    }

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
