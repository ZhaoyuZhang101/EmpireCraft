using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 言论自由(宪法条款)与理念著作。每年由 ConstitutionalEconomySystem 调用。
//
// 理念著作：国内的读书人每年写作宣扬某种理念的书(宣言、纲领、问答、演讲集、小说、诗集……)，
// 书名取自理念书库(见下)。它们放进城里的藏书，与理念名著走同一套传播逻辑(LandmarkBookSystem.GetCityIdeologyBooks)：
// 影响本城并向邻城外溢，成书越久影响越大，信众饱和后不再推动。单本影响力是名著的 TreatiseOutbreak 倍。
//
// 言论自由三档：
//   · 宽松：著作多(每 6 城一本/年，最多 6 本)，作者写自己信的理念，百家争鸣；所有理念的书流传更广(×1.2)；
//           思想交锋让立国理念保持活力，意识形态疲劳每年少积累 1(见 IdeologyDynamicsSystem)；
//   · 一般：每 15 城一本/年(最多 3 本，每本一半概率出版)，一半是立国理念的书；
//   · 严格：出版审查，只出立国理念的书(每 15 城一本/年，最多 3 本)；非立国理念的书(含名著)被查禁，影响只剩三成；
//           但宣传显得空洞，意识形态疲劳每年多积累 1。
//
// 理念书库：Locales/Cultures 下 key,cz,en,ch 的 CSV——
//   Books/IdeologyBooks.csv                  通用书库；
//   Culture_<文化>/<文化>IdeologyBooks.csv    文化自己的书库(与通用书库合并使用)。
//   key 以 "<理念>_" 开头(如 Socialism_3)的是该理念专属书名；以 "Any_" 开头的是各理念通用的体裁模板。
//   模板占位符：{0} 理念名  {1} 作者  {2} 作者所在城市。
public static class SpeechFreedomSystem
{
    public const float TreatiseOutbreak = 0.3f;
    private const float CensoredReach = 0.3f;
    private const float FreeReach = 1.2f;
    private const string LibraryName = "IdeologyBooks";

    // 这本宣扬 ideology 的书在该城的流传程度(言论自由决定)
    public static float BookReach(City city, PartyIdeology ideology)
    {
        Empire empire = city?.kingdom?.GetEmpire();
        if (empire == null) return 1f;
        // 思想解放期(见 IdeologyDynamicsSystem)：各种理念书籍的影响力最多翻倍
        float liberation = 1f + IdeologyDynamicsSystem.GetLiberation(empire);
        ConstitutionClauses clauses = ConstitutionSystem.GetClauses(empire);
        if (clauses == null) return liberation;
        return liberation * clauses.speech switch
        {
            ConstitutionSpeech.Free => FreeReach,
            ConstitutionSpeech.Strict => ideology == clauses.founding_ideology ? 1f : CensoredReach,
            _ => 1f
        };
    }

    // 意识形态疲劳的年增量修正
    public static float FatigueModifier(Empire empire) => ConstitutionSystem.GetSpeech(empire) switch
    {
        ConstitutionSpeech.Free => -1f,
        ConstitutionSpeech.Strict => 1f,
        _ => 0f
    };

