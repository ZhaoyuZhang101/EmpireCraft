using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 游牧与农耕的战力平衡诊断(用户 2026-10-20 选"积弊动真格""游牧来得快去得快"，先打日志再调数)：只记一方是游牧的战事，写进 Player.log——
//   · [游牧平衡·交战]：一座城一年至多一行，全图每月至多 MaxBattleLines 行；攻守双方的兵力、实效(空饷、武备、组织度)、
//     火力、统帅、补给、雄主，守方的守势加成、困守、团练、关城，地形与胜负比；
//   · [游牧平衡·兵源]：打仗的游牧国家、与游牧交战的国家，一国一年至多一行，全图每月至多 MaxRealmLines 行；
//     背景人口、兵源、军费系数、征兵比例、积弊、现有兵力。
public static class WarBalanceDiagnostics
{
    private const int MaxBattleLines = 6, MaxRealmLines = 8, CooldownMonths = 12;
    private const double SecondsPerMonth = 5d;

    private static readonly Dictionary<long, double> BattleLogged = new(), RealmLogged = new();
    private static double _monthAt = -1d;
    private static int _battleLines, _realmLines;

    private static bool Budget(bool battle)
    {
        double now = World.world.getCurWorldTime();
        if (_monthAt < 0d || now < _monthAt || now - _monthAt >= SecondsPerMonth)
        {
            _monthAt = now;
            _battleLines = _realmLines = 0;
            if (BattleLogged.Count > 4096) BattleLogged.Clear();
            if (RealmLogged.Count > 4096) RealmLogged.Clear();
        }
        return battle ? _battleLines < MaxBattleLines : _realmLines < MaxRealmLines;
    }

    private static bool Cooling(Dictionary<long, double> logged, long id) =>
        logged.TryGetValue(id, out double at) && World.world.getCurWorldTime() >= at && Date.getMonthsSince(at) < CooldownMonths;

    private static bool Nomad(Kingdom kingdom) => FrontierRelations.IsNomadRealm(FrontSystem.Realm(kingdom));

    // 本国(连同所在帝国的核心)在跟游牧打仗，或自己就是游牧
    private static bool NomadWar(Kingdom kingdom)
    {
        if (Nomad(kingdom)) return true;
        foreach (Kingdom side in new[] { kingdom, FrontSystem.Realm(kingdom) })
        {
            if (side?.data == null || side.isRekt()) continue;
            foreach (War war in side.getWars())
            {
                if (war?.data == null || war.hasEnded()) continue;
                Kingdom foe = war.isAttacker(side) ? war.main_defender : war.main_attacker;
                if (foe?.data != null && !foe.isRekt() && Nomad(foe)) return true;
            }
        }
        return false;
    }

    private static string Name(Kingdom kingdom) => kingdom?.data == null ? "?" : kingdom.GetKingdomName();

