using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems;

// 文武等级的规则与配置，配置见 Scripts/Regimes/Configs/OfficialRanks.json。这里不碰游戏类型，取数在 OfficialRankSystem。
//   · 存档仍是 OfficeIdentity.honoraryOfficial(0~8，0 最高)。每套称谓都是九档，切政体、读旧档只换名字，不换算。
//   · 称谓由政体形态加文明决定：先找本文明，再找本形态的 default，再找 fallback；空字符串表示这种政体不设等级。
//   · 律令制(performance_ladder)按绩效直接升降，不加岗位底线；其余政体岗位定底、只升不降。
public sealed class OfficialRankEntry
{
    public string civil;
    public string military;
    public bool grade_suffix;
    public bool merit;
    // 没有军职的军人单走的一段军衔，见 soldier_ladders；九档放不下的低阶军衔放这里
    public string soldier_ladder;
}

public sealed class OfficialRankForm
{
    public bool modern;
    public bool performance_ladder;
    public string civil_label = "official_rank_label_court";
    public string military_label = "official_rank_label_martial";
    // 本形态自己的军团定底和军官底线；不写用全局的
    public List<OfficialRankGeneralFloor> general_floors;
    public int? captain_floor;
    public Dictionary<string, OfficialRankEntry> civilizations = new();
}

public sealed class OfficialRankCivilizations
{
    public Dictionary<string, string> cultures = new();
    public Dictionary<string, string> lines = new();
}

public sealed class OfficialRankGeneralFloor
{
    public float below = -1f;
    public int tier;
}

public sealed class OfficialRankSoldierStep
{
    public double performance;
    public string key = "";
}

public sealed class OfficialRankConfig
{
    public int version;
    public OfficialRankCivilizations civilizations = new();
    public OfficialRankEntry fallback = new() { civil = "grades_general", military = "army_general" };
    public Dictionary<string, OfficialRankForm> forms = new();
    public Dictionary<string, Dictionary<string, int>> floors = new();
    public Dictionary<string, List<int>> unranked = new();
    public Dictionary<string, List<int>> military_offices = new();
    public List<OfficialRankGeneralFloor> general_floors = new();
    public int captain_floor = 5;
    public int soldier_cap = 6;
    public int capital_bonus = 2;
    public Dictionary<string, List<OfficialRankSoldierStep>> soldier_ladders = new();
    // 只给这个人的军衔(元老名 → 语言键)
    public Dictionary<string, string> named_ranks = new();
}

public static class OfficialRankRules
{
    public const string DefaultCivilization = "default";
    public const string FollowMetropole = "@metropole";
    public const int Bottom = 8;

    public static int Clamp(int tier) => tier < 0 ? 0 : tier > Bottom ? Bottom : tier;

    public static string CivilizationOf(OfficialRankConfig config, string culture, string line)
    {
        if (!string.IsNullOrEmpty(culture) && config.civilizations.cultures.TryGetValue(culture, out string own)) return own;
        if (!string.IsNullOrEmpty(line) && config.civilizations.lines.TryGetValue(line, out string byLine)) return byLine;
        return DefaultCivilization;
    }

    public static OfficialRankForm FormDef(OfficialRankConfig config, string form) =>
        !string.IsNullOrEmpty(form) && config.forms.TryGetValue(form, out OfficialRankForm def) ? def : null;

    private static OfficialRankEntry Entry(OfficialRankForm form, string civilization)
    {
        if (form == null) return null;
        if (!string.IsNullOrEmpty(civilization) && form.civilizations.TryGetValue(civilization, out OfficialRankEntry own)) return own;
        return form.civilizations.TryGetValue(DefaultCivilization, out OfficialRankEntry common) ? common : null;
    }

    public static string SetOf(OfficialRankConfig config, string form, string civilization, bool military)
    {
        OfficialRankForm def = FormDef(config, form);
        string Pick(OfficialRankEntry entry) => entry == null ? null : military ? entry.military : entry.civil;
        string set = Pick(Entry(def, civilization));
        if (set != null) return set;
        if (def != null && def.civilizations.TryGetValue(DefaultCivilization, out OfficialRankEntry common) && Pick(common) != null)
            return Pick(common);
        return Pick(config.fallback) ?? "";
    }

    public static bool GradeSuffix(OfficialRankConfig config, string form, string civilization) =>
        Entry(FormDef(config, form), civilization)?.grade_suffix == true;

    public static bool ShowsMerit(OfficialRankConfig config, string form, string civilization) =>
        Entry(FormDef(config, form), civilization)?.merit == true;

