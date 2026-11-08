using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 教廷页上的按钮(上帝视角直接干预)：立即加冕、绝罚或解除、号召圣战、创立骑士团
public static partial class PapacySystem
{
    // 加冕不了的缘由(语言键，格式参数为帝国全名)；能加冕返回 null。
    // 教廷页只要西方、有圣地就显示，加冕却还要君主制：以前古典共和(元首制)、帝位空缺、已加冕时按钮点了毫无动静
    public static string CrownBlocker(Empire empire)
    {
        if (!Applies(empire)) return "papacy_block_regime";
        if (IsHolySee(empire)) return "papacy_block_holy_see";
        if (empire.Emperor == null) return "papacy_crown_block_vacant";
        if (IsExcommunicated(empire)) return "papacy_crown_block_banned";
        if (IsCrowned(empire)) return "papacy_crown_block_done";
        return null;
    }

    public static bool ActCrown(Empire empire)
    {
        if (CrownBlocker(empire) != null) return false;
        Religion religion = ReligionOf(empire);
        Kingdom papacy = Papacy(religion);
        bool self = BrokeWithRome(empire) || papacy == null;
        Crown(empire, State(empire), empire.Emperor, self, papacy);
        if (!self) RealmNamingSystem.OnPapalCoronation(empire, RealmNamingSystem.CoronationVia(empire, papacy, false));
        return true;
    }

    // 逼冕得手(见 RealmNamingSystem.Universal)：解除这场战争招来的绝罚，没由教宗加冕过的由教宗加冕
    public static bool CrownByForce(Empire empire)
    {
        if (!Applies(empire) || IsHolySee(empire) || empire.Emperor == null || BrokeWithRome(empire)) return false;
        Kingdom papacy = Papacy(ReligionOf(empire));
        if (papacy == null) return false;
        PapalRelationState state = State(empire);
        if (IsExcommunicated(empire)) Lift(empire, state, LM.Get("realm_coerce_ban_lifted_history"));
        if (!IsCrowned(empire) || state.self_crowned) Crown(empire, state, empire.Emperor, self: false, papacy);
        return true;
    }

    // 神圣之名(用户 2026-10-27 要"让帝国加上神圣前缀的按钮"，并补上加冕称神圣帝国的事件；以前只有神授君权决议能给，
    // 那条决议要教宗国就在本帝国里，又读空的帝国国教，几乎从不触发)：
    //   · 授名：国号加"神圣"前缀(剧本开局的神圣罗马另先由教宗加冕)；天下之名关着时国号取教廷都城的法理(原型"神圣罗马")；
    //     史书、世界日志记"某自称承天受命，为某某人的皇帝，某某自此称神圣某某"；
    //   · 去名：前缀还原为授名前的(方位、"大"等)；
    //   · 神授君权诉求(见 TempFac_神授君权)与教廷页的按钮共用这里。
    //   · 用户 2026-11-08 按史实改：神圣不是教宗授的——1157 年巴巴罗萨与教廷争权时才自称"神圣帝国"，帝国的神圣直接来自上帝、
    //     不靠教宗，那时罗马之名已承袭三百多年。所以教宗加冕不再顺带授神圣；持普世之名满 HolyYears 年、又与教廷正面相争过
    //     (叙任权之争、遭绝罚，见 HadClash)的，由神授君权诉求自称神圣。上帝视角的按钮不看这两条。
    public const int HolyYears = 100;

    public static bool HasHolyName(Empire empire) =>
        empire?.data != null && empire.data.directPre == OverallHelperFunc.HolyPrefixKey;

    // 授不了神圣之名的缘由(同 CrownBlocker)；decree = 上帝视角的按钮，不看持名年限与争权由头。
    // 神圣依附于普世之名(用户 2026-10-27 截图"神圣北海帝国"："没有了教宗授予的天下之名那么神圣也不会存在，建议收回")：
    // 没持有普世之名(或因绝罚暂停)的称不得神圣；收回普世之名时神圣一并去掉(见 RealmNamingSystem.RevokeUniversal)，
    // 每年核对时没有普世之名还挂着神圣的也去掉。天下之名规则关着时不看普世之名，持名年限改看教宗加冕过。
    // 神圣全世界只有一个：罗马系各文化只认一座圣城、一个教廷；已有一家称神圣就不再有
    public static string HolyBlocker(Empire empire, bool decree = false)
    {
        if (!Applies(empire)) return "papacy_block_regime";
        if (IsHolySee(empire)) return "papacy_block_holy_see";
        if (empire.Emperor == null) return "papacy_crown_block_vacant";
        if (IsExcommunicated(empire)) return "papacy_crown_block_banned";
        Religion religion = ReligionOf(empire);
        if (religion != TheChurch()) return "papacy_holy_block_church";
        if (BrokeWithRome(empire) || Papacy(religion) == null) return "papacy_holy_block_no_see";
        if (RealmNamingSystem.Enabled && !RealmNamingSystem.HoldsUniversal(empire)) return "papacy_holy_block_universal";
        if (OtherHolyEmpire(empire) != null) return "papacy_holy_block_taken";
        if (decree) return null;
        if (!HeldLongEnough(empire)) return "papacy_holy_block_young";
        if (!HadClash(empire)) return "papacy_holy_block_clash";
        return null;
    }

