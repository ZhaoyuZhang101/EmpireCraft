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
    //   · 授名：没由教宗加冕过的先加冕(教廷不在、已脱离罗马的自行加冕)，国号加"神圣"前缀；天下之名关着时国号取教廷都城的法理
    //     (原型"神圣罗马")；史书、世界日志记"教宗为某加冕，尊为某某人的皇帝，某某自此称神圣某某"；
    //   · 去名：前缀还原为授名前的(方位、"大"等)；
    //   · 神授君权决议(见 TempFac_神授君权)与教廷页的按钮共用这里；持有普世之名的帝国由教宗加冕时也自动授(见 RealmNamingSystem.OnPapalCoronation)。
    public static bool HasHolyName(Empire empire) =>
        empire?.data != null && empire.data.directPre == OverallHelperFunc.HolyPrefixKey;

    // 授不了神圣之名的缘由(同 CrownBlocker)。
    // 神圣依附于普世之名(用户 2026-10-27 截图"神圣北海帝国"："没有了教宗授予的天下之名那么神圣也不会存在，建议收回")：
    // 没持有教宗授予的普世之名(或因绝罚暂停)的称不得神圣——神授君权决议、教廷页按钮都按这里；收回普世之名时神圣一并去掉
    // (见 RealmNamingSystem.RevokeUniversal)，每年核对时没有普世之名还挂着神圣的也去掉。天下之名规则关着时照旧不要求
    public static string HolyBlocker(Empire empire)
    {
        if (!Applies(empire)) return "papacy_block_regime";
        if (IsHolySee(empire)) return "papacy_block_holy_see";
        if (empire.Emperor == null) return "papacy_crown_block_vacant";
        if (IsExcommunicated(empire)) return "papacy_crown_block_banned";
        if (RealmNamingSystem.Enabled && !RealmNamingSystem.HoldsUniversal(empire)) return "papacy_holy_block_universal";
        return null;
    }

    // 教廷页按钮：有神圣之名的去掉，没有的授予
    public static bool ActHoly(Empire empire)
    {
        if (HasHolyName(empire))
        {
            RevokeHoly(empire);
            return true;
        }
        if (HolyBlocker(empire) != null) return false;
        GrantHoly(empire);
        return true;
    }

    // 正在授神圣之名(授名途中顺带的加冕不再触发自动授名，见 RealmNamingSystem.OnPapalCoronation)
    internal static bool GrantingHoly { get; private set; }

    public static void GrantHoly(Empire empire)
    {
        if (GrantingHoly) return;
        GrantingHoly = true;
        try { GrantHolyCore(empire); }
        finally { GrantingHoly = false; }
    }

    private static void GrantHolyCore(Empire empire)
    {
        Religion religion = ReligionOf(empire);
        Kingdom papacy = Papacy(religion);
        PapalRelationState state = State(empire);
        Actor emperor = empire.Emperor;
        bool self = BrokeWithRome(empire) || papacy == null;
        if (!IsCrowned(empire) || state.self_crowned && !self)
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
        Actor pope = Pope(religion);
        string text = self || pope == null
            ? string.Format(LM.Get("holy_name_self_history"), emperor.getName(), people, before, empire.GetEmpireFullName())
            : string.Format(LM.Get("holy_name_granted_history"), pope.getName(), emperor.getName(), people, before,
                empire.GetEmpireFullName());
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
        if (religion == null || ActiveCrusade(religion) != null) return false;
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
        string place = index == 1 ? religion.GetCity()?.GetCityName() ?? host.capital?.GetCityName()
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