    public static string Key(string set, int tier) =>
        set.Contains("{0}") ? string.Format(set, tier) : "official_rank_" + set + "_" + tier;

    // 这套缺了这一档就退到通用一套；通用也没有返回 null，由调用方显示数字
    public static string ResolveKey(OfficialRankConfig config, string set, int tier, bool military, Func<string, bool> has)
    {
        string key = Key(set, tier);
        if (has(key)) return key;
        string fallback = military ? config.fallback?.military : config.fallback?.civil;
        if (string.IsNullOrEmpty(fallback)) return null;
        key = Key(fallback, tier);
        return has(key) ? key : null;
    }

    // 文武不看存档里的类别优先：统军的算武；在任的看官职；没有官职的看在不在军中，最后才看存档
    public static bool IsMilitary(bool general, bool onOffice, bool militaryOffice, bool inArmy, bool identityMilitary)
    {
        if (general) return true;
        if (onOffice) return militaryOffice;
        return inArmy || identityMilitary;
    }

    public static bool MilitaryOffice(OfficialRankConfig config, string regime, int officeType, bool armyPost) =>
        armyPost || (config.military_offices.TryGetValue(regime, out List<int> types) && types.Contains(officeType));

    // 君主、后宫和世袭封君不列官阶
    public static bool Unranked(OfficialRankConfig config, string regime, int officeType, bool harem, bool monarchy)
    {
        if (harem || (monarchy && officeType == 0)) return true;
        return config.unranked.TryGetValue(regime, out List<int> types) && types.Contains(officeType);
    }

    public static int PostFloor(OfficialRankConfig config, string regime, int officeType, bool capital, int honorary)
    {
        int floor = -1;
        if (config.floors.TryGetValue(regime, out Dictionary<string, int> table) &&
            table.TryGetValue(officeType.ToString(), out int mapped)) floor = mapped;
        else if (honorary >= 0) floor = honorary;
        if (floor < 0) return -1;
        return Clamp(capital ? floor - config.capital_bonus : floor);
    }

    public static string NamedRankKey(OfficialRankConfig config, string name) =>
        !string.IsNullOrEmpty(name) && config.named_ranks.TryGetValue(name, out string key) ? key : null;

    // 没有军职的军人这一段军衔：按绩效取最高够得上的一级；这一套没配返回 null
    public static string SoldierKey(OfficialRankConfig config, string form, string civilization, double performance)
    {
        string ladder = Entry(FormDef(config, form), civilization)?.soldier_ladder;
        if (string.IsNullOrEmpty(ladder) || !config.soldier_ladders.TryGetValue(ladder, out List<OfficialRankSoldierStep> steps) || steps.Count == 0)
            return null;
        string key = steps[0].key;
        foreach (OfficialRankSoldierStep step in steps)
            if (performance >= step.performance) key = step.key;
        return key;
    }

    public static int CaptainFloor(OfficialRankConfig config, string form) =>
        Clamp(FormDef(config, form)?.captain_floor ?? config.captain_floor);

    public static int GeneralFloor(OfficialRankConfig config, string form, float full)
    {
        foreach (OfficialRankGeneralFloor floor in FormDef(config, form)?.general_floors ?? config.general_floors)
            if (floor.below <= 0f || full < floor.below) return Clamp(floor.tier);
        return -1;
    }

    // 与律令制同一条绩效线
    public static int FromPerformance(double performance)
    {
        if (performance > 1600d) return 0;
        if (performance > 1300d) return 1;
        if (performance > 1000d) return 2;
        return Clamp(Bottom - (int)Math.Floor(performance / 200d));
    }

    // 只升不降：better 小于 0 表示没有依据
    public static int Ratchet(int stored, int better) => better < 0 ? Clamp(stored) : Math.Min(Clamp(stored), Clamp(better));

    // 显示用的档位；返回 -1 表示不显示(还没立过功的普通士兵)
    public static int Effective(OfficialRankConfig config, bool ladder, int stored, int postFloor, int generalFloor,
        int captainFloor, bool plainSoldier, double performance)
    {
        int tier = Clamp(stored);
        if (ladder) return tier;
        if (plainSoldier)
        {
            if (tier >= Bottom && performance <= 0d) return -1;
            return Math.Max(tier, Clamp(config.soldier_cap));
        }
        if (postFloor >= 0) tier = Math.Min(tier, Clamp(postFloor));
        if (generalFloor >= 0) tier = Math.Min(tier, Clamp(generalFloor));
        if (captainFloor >= 0) tier = Math.Min(tier, Clamp(captainFloor));
        return tier;
    }
}