    public static void Update(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived() || World.world?.books == null) return;
        ConstitutionClauses clauses = ConstitutionSystem.GetClauses(empire);
        if (clauses == null) return;
        List<City> cities = empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
            .SelectMany(kingdom => kingdom.cities)
            .Where(city => city != null && !city.isRekt() && city.units != null && city.hasBookSlots()).ToList();
        if (cities.Count == 0) return;
        int books = clauses.speech switch
        {
            ConstitutionSpeech.Free => Mathf.Clamp(cities.Count / 6, 1, 6),
            _ => Mathf.Clamp(cities.Count / 15, 1, 3)
        };
        // 思想解放期：著作出版量最多翻倍
        books = Mathf.RoundToInt(books * (1f + IdeologyDynamicsSystem.GetLiberation(empire)));
        bool announced = false;
        for (int i = 0; i < books; i++)
        {
            if (clauses.speech == ConstitutionSpeech.Limited && UnityEngine.Random.value < 0.5f) continue;
            try
            {
                // 每国每年只发一条世界提示，免得宽松的大国刷屏
                if (Publish(cities.GetRandom(), clauses, !announced)) announced = true;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 理念著作出版失败: {exception.Message}");
                return;
            }
        }
    }

    // 出版一本理念著作；成功返回 true。一个理念要本文化已有它的奠基名著(资本论、国富论……)才能有人写它的著作
    private static bool Publish(City city, ConstitutionClauses clauses, bool announce)
    {
        if (city == null || city.isRekt() || !city.hasBookSlots()) return false;
        List<Actor> writers = city.units.Where(actor => actor != null && actor.isAlive() && actor.isAdult() &&
                                                         actor.city == city && actor.language != null &&
                                                         actor.culture != null && !actor.IsWarMachine())
            .OrderByDescending(actor => actor.stats["intelligence"]).Take(5).ToList();
        if (writers.Count == 0) return false;
        PartyIdeology founding = clauses.founding_ideology;
        bool official = clauses.speech == ConstitutionSpeech.Strict ||
                        clauses.speech == ConstitutionSpeech.Limited && UnityEngine.Random.value < 0.5f;
        Actor author;
        PartyIdeology ideology;
        if (official)
        {
            // 官方出版物：由信奉立国理念的人执笔(没有就找个人改信)
            author = writers.FirstOrDefault(actor => IdeologyPopulationSystem.Get(actor) == founding) ?? writers.GetRandom();
            ideology = founding;
            if (!LandmarkBookSystem.HasFoundingBook(TechnologySystem.GetCultureOf(author), ideology)) return false;
            if (IdeologyPopulationSystem.Get(author) != founding) IdeologyPopulationSystem.Set(author, founding);
        }
        else
        {
            // 作者写自己信的理念——只有奠基名著已在本文化流传的理念才写得出来
            author = writers.Where(actor => LandmarkBookSystem.HasFoundingBook(TechnologySystem.GetCultureOf(actor),
                    IdeologyPopulationSystem.Get(actor)))
                .OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
            if (author == null) return false;
            ideology = IdeologyPopulationSystem.Get(author);
        }

        Book book = World.world.books.generateNewBook(author);
        if (book == null) return false;
        string culture = TechnologySystem.GetCultureOf(author);
        string title = PickTitle(culture, ideology, author, city);
        if (!string.IsNullOrWhiteSpace(title)) book.setName(BookNamingSystem.ClaimUnique(title));
        BookExtension.BookExtraData data = book.GetOrCreate();
        data.id = book.getID();
        data.ideology = ideology.ToString();
        data.culture = culture ?? "";
        if (announce) Announce(author, book, ideology);
        return true;
    }

    private static void Announce(Actor author, Book book, PartyIdeology ideology)
    {
        Kingdom kingdom = author.kingdom;
        if (kingdom == null) return;
        Color color = kingdom.getColor()._color_text;
        new WorldLogMessage(GameLibrary.EmpireCraftWorldLogLibrary.ideology_treatise_log,
            author.getName(), book.name, PartySystem.GetIdeologyName(ideology))
        {
            location = author.current_position,
            color_special1 = color,
            color_special2 = color,
            color_special3 = color
        }.add();
    }

    // 优先用该理念的专属书名(没用过的)，否则用体裁模板组合
    private static string PickTitle(string culture, PartyIdeology ideology, Actor author, City city)
    {
        List<(string key, string text)> entries = BookNamingSystem.Entries(culture, LibraryName);
        if (entries.Count == 0) return null;
        string prefix = ideology + "_";
        List<string> own = entries.Where(entry => entry.key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry => entry.text).Where(text => !BookNamingSystem.IsUsed(Fill(text, ideology, author, city)))
            .ToList();
        List<string> templates = entries.Where(entry => entry.key.StartsWith("Any_", StringComparison.Ordinal))
            .Select(entry => entry.text).ToList();
        string pattern = own.Count > 0 && (templates.Count == 0 || UnityEngine.Random.value < 0.6f)
            ? own.GetRandom()
            : templates.Count > 0 ? templates.GetRandom() : null;
        return pattern == null ? null : Fill(pattern, ideology, author, city);
    }

    private static string Fill(string pattern, PartyIdeology ideology, Actor author, City city)
    {
        bool chinese = LocalizedTextManager.instance?.language is "cz" or "ch";
        string Tidy(string text) => chinese && text != null ? text.Replace(" ", "") : text ?? "";
        string result = pattern.Replace("{0}", PartySystem.GetIdeologyName(ideology))
            .Replace("{1}", Tidy(author?.getName()))
            .Replace("{2}", Tidy(city?.name));
        // {3}：该理念奠基名著在本文化的书名(导读、注疏、重读……)
        if (result.Contains("{3}"))
        {
            LandmarkBookConfig founding = LandmarkBookSystem.FoundingBook(ideology);
            string culture = author == null ? null : TechnologySystem.GetCultureOf(author);
            result = result.Replace("{3}", founding == null ? PartySystem.GetIdeologyName(ideology)
                : LandmarkBookSystem.GetTitle(founding.id, culture));
        }
        return result;
    }
}