    // 每月征召时(CorpsSystem.Mobilize，打仗时)
    public static void Realm(Kingdom kingdom, float background, float pool, float budget, float levyShare)
    {
        try
        {
            if (kingdom?.data == null || World.world == null || !Budget(false) || Cooling(RealmLogged, kingdom.id) ||
                !NomadWar(kingdom)) return;
            RealmLogged[kingdom.id] = World.world.getCurWorldTime();
            _realmLines++;
            Layer.Empire empire = kingdom.GetEmpire();
            List<CorpsData> corps = CorpsSystem.Of(kingdom);
            float strength = corps.Sum(entry => Mathf.Max(0f, entry.strength)), full = corps.Sum(entry => Mathf.Max(0f, entry.full));
            LogService.LogInfo($"[EmpireCraft][游牧平衡·兵源] {Name(kingdom)}({CultureService.GetRealmCulture(kingdom)}" +
                               $"{(Nomad(kingdom) ? "·游牧" : "")}{(empire != null ? "·" + empire.GetEmpireName() : "")}) " +
                               $"背景人口{background:0} 征兵比例{levyShare:0.000} 武备兵源×{MaladySystem.LevyFactor(kingdom):0.00} " +
                               $"兵源{pool:0} 军费系数{budget:0.00} 实征{pool * budget:0} 现兵{strength:0}/{full:0} " +
                               $"积弊均{MaladySystem.Average(empire):0}/武备{MaladySystem.Get(empire, MaladySystem.Military):0} " +
                               $"正统{(empire != null ? empire.Legitimacy.ToString() : "—")} " +
                               $"思归×{NomadCycleSystem.HomesickLevy(kingdom):0.00} 自立门槛{NomadCycleSystem.SecessionGate(kingdom)}");
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][游牧平衡] 兵源诊断失败: {exception.Message}");
        }
    }

    // 每月交战结算后(FrontSystem.ResolveCity)
    public static void Battle(City city, Kingdom attacker, Kingdom owner, List<(Kingdom kingdom, CorpsData corps)> attackers,
        List<(Kingdom kingdom, CorpsData corps)> holders, FrontTerrain terrain, float ground, bool landing, bool breach,
        float remnant, float militia, float fort, float attack, float defend, float ratio)
    {
        try
        {
            if (city?.data == null || World.world == null || !Budget(true) || Cooling(BattleLogged, city.data.id)) return;
            bool nomadAttacker = Nomad(attacker), nomadOwner = Nomad(owner);
            if (!nomadAttacker && !nomadOwner) return;
            BattleLogged[city.data.id] = World.world.getCurWorldTime();
            _battleLines++;
            var text = new StringBuilder();
            text.Append($"[EmpireCraft][游牧平衡·交战] {city.GetCityName()} 攻:{Name(attacker)}{(nomadAttacker ? "·游牧" : "")} ");
            Side(text, attackers);
            text.Append($" → 攻势{attack:0} | 守:{Name(owner)}{(nomadOwner ? "·游牧" : "")} ");
            Side(text, holders);
            float entrench = Weighted(holders, corps => corps.entrench);
            text.Append($" 守势加成{(breach ? 1.1f + entrench * 0.5f : 1.25f + entrench):0.00} 困守×{remnant:0.00} 团练{militia:0} " +
                        $"关城×{fort:0.00} → 守势{defend:0} | 地形{TerrainSystem.Name(terrain)}×{ground:0.00}" +
                        $"{(landing ? " 登陆×0.70" : "")}{(breach ? " 重炮破城" : "")} 胜负比{ratio:0.00}");
            LogService.LogInfo(text.ToString());
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][游牧平衡] 交战诊断失败: {exception.Message}");
        }
    }

    private static void Side(StringBuilder text, List<(Kingdom kingdom, CorpsData corps)> side)
    {
        float men = side.Sum(entry => entry.corps.strength);
        float effective = side.Sum(entry => FrontSystem.Effective(entry.corps));
        text.Append($"兵{men:0} 实效{effective:0}(空饷×{Weighted(side, PalaceArmySystem.RealShare):0.00} " +
                    $"武备×{Weighted(side, MaladySystem.MilitaryFactor):0.00} 组织度{Weighted(side, corps => corps.org):0} " +
                    $"义军×{Weighted(side, CorpsSystem.InsurgentFactor):0.00}) " +
                    $"火力×{Weighted(side, entry => FrontSystem.Firepower(entry.kingdom, entry.corps)):0.00} " +
                    $"统帅×{Weighted(side, entry => WarCommandSystem.Factor(entry.kingdom)):0.00} " +
                    $"补给×{Weighted(side, LogisticsSystem.AttackFactor):0.00} " +
                    $"雄主×{Weighted(side, entry => RulerTraitSystem.FounderCombat(entry.kingdom)):0.00}");
    }

    private static float Weighted(List<(Kingdom kingdom, CorpsData corps)> side, global::System.Func<CorpsData, float> value) =>
        Weighted(side, entry => value(entry.corps));

    private static float Weighted(List<(Kingdom kingdom, CorpsData corps)> side,
        global::System.Func<(Kingdom kingdom, CorpsData corps), float> value)
    {
        float men = 0f, sum = 0f;
        foreach (var entry in side)
        {
            float weight = Mathf.Max(1f, entry.corps.strength);
            men += weight;
            sum += value(entry) * weight;
        }
        return men <= 0f ? 0f : sum / men;
    }
}
