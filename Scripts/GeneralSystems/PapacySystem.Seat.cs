using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 君主亲自办的教廷事务(手机终端的教廷页，见 TerminalHost.Papacy)。教廷页上的按钮(PapacySystem.Actions)是上帝直接干预，
// 君主办不得；这里照年度结算的口径，只是由君主自己定什么时候办、不掷骰子：
//   · 皇帝请冕：同 Coronation——脱离罗马或教廷不在的自行加冕，教廷在自家的直接加冕，封建制帝国先罗马远征，
//     与教廷交战、教宗正统太低的不成；献礼照年度经常收入的一成；
//   · 皇帝悔罪：同卡诺莎(Penance)，献上年度经常收入的一半，教权 +10，放弃叙任权之争；
//   · 教宗绝罚、赦免一位皇帝：教宗谕令；教宗受挟(教廷之囚)时谕令不出门，教权跌破绝罚自然失效的线也不成；
//   · 教宗号召圣战：同 TryCrusade 的目标与冷却，本国在打仗的也能号召(年度结算里在打仗就不号召，那是 AI 的分寸)；
//   · 教宗创立骑士团：同圣战告捷后的成立，只是不看告捷；至多 MaxOrders 个，东道国是教廷所在国
// 都回结果码(TerminalHost.Papacy 照它出字)：ok 办成；其余是没办成的缘由
public static partial class PapacySystem
{
    public const string SeatOk = "ok", SeatRomzug = "romzug", SeatSelf = "self";

    // 这一国的君主是哪个西方宗教的教宗(教廷所在国之君、教廷没受挟)；不是回 null
    public static Religion PopeOf(Kingdom realm)
    {
        Religion religion = realm?.religion;
        if (religion?.data == null || realm.king == null) return null;
        return Papacy(religion) == realm && Pope(religion) == realm.king && HolyCity(religion) != null ? religion : null;
    }