    // 持普世之名满 HolyYears 年(天下之名规则关着时：教宗加冕过满这些年)
    private static bool HeldLongEnough(Empire empire)
    {
        PapalRelationState state = State(empire);
        double since = RealmNamingSystem.Enabled ? state?.universal_at ?? -1d : state?.crowned_at ?? -1d;
        if (!RealmNamingSystem.Enabled && (state == null || state.self_crowned)) return false;
        return since >= 0d && World.world.getCurWorldTime() >= since && Date.getYearsSince(since) >= HolyYears;
    }

    // 那个唯一的教会：奉普世教会、圣地还在、圣地属罗马系文化(或教会本就属罗马天下)的宗教里，罗马天下的优先，再是没有母宗教的，
    // 再是立教最早的。别的天下自立的教会排在后面，分出去的教派没有教廷(见 HasSee)，不算
    public static Religion TheChurch()
    {
        Religion best = null;
        (int, int, double, long) rank = default;
        if (World.world?.religions == null) return null;
        foreach (Religion religion in World.world.religions.list)
        {
            if (religion?.data == null || !ReligionFaith.IsPapal(religion) || !HasSee(religion)) continue;
            City city = HolyCity(religion);
            if (city == null) continue;
            bool romanSphere = ReligionSphere.SphereOf(religion) == Line;
            if (!romanSphere && InstitutionSystem.GetCultureLine(CultureService.GetMainCulture(city, initialize: false)) != Line) continue;
            (int, int, double, long) mine = (romanSphere ? 0 : 1, religion.GetOrCreate().ParentReligionId >= 0 ? 1 : 0, religion.data.created_time, religion.id);
            if (best != null && mine.CompareTo(rank) >= 0) continue;
            best = religion;
            rank = mine;
        }
        return best;
    }

    // 别家称着神圣的帝国
    private static Empire OtherHolyEmpire(Empire empire)
    {
        if (ModClass.EMPIRE_MANAGER == null) return null;
        foreach (Empire other in ModClass.EMPIRE_MANAGER)
            if (other != empire && other?.data != null && !other.isRekt() && !other.IsArchived() && HasHolyName(other)) return other;
        return null;
    }

    // 已经称神圣的还算不算数(每年核对，见 RealmNamingSystem.YearlyUniversal)：不是那个教会的去掉；两家以上的留国力大的。
    // 教廷一时不在不收回
    public static bool HolyStays(Empire empire)
    {
        if (ReligionOf(empire) != TheChurch()) return false;
        Empire other = OtherHolyEmpire(empire);
        if (other == null || ReligionOf(other) != TheChurch()) return true;
        (double, long) mine = (WarSituation.Power(empire.CoreKingdom), -empire.id), theirs = (WarSituation.Power(other.CoreKingdom), -other.id);
        return mine.CompareTo(theirs) >= 0;
    }

    // 教廷页按钮：有神圣之名的去掉，没有的授予
    public static bool ActHoly(Empire empire)
    {
        if (HasHolyName(empire))
        {
            RevokeHoly(empire);
            return true;
        }
        if (HolyBlocker(empire, decree: true) != null) return false;
        GrantHoly(empire);
        return true;
    }

    // 正在授神圣之名(防重入)
    internal static bool GrantingHoly { get; private set; }

    // crownFirst = 剧本开局(神圣罗马开局即神圣)：没由教宗加冕的先由教宗加冕。别处称神圣不牵涉加冕(神圣是帝国自称的)
    public static void GrantHoly(Empire empire, bool crownFirst = false)
    {
        if (GrantingHoly) return;
        GrantingHoly = true;
        try { GrantHolyCore(empire, crownFirst); }
        finally { GrantingHoly = false; }
    }

