using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
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
        return true;
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
