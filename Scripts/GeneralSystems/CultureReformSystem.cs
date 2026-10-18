using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 易俗改制(如北魏孝文帝改革)：国家的文化与治下多数百姓不同时，君主改从百姓之俗——
//   · 条件：都城的主流文化与国家文化(征服王朝看统治族群的文化)不同，且是治下过半城市的主流文化
//     (帝国核心按全帝国的城市算)；征服王朝还要这个文化就是它采用的制度文化；
//   · 国家文化、法理文化(未锁定、未开固定法理文化时)改为这个文化，政体随之改为该文化的政体
//     (帝国核心由制度系统重算)；征服王朝融合一步到位；
//   · 王族与旧俗出身的在任官员改从新俗；宗族本非这个文化的，整支改用新文化的姓氏(改姓)；
//   · 旧俗为主的城市一时不安。
// 只有游牧(国家文化、征服王朝的统治族群文化是游牧政体的文化)走这条决议(玩家："那么大一个游牧汗国直接变成法兰克了"
// "能不能让别的文化不要参与改朝换代"；用户 2026-10-17："除了游牧，游牧的改朝换代是决议不是自动的")；
// 别的文化不易俗，治下百姓的文化由同化、教化慢慢改。神力强制改俗(Force)不受此限。
public static class CultureReformSystem
{
    public const string Plot = "kingdom_culture_reform";
    // 旧俗为主的城市一次性掉的稳定度
    private const float OldCustomShock = 15f;
    // 一次改制最多给多少个官员宗族改姓(王族不算在内)
    private const int MaxRenamedClans = 32;

    public static bool CanReform(Actor actor) =>
        actor?.kingdom?.king == actor && FindCandidate(actor.kingdom, out _, out _);