    private static void GrantHolyCore(Empire empire, bool crownFirst)
    {
        Religion religion = ReligionOf(empire);
        Kingdom papacy = Papacy(religion);
        PapalRelationState state = State(empire);
        Actor emperor = empire.Emperor;
        bool self = BrokeWithRome(empire) || papacy == null;
        if (crownFirst && (!IsCrowned(empire) || state.self_crowned && !self))
        {
            Crown(empire, state, emperor, self, papacy);
            if (!self) RealmNamingSystem.OnPapalCoronation(empire, RealmNamingSystem.CoronationVia(empire, papacy, false));
        }
        string before = empire.GetEmpireFullName();
        string people = RealmNamingSystem.PeopleName(empire);
        if (!HasHolyName(empire)) state.holy_previous_prefix = empire.data.directPre ?? "";
        empire.data.directPre = OverallHelperFunc.HolyPrefixKey;
        string seatName = RealmNamingSystem.Enabled ? null : papacy?.capital?.GetTitle()?.name;
        empire.SetEmpireName(string.IsNullOrWhiteSpace(seatName) ? empire.GetEmpireName() : seatName);
        // 帝国自称神圣(承天受命、不假教宗)
        string text = string.Format(LM.Get("holy_name_self_history"), emperor.getName(), people, before, empire.GetEmpireFullName());
        EventRecorder.Record(empire, text);
        emperor.RecordPersonalHistory(text);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
        MonarchyLegitimacy.Invalidate(empire);
        NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][教廷] {before} 得神圣之名，称 {empire.GetEmpireFullName()}");
    }

    public static void RevokeHoly(Empire empire)
    {
        if (!HasHolyName(empire)) return;
        PapalRelationState state = State(empire);
        string before = empire.GetEmpireFullName();
        empire.data.directPre = state?.holy_previous_prefix ?? "";
        if (state != null) state.holy_previous_prefix = "";
        empire.SetEmpireName(empire.GetEmpireName());
        EventRecorder.Record(empire, string.Format(LM.Get("holy_name_revoked_history"), before, empire.GetEmpireFullName()));
        MonarchyLegitimacy.Invalidate(empire);
    }

    // 绝罚、解除绝罚做不了的缘由(同 CrownBlocker)
    public static string BanBlocker(Empire empire) =>
        !Applies(empire) ? "papacy_block_regime" : IsHolySee(empire) ? "papacy_block_holy_see" : null;

    public static bool ActToggleBan(Empire empire)
    {
        if (BanBlocker(empire) != null) return false;
        PapalRelationState state = State(empire);
        if (IsExcommunicated(empire))
        {
            Penance(empire, state, ReligionOf(empire), Papacy(ReligionOf(empire)));
            return true;
        }
        Excommunicate(empire, state, "papal_ban_reason_decree");
        return IsExcommunicated(empire);
    }

    public static bool ActCrusade(Empire empire)
    {
        Religion religion = ReligionOf(empire);
        // 教派、地方教会没有号召力(见 CallsHolyWar)
        if (religion == null || !CallsHolyWar(religion) || ActiveCrusade(religion) != null) return false;
        Kingdom papacy = Papacy(religion);
        Kingdom caller = papacy != null && !IsCaptive(religion) ? papacy : Strongest(religion);
        if (caller == null) return false;
        (Kingdom target, int kind) = FindTarget(religion, caller, 100f);
        if (target == null || target == caller) return false;
        Declare(religion, caller, target, kind);
        return ActiveCrusade(religion) != null;
    }

    public static bool ActFoundOrder(Empire empire)
    {
        Religion religion = ReligionOf(empire);
        if (religion == null || Orders(religion).Count() >= MaxOrders || empire.CoreKingdom == null) return false;
        int before = Orders(religion).Count();
        var data = religion.GetOrCreate();
        data.Orders ??= new global::System.Collections.Generic.List<KnightOrderData>();
        int index = data.Orders.Count % 3 + 1;
        Kingdom host = empire.CoreKingdom;
        string place = index == 1 ? CrusadeCity(religion)?.GetCityName() ?? host.capital?.GetCityName()
            : host.capital?.GetCityName() ?? host.GetKingdomName();
        var order = new KnightOrderData
        {
            id = HelperFunc.OverallHelperFunc.IdGenerator.NextId(),
            name = string.Format(LM.Get("knight_order_name_" + index), place),
            host_kingdom_id = host.id,
            founded_at = World.world.getCurWorldTime()
        };
        data.Orders.Add(order);
        PickGrandMaster(order, host);
        RecordReligion(host, string.Format(LM.Get("knight_order_founded_history"), order.name, host.GetKingdomName()));
        return Orders(religion).Count() > before;
    }
}
