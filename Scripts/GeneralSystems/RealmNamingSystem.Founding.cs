using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 选定国号(华夏，用户 2026-10-20 定，见方案"选定国号"一章)。帝国法理是大一号的王国法理(如晋之于韩赵魏)：
// 一片更大的地域名，由下面的王国法理组成；有人已用着它时，境内挑战的各家各用自家的王国法理名称帝。称帝时选号顺序——
//   ① 中兴沿用旧号(皇族接续，见 Empire.ReplaceEmpire)、复国用祖号(方位或"后"，见 SelectRestorationName)；
//   ② 帝国法理：据有它三分之二、占着它的首府(旧都)、它眼下无主且这个名字没人在用 → 用帝国法理名；几个都够格取主法理所在的那个；
//      名字已有人用、或它还归现存帝国 → 算挑战、僭越(建伪法理)，转③；挑战、去帝号战争、绝嗣裂土等非称帝剧情的不走这一步；
//   ③ 自家王国法理名：主法理，其次都城所在的王国法理(据点)；起义军打了前朝旗号的用前朝号；
//   ④ 美号："大"加一个吉字，取本文化美号词库(没有就用华夏的)，本天下用过的跳过；
//   ⑤ 查重：本天下前朝(已亡帝国)用过的国号再用加"后"(世界法则「姓氏国号」开着时改加开国之君的姓，同姓用方位，方位也算不出加"后")；
//      同时并存的同名照同名相争(见 RealmNamingSystem.Contest)。
// 不论选哪个名都绑脚下的帝国法理(见 EmpireManager.NewEmpire)，帝国法理名第一次生成时定下，此后不改。
// 现代政体、原始部落不在此列(另有命名)。
public static partial class RealmNamingSystem
{
    public const int SourceGrandName = 5, SourceRebelBanner = 6, SourceUsurper = 7;

    // 起义军打前朝旗号的机会
    private const float RebelBannerChance = 0.3f;

    // 挑战称帝(去帝号战争，见 ImperialLegitimacyChallengeService.StartChallenge)：以自家王国法理名称帝，不走复国、帝国法理
    private static int _challengeFounding;

    public static void BeginChallengeFounding() => _challengeFounding++;
    public static void EndChallengeFounding() => _challengeFounding = Math.Max(0, _challengeFounding - 1);

    private static bool UsesFoundingRules(Kingdom kingdom)
    {
        RegimeType? type = kingdom?.GetRegime()?.type;
        return type != null && type != RegimeType.Modern && !RegimeManager.IsPrimitive(type);
    }

    // 华夏选号(Empire.SelectFoundingEmpireName → SelectFoundingName)
    private static EmpireFoundingNameChoice SelectHuaxiaName(Empire empire, Kingdom kingdom, EmpireCore riseCore,
        EmpireFormationRoute route)
    {
        if (!UsesFoundingRules(kingdom)) return null;
        EmpireCore lead = TianxiaSystem.LeadOf(kingdom.capital);
        bool proclaim = route != EmpireFormationRoute.None && _challengeFounding == 0;
        bool lineage = route is EmpireFormationRoute.Restoration or EmpireFormationRoute.Dominion or
            EmpireFormationRoute.Legitimacy or EmpireFormationRoute.Seal;
        // ① 复国
        if (proclaim && lineage)
        {
            EmpireFoundingNameChoice restored = SelectRestorationName(empire, kingdom, DominionCore(kingdom), riseCore);
            if (restored != null) return restored;
        }
        // ② 帝国法理
        if (proclaim && route is EmpireFormationRoute.Dominion or EmpireFormationRoute.Legitimacy or EmpireFormationRoute.Seal)
        {
            EmpireCore core = TitleNameCore(kingdom, empire);
            if (core != null)
            {
                string coreName = CoreName(core);
                empire.data.name_source = SourceEmpireTitle;
                empire.data.named_core_id = core.id;
                MarkReuse(empire, kingdom, coreName, lead);
                return new EmpireFoundingNameChoice(coreName, EmpireFoundingNameSource.DominionCore);
            }
        }
        // ③ 起义军打的前朝旗号
        if (_challengeFounding == 0 && SelectBannerName(empire, kingdom) is EmpireFoundingNameChoice banner) return banner;
        // ③ 自家王国法理名
        string own = OwnTitleName(kingdom);
        if (!string.IsNullOrWhiteSpace(own))
        {
            empire.data.name_source = SourceKingdomTitle;
            MarkReuse(empire, kingdom, own, lead);
            return new EmpireFoundingNameChoice(own, EmpireFoundingNameSource.MainTitle);
        }
        // ④ 美号
        string grand = PickGrandName(CultureService.GetRealmCulture(kingdom), lead, kingdom.id);
        if (string.IsNullOrWhiteSpace(grand)) return null;
        empire.data.name_source = SourceGrandName;
        empire.data.directPre = "";
        LogService.LogInfo($"[EmpireCraft][选定国号] {kingdom.GetKingdomName()} 无王国法理可用，取美号 {grand}");
        return new EmpireFoundingNameChoice(grand, EmpireFoundingNameSource.GrandName);
    }

