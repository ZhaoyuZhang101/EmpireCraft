using System;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 码头造船的花费(见 EmpireCraftBehCheckBoat)。原版城里的金子是百姓缴税时留给本城的那一半(BehActorGiveTax 存进仓库)，
// 码头造船就花它(渔船 5、商船 10、运输船 20 金，另要木头、金属)；开了国库(TreasurySystem)以后这份税进了城库，仓里几乎
// 没有金子，无小人、纯净地图下大半港口的码头一艘船也造不起。
// 改为：木头、石头、金属照旧从仓里拿；金子仓里不够时，缺的那部分由城库按金价(PopulationEconomySystem.UnitValue)付，
// 只动可支配的钱(留足保底，见 TreasurySystem.DiscretionaryFunds)。仓里够的、没开国库的城照原版。
public static class BoatFunding
{
    // 造这艘船要从城库付多少钱：0 = 照原版从仓里拿齐；-1 = 造不起
    public static int Plan(ConstructionCost cost, int wood, int stone, int metals, int gold, bool treasury, float goldValue,
        int funds)
    {
        if (cost == null) return 0;
        if (wood < cost.wood || stone < cost.stone || metals < cost.common_metals) return -1;
        int shortGold = Math.Max(0, cost.gold - Math.Max(0, gold));
        if (shortGold == 0) return 0;
        if (!treasury) return -1;
        int money = Math.Max(1, (int)Math.Ceiling(shortGold * Math.Max(0f, goldValue)));
        return money <= Math.Max(0, funds) ? money : -1;
    }

    public static int Plan(City city, ConstructionCost cost)
    {
        if (city == null) return -1;
        // 仓里够(含原版"城市资源无限"调试项)：照原版
        if (cost == null || city.hasEnoughResourcesFor(cost)) return 0;
        bool treasury = TreasurySystem.Enabled(city);
        return Plan(cost, city.getResourcesAmount("wood"), city.getResourcesAmount("stone"),
            city.getResourcesAmount("common_metals"), city.getResourcesAmount("gold"), treasury,
            treasury ? PopulationEconomySystem.UnitValue(city, "gold") : 0f,
            treasury ? TreasurySystem.DiscretionaryFunds(city) : 0);
    }

    // 照 Plan 的结果付：money 为 0 时同原版；否则仓里的金子全拿上，缺的从城库付
    public static void Pay(City city, ConstructionCost cost, int money)
    {
        if (city == null || cost == null) return;
        if (money <= 0)
        {
            city.spendResourcesForBuildingAsset(cost);
            return;
        }
        city.takeResource("wood", cost.wood);
        city.takeResource("stone", cost.stone);
        city.takeResource("common_metals", cost.common_metals);
        int gold = Math.Min(cost.gold, Math.Max(0, city.getResourcesAmount("gold")));
        if (gold > 0) city.takeResource("gold", gold);
        city.SubMoney(money, TreasuryCategory.Construction);
    }
}
