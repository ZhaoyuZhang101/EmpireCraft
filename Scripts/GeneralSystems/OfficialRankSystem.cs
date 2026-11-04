using System;
using System.IO;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一个人的文武等级按所在政体和文明显示成什么(律令的散官、现代的军衔与文职级别、封建的宫廷官阶……)，规则见 OfficialRankRules。
// 只有 OnAppointed、OnPerformance、OnCommandEnded、CarryOver 写存档字段，显示和搜索都不改数据。
public readonly struct OfficialRank
{
    public readonly bool Shown;
    public readonly bool Military;
    public readonly bool Merit;
    public readonly int Tier;
    public readonly string Key;
    public readonly string Name;
    public readonly string Text;
    public readonly string LabelKey;

    public OfficialRank(bool military, bool merit, int tier, string key, string name, string text, string labelKey)
    {
        Shown = true;
        Military = military;
        Merit = merit;
        Tier = tier;
        Key = key;
        Name = name;
        Text = text;
        LabelKey = labelKey;
    }
}

public static class OfficialRankSystem
{
    private const string SenatorKey = "official_rank_aosong_senator";
    private static OfficialRankConfig _config;

    public static OfficialRankConfig Config
    {
        get
        {
            if (_config != null) return _config;
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, "Scripts", "Regimes", "Configs", "OfficialRanks.json");
                _config = JsonConvert.DeserializeObject<OfficialRankConfig>(File.ReadAllText(path)) ?? new OfficialRankConfig();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][官阶] OfficialRanks.json 读取失败，一律按通用称谓：{exception.Message}");
                _config = new OfficialRankConfig();
            }
            return _config;
        }
    }

    private static Kingdom RealmOf(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 看哪个政体的官职表：殖民地、特许公司和穿越政权看自己，其余看帝国核心
    private static RegimeType? RegimeOf(Kingdom kingdom)
    {
        RegimeType? own = kingdom?.GetRegime()?.type;
        if (own is RegimeType.Colonial or RegimeType.Company or RegimeType.Transmigrant) return own;
        RegimeType? type = RealmOf(kingdom)?.GetRegime()?.type;
        return type == RegimeType.Republic ? RegimeType.Modern : type;
    }

    private static bool IsMonarchy(Kingdom kingdom, RegimeType regime) =>
        RegimeManager.IsMonarchy(regime) && !RepublicSystem.IsRepublic(kingdom?.GetEmpire());

    // 政体形态：现代政体分一党与多党，君主国开了议会算立宪，其余就是政体本身
    public static string FormOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        RegimeType? own = kingdom.GetRegime()?.type;
        if (own is RegimeType.Colonial or RegimeType.Company) return own.ToString();
        Empire empire = kingdom.GetEmpire();
        Kingdom realm = empire?.CoreKingdom ?? kingdom;
        RegimeType? type = realm?.GetRegime()?.type;
        if (type == null) return null;
        // 只看自己和核心：澳宋混在别国帝国里时，那个帝国的官员不跟着变
        if (own == RegimeType.Transmigrant || type == RegimeType.Transmigrant ||
            TransmigrantSystem.IsTransmigrant(kingdom) || TransmigrantSystem.IsTransmigrant(realm))
            return nameof(RegimeType.Transmigrant);
        if (type is RegimeType.Modern or RegimeType.Republic || RepublicSystem.IsRepublic(empire))
            return RepublicSystem.IsOneParty(empire) || PartyBanSystem.UsesDemocraticCentralism(empire) ? "one_party" : "republic";
        if (RegimeManager.IsMonarchy(type) && ParliamentSystem.HasParliament(empire)) return "constitutional";
        return type.ToString();
    }

    public static string CivilizationOf(Kingdom kingdom)
    {
        string culture = CultureService.GetRealmCulture(RealmOf(kingdom));
        return OfficialRankRules.CivilizationOf(Config, culture, string.IsNullOrEmpty(culture) ? "" : InstitutionSystem.GetCultureLine(culture));
    }

    public static bool UsesPerformanceLadder(Kingdom kingdom) =>
        OfficialRankRules.FormDef(Config, FormOf(kingdom))?.performance_ladder == true;

    public static bool IsModern(Kingdom kingdom) => OfficialRankRules.FormDef(Config, FormOf(kingdom))?.modern == true;

    public static string PyramidKey(Empire empire) =>
        IsModern(empire?.CoreKingdom) ? "bureau_office_pyramid_modern" : "bureau_office_pyramid";

    // 称谓和它的标题。殖民地、特许公司的武职跟宗主国走
    private static string SetFor(Kingdom kingdom, string form, bool military, out string labelKey, out string rankForm, out string civilization)
    {
        civilization = CivilizationOf(kingdom);
        rankForm = form;
        string set = OfficialRankRules.SetOf(Config, form, civilization, military);
        if (set == OfficialRankRules.FollowMetropole)
        {
            Kingdom metropole = ColonySystem.MetropoleOf(kingdom);
            rankForm = FormOf(metropole);
            civilization = metropole == null ? civilization : CivilizationOf(metropole);
            set = metropole == null ? "" : OfficialRankRules.SetOf(Config, rankForm, civilization, military);
            if (string.IsNullOrEmpty(set) || set == OfficialRankRules.FollowMetropole)
                set = military ? Config.fallback?.military : Config.fallback?.civil;
        }
        OfficialRankForm def = OfficialRankRules.FormDef(Config, rankForm);
        labelKey = (military ? def?.military_label : def?.civil_label) ?? (military ? "official_rank_label_military" : "official_rank_label_civil");
        return set;
    }

    private static OfficialRank Build(string form, string civilization, string set, string labelKey, int tier, bool military)
    {
        string key = OfficialRankRules.ResolveKey(Config, set, tier, military, candidate => LM.Has(candidate));
        string name = key != null ? LM.Get(key) : string.Format(LM.Get("official_rank_fallback_format"), tier + 1);
        string text = OfficialRankRules.GradeSuffix(Config, form, civilization)
            ? name + string.Format(LM.Get("official_rank_grade_suffix"), tier + 1)
            : name;
        return new OfficialRank(military, OfficialRankRules.ShowsMerit(Config, form, civilization), tier, key, name, text, labelKey);
    }

    private static bool IsCapitalPost(OfficeObject office) =>
        office?.meta_object is City city && city.data != null && city.kingdom?.capital == city;

    private static int PostFloor(OfficeObject office, RegimeType regime) =>
        OfficialRankRules.PostFloor(Config, regime.ToString(), office.officeType, IsCapitalPost(office), office.honorary);

    public static OfficialRank Of(Actor actor)
    {
        if (actor?.data == null || !actor.hasKingdom()) return default;
        Kingdom kingdom = actor.kingdom;
        OfficeIdentity identity = actor.GetIdentity();
        bool general = CorpsSystem.IsGeneral(actor);
        OfficeObject office = actor.IsOnOffice() ? actor.GetOffice() : null;
        if (identity == null && !general && office == null) return default;
        string form = FormOf(kingdom);
        RegimeType? regimeType = RegimeOf(kingdom);
        if (form == null || regimeType == null) return default;
        RegimeType regime = regimeType.Value;
        string regimeName = regime.ToString();
        bool monarchy = IsMonarchy(kingdom, regime);
        bool sovereign = actor.IsEmperor() || (actor.isKing() && !kingdom.IsInEmpire());
        if (monarchy && sovereign) return default;
        if (office != null && OfficialRankRules.Unranked(Config, regimeName, office.officeType,
                office.leader_select_method == LeaderSelectMethod.Harem, monarchy)) return default;

        // 只给这个人的军衔，不看眼下任什么职
        string named = OfficialRankRules.NamedRankKey(Config, TransmigrantSystem.RosterNameOf(actor));
        if (named != null && LM.Has(named))
        {
            SetFor(kingdom, form, true, out string namedLabel, out _, out _);
            return new OfficialRank(true, false, 0, named, LM.Get(named), LM.Get(named), namedLabel);
        }

        bool militaryOffice = office != null && OfficialRankRules.MilitaryOffice(Config, regimeName, office.officeType, office.meta_object is Army);
        bool inArmy = actor.hasArmy();
        bool military = OfficialRankRules.IsMilitary(general, office != null, militaryOffice, inArmy,
            identity?.peerageType == PeerageType.Military);
        string set = SetFor(kingdom, form, military, out string labelKey, out string rankForm, out string civilization);
        if (string.IsNullOrEmpty(set)) return default;
        if (!military && TransmigrantSystem.IsSenator(actor))
        {
            string senator = LM.Get(SenatorKey);
            return new OfficialRank(false, false, 0, SenatorKey, senator, senator, labelKey);
        }

        bool ladder = OfficialRankRules.FormDef(Config, form)?.performance_ladder == true;
        bool captain = actor.is_army_captain;
        int postFloor = office != null ? PostFloor(office, regime) : sovereign ? 0 : -1;
        int generalFloor = general && CorpsSystem.CommandOf(actor) is CorpsData corps ? OfficialRankRules.GeneralFloor(Config, rankForm, corps.full) : -1;
        bool noCommand = military && !general && !captain && office == null;
        int stored = identity?.honoraryOfficial ?? OfficialRankRules.Bottom;
        double performance = identity?.TotalPerformance ?? 0d;
        // 九档放不下的低阶军衔：没带过兵、也没任过高职的军人走这一段
        if (noCommand && OfficialRankRules.Clamp(stored) >= OfficialRankRules.Clamp(Config.soldier_cap))
        {
            string step = OfficialRankRules.SoldierKey(Config, rankForm, civilization, performance);
            if (step != null && LM.Has(step)) return new OfficialRank(true, false, OfficialRankRules.Bottom, step, LM.Get(step), LM.Get(step), labelKey);
        }
        int tier = OfficialRankRules.Effective(Config, ladder, stored, postFloor, generalFloor,
            captain ? OfficialRankRules.CaptainFloor(Config, rankForm) : -1, noCommand && inArmy, performance);
        return tier < 0 ? default : Build(rankForm, civilization, set, labelKey, tier, military);
    }

    // 空缺的官位：按岗位本身的底线；律令制不设底线，不显示
    public static OfficialRank OfPost(OfficeObject office, Kingdom kingdom)
    {
        if (office == null) return default;
        string form = FormOf(kingdom);
        RegimeType? regimeType = RegimeOf(kingdom);
        if (form == null || regimeType == null || OfficialRankRules.FormDef(Config, form)?.performance_ladder == true) return default;
        RegimeType regime = regimeType.Value;
        string regimeName = regime.ToString();
        if (OfficialRankRules.Unranked(Config, regimeName, office.officeType, office.leader_select_method == LeaderSelectMethod.Harem,
                IsMonarchy(kingdom, regime))) return default;
        int floor = PostFloor(office, regime);
        if (floor < 0) return default;
        bool military = OfficialRankRules.MilitaryOffice(Config, regimeName, office.officeType, office.meta_object is Army);
        string set = SetFor(kingdom, form, military, out string labelKey, out string rankForm, out string civilization);
        return string.IsNullOrEmpty(set) ? default : Build(rankForm, civilization, set, labelKey, floor, military);
    }

    // 上任：到岗位对应的级别，卸任后保留
    public static void OnAppointed(Actor actor, OfficeObject office)
    {
        OfficeIdentity identity = actor?.GetIdentity();
        if (identity == null || office == null || !actor.hasKingdom()) return;
        string form = FormOf(actor.kingdom);
        RegimeType? regime = RegimeOf(actor.kingdom);
        if (form == null || regime == null || OfficialRankRules.FormDef(Config, form)?.performance_ladder == true) return;
        identity.honoraryOfficial = OfficialRankRules.Ratchet(identity.honoraryOfficial, PostFloor(office, regime.Value));
    }

    // 考核、战功：律令制以外只升不降。没有军职的普通士兵不超过上限，免得杀敌多了直接当将军
    public static void OnPerformance(Actor actor)
    {
        OfficeIdentity identity = actor?.GetIdentity();
        if (identity == null) return;
        int earned = OfficialRankRules.FromPerformance(identity.TotalPerformance);
        if (actor.hasArmy() && !actor.is_army_captain && !CorpsSystem.IsGeneral(actor) && !actor.IsOnOffice())
            earned = Math.Max(earned, OfficialRankRules.Clamp(Config.soldier_cap));
        identity.honoraryOfficial = OfficialRankRules.Ratchet(identity.honoraryOfficial, earned);
    }

    // 卸下军团：按统过的兵力留军衔
    public static void OnCommandEnded(Actor general, CorpsData corps)
    {
        OfficeIdentity identity = general?.GetIdentity();
        if (identity == null || corps == null || !general.hasKingdom() || UsesPerformanceLadder(general.kingdom)) return;
        SetFor(general.kingdom, FormOf(general.kingdom), true, out _, out string rankForm, out _);
        identity.honoraryOfficial = OfficialRankRules.Ratchet(identity.honoraryOfficial, OfficialRankRules.GeneralFloor(Config, rankForm, corps.full));
    }

    // 换岗时身份重建：律令制以外不掉级
    public static void CarryOver(OfficeIdentity previous, OfficeIdentity next, Actor actor)
    {
        if (previous == null || next == null || ReferenceEquals(previous, next) || actor == null || !actor.hasKingdom()) return;
        string form = FormOf(actor.kingdom);
        if (form == null || OfficialRankRules.FormDef(Config, form)?.performance_ladder == true) return;
        next.honoraryOfficial = OfficialRankRules.Clamp(previous.honoraryOfficial);
        next.peerageType = previous.peerageType;
    }

    public static string NameFor(Actor actor)
    {
        OfficialRank rank = Of(actor);
        return rank.Shown ? rank.Text : LM.Get("label_none");
    }

    public static string SearchText(Actor actor)
    {
        OfficialRank rank = Of(actor);
        return rank.Shown ? rank.Name : "";
    }

    public static string PanelLabel(Actor actor)
    {
        OfficialRank rank = Of(actor);
        return LM.Get(!rank.Shown || UsesPerformanceLadder(actor?.kingdom) ? "label_rank" : rank.LabelKey);
    }
}