    // ② 够格的帝国法理：据三分之二、占首府、无主、名字没人在用；主法理所在的那个优先，其次据城多的
    private static EmpireCore TitleNameCore(Kingdom kingdom, Empire founding)
    {
        if (kingdom?.data == null || EmpireCoreControl.Sovereign(kingdom) != kingdom) return null;
        EmpireCore main = MainTitleCore(kingdom);
        EmpireCore best = null;
        int bestHeld = 0;
        foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
        {
            if (!IsFormalCore(core) || HolderOf(core, founding) != null || EmpireCoreManager.IsClaimedByContest(core, founding)) continue;
            (int held, int total) = Holding(core, kingdom);
            if (!RealmNameRules.HoldsShare(held, total)) continue;
            City seat = EmpireCoreManager.GetRepresentativeCity(core);
            if (seat?.kingdom == null || EmpireCoreControl.Sovereign(seat.kingdom) != kingdom) continue;
            if (NameInUse(CoreName(core), founding)) continue;
            if (core == main) return core;
            if (held <= bestHeld) continue;
            best = core;
            bestHeld = held;
        }
        return best;
    }

    // 这个名字眼下有现存帝国在用(不论前缀)
    private static bool NameInUse(string name, Empire except) =>
        !string.IsNullOrWhiteSpace(name) && ModClass.EMPIRE_MANAGER?.Any(other => other?.data != null && other != except &&
                                                                               !other.isRekt() && !other.IsArchived() &&
                                                                               other.CoreKingdom != null &&
                                                                               string.Equals(other.GetEmpireName(), name, StringComparison.Ordinal)) == true;

    // ③ 自家王国法理名：主法理，其次都城所在的王国法理(据点)
    private static string OwnTitleName(Kingdom kingdom)
    {
        foreach (string candidate in new[] { kingdom.GetMainTitle()?.data?.name, kingdom.capital?.GetTitle()?.data?.name })
        {
            string name = EmpireCoreManager.NormalizeCoreName(candidate);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return "";
    }

    // ④ 美号：本文化美号词库，没有就用华夏的；本天下用过的(现存、前朝)跳过
    public static string PickGrandName(string culture, EmpireCore lead, long seed)
    {
        List<string> pool = PartySystem.GetCultureWordPool(culture, "GrandNames");
        if (pool.Count == 0) pool = PartySystem.GetCultureWordPool(HuaxiaLine, "GrandNames");
        if (pool.Count == 0) return "";
        var used = new HashSet<string>(FallenDynasties(lead).Keys);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
            if (empire?.data != null && !empire.isRekt() && !empire.IsArchived()) used.Add(empire.GetEmpireName());
        int start = (int)(Math.Abs(seed) % pool.Count);
        for (int i = 0; i < pool.Count; i++)
        {
            string name = pool[(start + i) % pool.Count]?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !used.Contains(name)) return name;
        }
        return "";
    }