    public static bool FindCandidate(Kingdom kingdom, out string target, out string previous)
    {
        target = previous = null;
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.king == null || AncientWarfareCompatibility.Owns(kingdom) ||
            JimiSystem.IsJimiAdministration(kingdom) || CultureService.IsProtectorateGeneralTitle(kingdom.GetMainTitle()))
            return false;
        Empire empire = kingdom.GetEmpire();
        bool core = empire?.data != null && empire.CoreKingdom == kingdom && !empire.IsArchived() && !empire.isRekt();
        bool composite = core && CompositeEmpireService.IsComposite(empire);
        previous = composite ? CompositeEmpireService.GetRulingCulture(empire) : CultureService.GetRealmCulture(kingdom);
        if (!CompositeEmpireService.IsNomadicCulture(previous)) return false;
        City capital = kingdom.king.city ?? kingdom.capital;
        if (capital?.data == null || capital.isRekt() || capital.kingdom != kingdom) return false;
        string local = CultureService.GetMainCulture(capital);
        if (!CultureService.IsValidCulture(local) || string.Equals(local, previous, StringComparison.Ordinal)) return false;
        if (composite && !string.Equals(local, CompositeEmpireService.GetInstitutionalCulture(empire), StringComparison.Ordinal))
            return false;
        // 征服王朝要已走上从俗之路(见 CompositeEmpireService.GetRoute)：分治、兼用的不易俗
        if (composite && CompositeEmpireService.GetRoute(empire) != CompositeEmpireService.RouteAdopt) return false;
        int total = 0, same = 0;
        foreach (City city in RealmCities(kingdom, core ? empire : null))
        {
            total++;
            if (string.Equals(CultureService.GetMainCulture(city), local, StringComparison.Ordinal)) same++;
        }
        if (total == 0 || same * 2 <= total) return false;
        target = local;
        return true;
    }

    private static IEnumerable<City> RealmCities(Kingdom kingdom, Empire empire)
    {
        IEnumerable<Kingdom> kingdoms = empire?.kingdoms_list != null ? empire.kingdoms_list : new List<Kingdom> { kingdom };
        foreach (Kingdom member in kingdoms)
        {
            if (member?.cities == null || member.isRekt()) continue;
            foreach (City city in member.cities)
                if (city?.data != null && !city.isRekt()) yield return city;
        }
    }

    public static bool Enact(Actor king)
    {
        Kingdom kingdom = king?.kingdom;
        if (kingdom?.king != king || !FindCandidate(kingdom, out string target, out string previous)) return false;
        return Apply(kingdom, king, target, previous, false);
    }

    // 神力易俗(见 GodPowers/CityCulturePower)：本国最后一座城改了文化，国家连同政体、制度当即改从新俗——不看条件，
    // 法理文化锁定、固定法理文化也照改
    public static bool Force(Kingdom kingdom, string target)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !CultureService.IsValidCulture(target)) return false;
        Empire empire = kingdom.GetEmpire();
        bool core = empire?.data != null && empire.CoreKingdom == kingdom && !empire.IsArchived() && !empire.isRekt();
        string previous = core && CompositeEmpireService.IsComposite(empire)
            ? CompositeEmpireService.GetRulingCulture(empire) : CultureService.GetRealmCulture(kingdom);
        if (string.Equals(previous, target, StringComparison.Ordinal)) return false;
        Actor king = kingdom.king != null && !kingdom.king.isRekt() && kingdom.king.isAlive() ? kingdom.king : null;
        return Apply(kingdom, king, target, previous, true);
    }

    private static bool Apply(Kingdom kingdom, Actor king, string target, string previous, bool force)
    {
        Empire empire = kingdom.GetEmpire();
        bool core = empire?.data != null && empire.CoreKingdom == kingdom;
        bool composite = core && CompositeEmpireService.IsComposite(empire);
        RegimeType? before = kingdom.GetRegime()?.type;

        // 王族的宗族出身要在改俗之前看(改完君主本人就是新文化了)
        SpecificClan royal = king?.GetSpecificClan();
        bool renameRoyal = king != null && ForeignOrigin(royal, CultureService.GetActorCulture(king), target);

        KingdomTitle title = kingdom.GetMainTitle();
        if (title?.data != null && (force || !title.data.culture_locked && !CultureService.IsFixedDeJureCultureEnabled()))
            CultureService.ConvertTitleCulture(title, target);
        if (composite) CompositeEmpireService.CompleteByReform(empire);
        // 帝国不再按开国族群的文化算征服王朝(否则"采用中原制度"会把统治文化改回去)；
        // 帝国核心的默认文化(没锁定的)、统治文化也换成新文化——以前只改国家，制度、天下、同化目标仍按旧文化
        else if (core)
        {
            empire.data.conquest_founder_culture = target;
            EmpireCore empireCore = EmpireCoreManager.Get(empire);
            if (empireCore != null && !empireCore.culture_locked) empireCore.default_culture = target;
            empire.data.ruling_culture = target;
            InstitutionSystem.ForgetRulingMajority(empire);
        }
        CultureService.SetRealmCulture(kingdom, target, updatePoliticalSystem: !core);
        if (core) InstitutionSystem.ResyncCulture(empire);

        Culture native = string.Equals(CulturePatch.GetInjectedCultureName(kingdom.culture), target, StringComparison.Ordinal)
            ? kingdom.culture
            : CultureService.GetNativeCultureObject(target);
        var taken = new HashSet<string>(SpecificClanManager._specificClans.Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
            .Select(c => c.name), StringComparer.Ordinal);
        string royalSurname = null;
        if (royal != null && renameRoyal) royalSurname = RenameLineage(royal, target, native, taken, king.getID());
        else if (royal != null) Convert(royal, target, native, null);
        else if (king != null) Convert(king, target, native, null);
        ConvertOfficials(kingdom, previous, target, native, taken, royal);

        foreach (City city in kingdom.cities ?? new List<City>())
        {
            if (city?.data == null || city.isRekt() ||
                !string.Equals(CultureService.GetMainCulture(city), previous, StringComparison.Ordinal)) continue;
            CityStabilityData state = CityStabilitySystem.State(city);
            if (state != null) state.stability = Math.Max(0f, state.stability - OldCustomShock);
        }
        if (kingdom.GetRegime()?.type != before) EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        TranslateHelper.LogCultureReform(kingdom, core ? empire : null, king, target, royalSurname);
        LogService.LogInfo($"[EmpireCraft][易俗改制] {kingdom.GetKingdomName()} {previous} -> {target}，政体 {before} -> {kingdom.GetRegime()?.type}，王族改姓 {royalSurname ?? "(不改)"}");
        return true;
    }

    // 宗族本非目标文化：看始祖记下的文化，没有记录就看这个人原来的文化。
    // 同一文化圈(制度线相同，如华夏、山海)里改俗只改风俗不改姓——以前只比文化名，汉朝改从山海之俗，
    // 刘氏皇族整支改姓"彤鱼"(玩家反馈 2026-10-16)。改姓只留给跨文化圈的(如鲜卑拓跋改从华夏之俗改姓元)
    private static bool ForeignOrigin(SpecificClan clan, string fallback, string target)
    {
        string origin = clan != null ? SpecificClanManager.getPerson(clan.founder)?.culture : null;
        if (!CultureService.IsValidCulture(origin)) origin = fallback;
        return CultureService.IsValidCulture(origin) && !string.Equals(origin, target, StringComparison.Ordinal) &&
               !SurnameInheritanceSystem.SameSphere(origin, target);
    }

    // 旧俗出身的在任官员改从新俗；宗族本非新文化的整支改姓
    private static void ConvertOfficials(Kingdom kingdom, string previous, string target, Culture native, HashSet<string> taken,
        SpecificClan royal)
    {
        var done = new HashSet<long>();
        if (royal != null) done.Add(Root(royal).id);
        foreach (Actor actor in kingdom.units.ToList())
        {
            if (actor?.data == null || actor.isRekt() || !actor.IsOnOffice() ||
                !string.Equals(CultureService.GetActorCulture(actor), previous, StringComparison.Ordinal)) continue;
            SpecificClan clan = actor.GetSpecificClan();
            if (clan == null)
            {
                Convert(actor, target, native, null);
                continue;
            }
            if (!done.Add(Root(clan).id)) continue;
            if (done.Count <= MaxRenamedClans + 1 && ForeignOrigin(clan, previous, target))
                RenameLineage(clan, target, native, taken, actor.getID());
            else Convert(clan, target, native, null);
        }
    }

    private static SpecificClan Root(SpecificClan clan)
    {
        SpecificClan root = clan;
        for (int depth = 0; depth < 32 && root.parent_clan_id > 0; depth++)
        {
            SpecificClan parent = SpecificClanManager.Get(root.parent_clan_id);
            if (parent == null) break;
            root = parent;
        }
        return root;
    }

    // 同一世系(同一始祖宗族分出来的)且同姓的各支一起改姓
    private static string RenameLineage(SpecificClan clan, string target, Culture native, HashSet<string> taken, long seed)
    {
        string surname = NewSurname(target, native, taken, seed);
        if (string.IsNullOrWhiteSpace(surname))
        {
            Convert(clan, target, native, null);
            return null;
        }
        string old = clan.name;
        long root = Root(clan).id;
        foreach (SpecificClan branch in SpecificClanManager._specificClans.ToList())
        {
            if (branch == null || branch != clan && (!string.Equals(branch.name, old, StringComparison.Ordinal) || Root(branch).id != root))
                continue;
            Convert(branch, target, native, surname);
            branch.name = surname;
        }
        return surname;
    }

    private static void Convert(SpecificClan clan, string target, Culture native, string surname)
    {
        foreach (PersonalClanIdentity person in clan.LivingPeople)
        {
            Actor actor = person._actor;
            if (actor?.data != null && !actor.isRekt())
            {
                Convert(actor, target, native, surname);
                continue;
            }
            person.culture = target;
            if (string.IsNullOrWhiteSpace(surname)) continue;
            person.surname = surname;
            person.name = string.IsNullOrWhiteSpace(person.given_name) ? person.name
                : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(target, out Setting rule) && rule?.Unit?.is_invert == true
                    ? OverallHelperFunc.JoinNameParts(person.given_name, surname)
                    : OverallHelperFunc.JoinNameParts(surname, person.given_name);
        }
    }

    private static void Convert(Actor actor, string target, Culture native, string surname)
    {
        if (native != null && actor.culture != native) actor.setCulture(native);
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        if (identity != null) identity.culture = target;
        Name name = actor.GetModName();
        if (name == null) return;
        if (!string.IsNullOrWhiteSpace(surname))
        {
            name.familyName = surname;
            if (actor.hasClan()) actor.clan.data.name = OverallHelperFunc.JoinNameParts(surname, LM.Get("Clan"));
        }
        name.SetName(actor);
    }

    // 按新文化的宗族词库取一个姓；尽量不与现有宗族重名
    private static string NewSurname(string culture, Culture native, HashSet<string> taken, long seed)
    {
        int pos = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) && setting?.Clan != null
            ? setting.Clan.name_pos
            : 0;
        string clanWord = LM.Get("Clan");
        string fallback = null;
        for (int i = 0; i < 12; i++)
        {
            string generated = CultureService.GenerateCulturalName(MetaType.Clan, seed + i * 7919L, culture) ??
                               CulturePatch.GetOnomasticDataSafe(native, MetaType.Clan)?.generateName();
            string[] parts = (generated ?? "").SplitNameParts();
            if (parts.Length == 0) continue;
            string surname = parts[pos >= 0 && pos < parts.Length ? pos : 0];
            if (string.IsNullOrWhiteSpace(surname) || surname == clanWord) continue;
            fallback ??= surname;
            if (taken.Add(surname)) return surname;
        }
        return fallback;
    }
}