    // 皇帝请冕
    public static string SeatCoronation(Empire empire)
    {
        if (empire?.data == null || !Applies(empire)) return "regime";
        Actor emperor = empire.Emperor;
        if (emperor == null || !emperor.isAlive()) return "regime";
        if (IsCrowned(empire)) return "crowned";
        if (IsExcommunicated(empire)) return "banned";
        Religion religion = ReligionOf(empire);
        PapalRelationState state = State(empire);
        Kingdom core = empire.CoreKingdom, papacy = Papacy(religion);
        bool antipope = FollowsAntipope(empire);
        if (BrokeWithRome(empire) || papacy == null && !antipope)
        {
            Crown(empire, state, emperor, self: true, null);
            return SeatSelf;
        }
        if (antipope)
        {
            AntipopeCoronation(empire, state, religion, emperor);
            return IsCrowned(empire) ? SeatOk : "refused";
        }
        if (papacy == core || papacy.GetEmpire() == empire && !HolyRomanSystem.HasDiet(empire))
        {
            Crown(empire, state, emperor, self: false, papacy);
            RealmNamingSystem.OnPapalCoronation(empire, RealmNamingSystem.CoronationVia(empire, papacy, false));
            return SeatOk;
        }
        if (PopeLegitimacy.Applies(religion) && PopeLegitimacy.Get(religion) < PopeLegitimacy.CoronationBelow) return "pope_weak";
        if (papacy.isInWarWith(core)) return "at_war";
        int romzug = HolyRomanSystem.Romzug(empire);
        if (romzug == HolyRomanSystem.RomzugTravelling) return "travelling";
        if (romzug != HolyRomanSystem.RomzugArrived && HolyRomanSystem.StartRomzug(empire, papacy)) return SeatRomzug;
        int gift = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * CoronationGift));
        if (gift > 0)
        {
            core.SubMoney(gift, TreasuryCategory.Tithe);
            papacy.AddMoney(gift, TreasuryCategory.Tithe);
        }
        Crown(empire, state, emperor, self: false, papacy);
        RealmNamingSystem.OnPapalCoronation(empire, RealmNamingSystem.CoronationVia(empire, papacy, romzug == HolyRomanSystem.RomzugArrived));
        return SeatOk;
    }

    // 皇帝悔罪
    public static string SeatPenance(Empire empire)
    {
        if (empire?.data == null || !Applies(empire)) return "regime";
        if (!IsExcommunicated(empire)) return "not_banned";
        Religion religion = ReligionOf(empire);
        Penance(empire, State(empire), religion, ObeyedSee(empire, religion));
        return SeatOk;
    }

    // 教宗可以绝罚、赦免的皇帝：奉本宗、受教廷管的西方帝国(教廷就在自家的、脱离罗马的不算)
    public static List<Empire> SeatBannable(Religion religion)
    {
        var list = new List<Empire>();
        if (religion?.data == null || ModClass.EMPIRE_MANAGER == null) return list;
        Kingdom papacy = Papacy(religion);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire?.data == null || empire.isRekt() || !Applies(empire) || IsHolySee(empire) || BrokeWithRome(empire)) continue;
            if (ReligionOf(empire) != religion || empire.CoreKingdom == papacy || papacy?.GetEmpire() == empire) continue;
            if (empire.Emperor == null || !empire.Emperor.isAlive()) continue;
            list.Add(empire);
        }
        return list;
    }

    // 教宗绝罚或赦免一位皇帝(在罚中就赦免，没罚就绝罚)
    public static string SeatToggleBan(Religion religion, Empire empire)
    {
        if (religion?.data == null || !SeatBannable(religion).Contains(empire)) return "target";
        if (IsCaptive(religion)) return "captive";
        PapalRelationState state = State(empire);
        if (IsExcommunicated(empire))
        {
            Lift(empire, state, string.Format(LM.Get("papal_ban_pardoned_history"), Pope(religion)?.getName() ?? "",
                empire.Emperor?.getName() ?? "", empire.GetEmpireFullName()));
            return SeatOk;
        }
        if (Authority(religion) < AuthorityLapse) return "authority";
        Excommunicate(empire, state, "papal_ban_reason_decree");
        return IsExcommunicated(empire) ? SeatOk : "refused";
    }

    // 教宗号召圣战：眼下能不能(能就回 null)
    public static string SeatCrusadeBlocker(Religion religion)
    {
        if (religion?.data == null || !CallsHolyWar(religion)) return "no_holy_war";
        if (IsCaptive(religion)) return "captive";
        if (ActiveCrusade(religion) != null) return "crusade_on";
        if (World.world.getCurWorldTime() < religion.GetOrCreate().CrusadeCooldownUntil) return "cooldown";
        return null;
    }

    public static string SeatCrusade(Religion religion)
    {
        if (SeatCrusadeBlocker(religion) is { } blocked) return blocked;
        Kingdom papacy = Papacy(religion);
        (Kingdom target, int kind) = FindTarget(religion, papacy, Authority(religion));
        if (target == null || target == papacy) return "no_target";
        // 远距离不开战(同 Declare)；战端开不了(停战、法则)的也不成
        if (!StraitReach.Within(papacy, target)) return "far";
        Declare(religion, papacy, target, kind);
        return ActiveCrusade(religion) != null ? SeatOk : "no_war";
    }

    public static string SeatOrderBlocker(Religion religion) =>
        religion?.data == null || !ReligionFaith.FoundsOrders(religion) ? "no_orders"
        : Orders(religion).Count() >= MaxOrders ? "orders_full"
        : IsCaptive(religion) ? "captive" : null;

    public static string SeatFoundOrder(Religion religion)
    {
        if (SeatOrderBlocker(religion) is { } blocked) return blocked;
        Kingdom host = Papacy(religion);
        int before = Orders(religion).Count();
        var data = religion.GetOrCreate();
        data.Orders ??= new List<KnightOrderData>();
        int index = data.Orders.Count % 3 + 1;
        string place = index == 1 ? CrusadeCity(religion)?.GetCityName() ?? host.capital?.GetCityName()
            : host.capital?.GetCityName() ?? host.GetKingdomName();
        var order = new KnightOrderData
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            name = string.Format(LM.Get("knight_order_name_" + index), place),
            host_kingdom_id = host.id,
            founded_at = World.world.getCurWorldTime()
        };
        data.Orders.Add(order);
        PickGrandMaster(order, host);
        string text = string.Format(LM.Get("knight_order_founded_history"), order.name, host.GetKingdomName());
        RecordReligion(host, text);
        TranslateHelper.LogEventMessage(text, host);
        ReligionChronicle.Record(religion, ReligionChronicle.KindHolyWar, text);
        return Orders(religion).Count() > before ? SeatOk : "refused";
    }
}