    // 本天下前朝(已亡帝国)用过的国号 → (皇族的姓、那一朝的都城)。按天下各帝国法理记的历代帝国与史书取
    public static Dictionary<string, (string surname, City capital)> FallenDynasties(EmpireCore lead)
    {
        var result = new Dictionary<string, (string, City)>();
        if (lead == null || ModClass.EMPIRE_MANAGER == null) return result;
        foreach (EmpireCore core in TianxiaSystem.Members(lead))
        {
            City seat = core.GetCoreCapital() ?? EmpireCoreManager.GetRepresentativeCity(core);
            foreach (long id in core.empire_history_ids ?? new List<long>())
            {
                Empire living = ModClass.EMPIRE_MANAGER.get(id);
                if (living?.data != null && !living.isRekt() && !living.IsArchived()) continue;
                if (!ModClass.ALL_HISTORY_DATA.TryGetValue(id, out var histories) || histories == null) continue;
                foreach (EmpireCraftHistory history in histories)
                {
                    string name = EmpireCoreManager.NormalizeCoreName(history?.empire_name);
                    if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name)) continue;
                    result[name] = (history.royal_surname ?? "", seat);
                }
            }
        }
        return result;
    }

    // ⑤ 查重：前朝用过的国号再用加"后"；世界法则「姓氏国号」开着时加开国之君的姓，与前朝同姓用方位，方位也算不出加"后"。
    // 已带前缀(复国的方位等)的不加
    private static void MarkReuse(Empire empire, Kingdom kingdom, string name, EmpireCore lead)
    {
        empire.data.directPre = "";
        if (lead == null || !FallenDynasties(lead).TryGetValue(name, out var former)) return;
        string prefix = ReusePrefix(kingdom, former);
        empire.data.directPre = prefix;
        empire.data.founding_prefix = prefix;
        LogService.LogInfo($"[EmpireCraft][选定国号] {kingdom.GetKingdomName()} 用 {name}，前朝用过，冠 {prefix}");
    }

    // 前朝用过的国号再用冠什么：加"后"；世界法则「姓氏国号」开着时冠开国之君的姓(姓与名之间同方位前缀一样不空格)，
    // 与前朝同姓用方位(新都相对前朝都城，四方位)，同姓又同都(方位算不出)才加"后"
    private static string ReusePrefix(Kingdom kingdom, (string surname, City capital) former)
    {
        if (EmpireCraftWorldLawLibrary.empirecraft_law_surname_dynasty?.isEnabled() != true) return RealmNameRules.Later;
        string surname = kingdom.king?.GetSpecificClan()?.name?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(surname) && !string.Equals(surname, former.surname?.Trim(), StringComparison.Ordinal))
            return surname;
        City mine = kingdom.capital;
        if (mine == null || former.capital == null || former.capital == mine) return RealmNameRules.Later;
        return RealmNameRules.Direction4(former.capital.city_center.x, former.capital.city_center.y,
            mine.city_center.x, mine.city_center.y) ?? RealmNameRules.Later;
    }

    // ③ 起义军：起事时打了前朝旗号的，称帝用前朝号；本天下有人在用同号按复国规则加方位或"后"
    private static EmpireFoundingNameChoice SelectBannerName(Empire empire, Kingdom kingdom)
    {
        var extra = kingdom.GetOrCreate();
        string banner = extra.rebel_banner_name?.Trim();
        if (string.IsNullOrWhiteSpace(banner)) return null;
        extra.rebel_banner_name = "";
        EmpireCore bannerCore = EmpireCoreManager.Get(extra.rebel_banner_core_id);
        extra.rebel_banner_core_id = -1L;
        string prefix = "";
        if (NameInUse(banner, empire))
        {
            City seat = bannerCore == null ? null : EmpireCoreManager.GetRepresentativeCity(bannerCore);
            EmpireCore here = kingdom.capital?.GetEmpireCore();
            prefix = here != null && here == bannerCore || seat == null || kingdom.capital == null
                ? RealmNameRules.Later
                : RealmNameRules.Direction4(seat.city_center.x, seat.city_center.y,
                    kingdom.capital.city_center.x, kingdom.capital.city_center.y) ?? RealmNameRules.Later;
        }
        empire.data.directPre = prefix;
        empire.data.founding_prefix = prefix;
        empire.data.name_source = SourceRebelBanner;
        LogService.LogInfo($"[EmpireCraft][选定国号] 起义军 {kingdom.GetKingdomName()} 打前朝旗号称帝，号 {banner}{(prefix.Length > 0 ? "，冠 " + prefix : "")}");
        return new EmpireFoundingNameChoice(banner, EmpireFoundingNameSource.RebelBanner);
    }

    // 起事(KingdomExtension.StartLocalRebelling，地方叛乱)：三成打前朝旗号，号称"复某军"
    public static void TryRaiseBanner(Kingdom rebels, Empire origin)
    {
        if (!Enabled || rebels?.data == null || rebels.HasMainTitle() || !IsHuaxia(rebels) || rebels.capital == null) return;
        if (UnityEngine.Random.value >= RebelBannerChance) return;
        (string name, long coreId) = FormerDynasty(TianxiaSystem.LeadOf(rebels.capital), origin);
        if (string.IsNullOrWhiteSpace(name)) return;
        var extra = rebels.GetOrCreate();
        extra.rebel_banner_name = name;
        extra.rebel_banner_core_id = coreId;
        rebels.data.name = string.Format(LM.Get("rebel_banner_army"), name).UseLocalizedNameSeparator();
        LogService.LogInfo($"[EmpireCraft][起义旗号] {rebels.capital.GetCityName()} 起事，号 {rebels.data.name}");
    }

    // 前朝：本天下当前在位王朝之前、已亡的那一朝(最近亡的)，名字与在位王朝不同、眼下没人在用
    private static (string name, long coreId) FormerDynasty(EmpireCore lead, Empire reigning)
    {
        if (lead == null || ModClass.EMPIRE_MANAGER == null) return ("", -1L);
        string current = reigning?.GetEmpireName() ?? "";
        SpecificClan best = null;
        EmpireCore bestCore = null;
        SpecificClan[] clans;
        try
        {
            clans = SpecificClanManager._specificClans.ToArray();
        }
        catch
        {
            return ("", -1L);
        }
        foreach (SpecificClan clan in clans)
        {
            if (clan == null || !clan.HasHistoryEmpire() || clan.empire_fall_timestamp < 0d) continue;
            if (best != null && clan.empire_fall_timestamp <= best.empire_fall_timestamp) continue;
            string name = EmpireCoreManager.NormalizeCoreName(clan.empire_name);
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, current, StringComparison.Ordinal) || NameInUse(name, null)) continue;
            EmpireCore core = AncestralCore(clan);
            if (core == null || TianxiaSystem.Lead(core) != lead) continue;
            if (ModClass.EMPIRE_MANAGER.Any(empire => empire?.data != null && !empire.isRekt() && !empire.IsArchived() &&
                                                      SpecificClanManager.SameLineage(empire.EmpireSpecificClan, clan))) continue;
            best = clan;
            bestCore = core;
        }
        return best == null ? ("", -1L) : (EmpireCoreManager.NormalizeCoreName(best.empire_name), bestCore?.id ?? -1L);
    }

    // 帝国内部改朝换代(权臣篡位、外戚代立、太后另立……，见 Empire.NewEmperor)：用篡位者的封国名(他持有的王国法理)，没有封国用美号；
    // 宗室复辟(篡位者的宗族建过帝国)沿用旧号，不走这里。返回 false = 不是华夏、照旧规则
    public static bool NameUsurper(Empire empire, Actor usurper)
    {
        if (!Enabled || empire?.data == null || empire.CoreKingdom == null || !IsHuaxia(empire) ||
            !UsesFoundingRules(empire.CoreKingdom)) return false;
        string before = empire.GetEmpireFullName();
        string fief = "";
        // 封国：他持有的王国法理(即位时接过来的京畿主法理、都城法理不算)
        KingdomTitle seatTitle = empire.CoreKingdom.GetMainTitle(), capitalTitle = empire.CoreKingdom.capital?.GetTitle();
        foreach (long titleId in usurper?.GetOwnedTitle()?.ToList() ?? new List<long>())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER?.get(titleId);
            if (title == null || title == seatTitle || title == capitalTitle) continue;
            fief = EmpireCoreManager.NormalizeCoreName(title.data?.name);
            if (!string.IsNullOrWhiteSpace(fief)) break;
        }
        if (string.IsNullOrWhiteSpace(fief) && usurper?.kingdom != null && usurper.kingdom != empire.CoreKingdom)
            fief = EmpireCoreManager.NormalizeCoreName(usurper.kingdom.GetMainTitle()?.data?.name);
        EmpireCore lead = TianxiaSystem.Lead(EmpireCoreManager.Get(empire));
        empire.data.founding_prefix = "";
        if (!string.IsNullOrWhiteSpace(fief) && !string.Equals(fief, empire.GetEmpireName(), StringComparison.Ordinal))
        {
            MarkReuse(empire, empire.CoreKingdom, fief, lead);
            empire.data.name_source = SourceUsurper;
        }
        else
        {
            fief = PickGrandName(CultureService.GetRealmCulture(empire.CoreKingdom), lead, usurper?.id ?? empire.id);
            if (string.IsNullOrWhiteSpace(fief)) return false;
            empire.data.directPre = "";
            empire.data.name_source = SourceGrandName;
        }
        empire.SetEmpireName(fief);
        EventRecorder.Record(empire, string.Format(LM.Get("realm_usurper_name_history"), usurper?.getName() ?? "", before,
            empire.GetEmpireFullName()));
        LogService.LogInfo($"[EmpireCraft][选定国号] {before} 改朝，{usurper?.getName()} 定号 {empire.GetEmpireFullName()}");
        return true;
    }

    // 外族入主采纳中原制度(兼用、从俗，见 CompositeEmpireService.TryAdoptInstitutionalCultureName)：改用美号
    public static string ConquestGrandName(Empire empire, string institutionalCulture)
    {
        if (!Enabled || empire?.data == null || InstitutionSystem.GetCultureLine(institutionalCulture) != HuaxiaLine) return null;
        string grand = PickGrandName(institutionalCulture, TianxiaSystem.Lead(EmpireCoreManager.Get(empire)), empire.id);
        if (string.IsNullOrWhiteSpace(grand)) return null;
        empire.data.directPre = "";
        empire.data.name_source = SourceGrandName;
        return grand;
    }
}
