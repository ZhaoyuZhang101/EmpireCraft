using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 教廷页上的按钮(上帝视角直接干预)：立即加冕、绝罚或解除、号召圣战、创立骑士团
public static partial class PapacySystem
{
    public static bool ActCrown(Empire empire)
    {
        if (!Applies(empire) || IsCrowned(empire) || IsExcommunicated(empire) || empire.Emperor == null) return false;
        Religion religion = ReligionOf(empire);
        Kingdom papacy = Papacy(religion);
        bool self = BrokeWithRome(empire) || papacy == null;
        Crown(empire, State(empire), empire.Emperor, self, papacy);
        return true;
    }

    public static bool ActToggleBan(Empire empire)
    {
        if (!Applies(empire) || IsHolySee(empire)) return false;
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
