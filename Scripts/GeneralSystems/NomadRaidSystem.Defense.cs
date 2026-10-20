using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 定居一方的应对(用户问答三项都要)：
//   · 花钱买和：劫掠开打满一个月起，遭劫之国(帝国的边地由中央拿主意、出钱)每月按 SteppeRules.BuyOffChance 掷一次——
//     已被洗劫的城越多、对方越强、手上还有别的仗越想买，自己强出一倍不买；开国雄主、中兴雄主在位不买。三种买法：
//       - 和亲岁币：买方是君主制帝国、来的是联盟、盟主还没收谁的岁币时 35% —— 宗室女嫁给盟主(找不到人也照样议和)，
//         从此每年送岁币(二十年，两国互不开战，见 FrontierRelations、WarGate)；
//       - 开互市：买方是帝国时 40% —— 与出兵各部开互市 MarketTreatyYears 年(期内遭劫过也照常通市)，另给半份买和金；
//       - 金帛：给一份买和金 = max(20，遭劫之国户数的四分之一，买方国库一成)，出不起就不买。
//     买和金按出兵多少分给各部，当场议和退兵、联盟解散(算得手，盟主照样记威望)；
//   · 长城关隘：开战时量一量两边交界——守方边上有城墙的地块占比 ×0.7、交界的城里有完好关城 +0.3(至多 1)，即关隘之固；
//     选目标时打分打折(至多六折)，洗劫所得按 1 − 0.5×关隘之固打折，出兵各部正在进攻的军每月折损 WallAttrition×关隘之固；
//   · 屯田实边：二十年内施行过屯田实边的帝国，与外国接壤的边城守得住——遭洗劫时金帛只拿七成、粮只拿四分之一、
//     人口只掳一半，这座城只算抢了半座。
public static partial class NomadRaidSystem
{
    private const float WallAttrition = 0.03f, HeqinOdds = 0.35f, MarketOdds = 0.4f, TuntianYears = 20f;
    private const int MarketTreatyYears = 10, MinRansom = 20;
    private const float SteadfastMoney = 0.7f, SteadfastFood = 0.25f, SteadfastPeople = 0.5f;

    // 守方长城关隘之固(0~1)：交界地块里有墙的占比，交界的城里有没有完好的关城
    private static float Defense(List<Kingdom> members, Kingdom target)
    {
        var raiders = new HashSet<Kingdom>(members);
        int frontier = 0, walled = 0;
        bool fort = false;
        foreach (City city in target.cities)
        {
            if (city?.data == null || city.isRekt() || city.neighbours_kingdoms == null ||
                !city.neighbours_kingdoms.Any(raiders.Contains)) continue;
            if (!fort && RuralSettlementSystem.InCity(city).Any(settlement => settlement.fort && !settlement.cleared &&
                                                                             settlement.ruined_at < 0d)) fort = true;
            foreach (TileZone zone in city.border_zones)
            {
                if (zone == null) continue;
                bool facing = false, wall = TerrainResistance.HasWall(zone);
                foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                {
                    if (next?.city?.kingdom == null || !raiders.Contains(next.city.kingdom)) continue;
                    facing = true;
                    if (!wall && TerrainResistance.HasWall(next)) wall = true;
                }
                if (!facing) continue;
                frontier++;
                if (wall) walled++;
            }
        }
        return SteppeRules.Defense(frontier > 0 ? walled / (float)frontier : 0f, fort);
    }

    // 屯田实边的边城：所在帝国二十年内施行过屯田实边，城与外国接壤
    private static bool Steadfast(City city)
    {
        Empire empire = city?.kingdom?.GetEmpire();
        if (empire?.data?.claim_done_at == null ||
            !empire.data.claim_done_at.TryGetValue(TemporaryFactionType.屯田实边.ToString(), out double at) ||
            YearsSince(at) >= TuntianYears) return false;
        return city.neighbours_cities?.Any(other => other?.kingdom != null && other.kingdom.GetEmpire() != empire) == true;
    }

    static partial void MonthlyCourse(War war, RaidWarState state)
    {
        WallLosses(war, state);
        if (state.bought_off == 0 && WarSituation.WarYears(war) >= 1f / 12f) TryBuyOff(war, state);
    }

    // 关隘折损：出兵各部正在进攻的军
    private static void WallLosses(War war, RaidWarState state)
    {
        if (state.defense <= 0f || !CorpsSystem.Enabled) return;
        foreach (long id in state.members)
        {
            Kingdom member = LiveKingdom(id);
            if (member == null || !war.isAttacker(member)) continue;
            foreach (CorpsData corps in CorpsSystem.Of(member))
                if (corps.task == "attack" && corps.strength > 0f)
                    CorpsSystem.Casualties(corps, corps.strength * WallAttrition * state.defense);
        }
    }

    #region 买和

    private static void TryBuyOff(War war, RaidWarState state)
    {
        Kingdom target = war.main_defender;
        Kingdom payer = Realm(target);
        if (!Alive(target) || !Alive(payer) || !payer.hasKing() || RulerTraitSystem.FounderReigns(payer)) return;
        List<Kingdom> raiders = state.members.Select(LiveKingdom).Where(member => member != null && war.isAttacker(member)).ToList();
        if (raiders.Count == 0) return;
        double ratio = raiders.Sum(PowerOf) / Math.Max(1d, PowerOf(target));
        bool otherWars = payer.getWars().Any(other => other?.data != null && other != war && !other.hasEnded());
        if (UnityEngine.Random.value >= SteppeRules.BuyOffChance(state.sacked, ratio, otherWars)) return;
        int funds = StateSettlementSystem.DiscretionaryFunds(payer);
        int households = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(target) : target.units?.Count ?? 0;
        int ransom = Math.Max(MinRansom, Math.Max(households / 4, payer.GetMoney() / 10));
        Kingdom leader = LiveKingdom(state.leader_id);
        Empire empire = payer.IsEmpire() ? payer.GetEmpire() : null;
        string text;
        int paid;
        if (empire != null && leader != null && raiders.Count > 1 && !leader.HasGivenAlliance() &&
            RegimeManager.IsMonarchy(payer.GetRegime()?.type) && !RepublicSystem.IsRepublic(empire) &&
            UnityEngine.Random.value < HeqinOdds)
        {
            Actor bride = RoyalMarriageSystem.ArrangeHeqin(payer, leader);
            leader.JoinGivenAlliance(empire);
            paid = 0;
            state.bought_off = 3;
            text = bride != null
                ? string.Format(LM.Get("nomad_raid_heqin_history"), empire.GetEmpireName(), bride.getName(), state.leader_name)
                : string.Format(LM.Get("nomad_raid_heqin_plain_history"), empire.GetEmpireName(), state.leader_name);
        }
        else if (empire != null && funds >= ransom / 2 && UnityEngine.Random.value < MarketOdds)
        {
            double now = World.world.getCurWorldTime();
            foreach (Kingdom raider in raiders)
                foreach (Kingdom side in new[] { raider, Realm(raider) }.Distinct())
                {
                    SteppeState steppe = State(side);
                    steppe.market_treaty_with = payer.id;
                    steppe.market_treaty_at = now;
                }
            paid = ransom / 2;
            state.bought_off = 2;
            text = string.Format(LM.Get("nomad_raid_market_history"), payer.GetKingdomName(), state.leader_name, paid);
        }
        else if (funds >= ransom)
        {
            paid = ransom;
            state.bought_off = 1;
            text = string.Format(LM.Get("nomad_raid_ransom_history"), payer.GetKingdomName(), paid, state.leader_name);
        }
        else
        {
            LogService.LogInfo($"{LogTag} {payer.GetKingdomName()} 想向{state.leader_name}买和，出不起{ransom}(可动用{funds})");
            return;
        }
        if (paid > 0)
        {
            payer.SubMoney(paid, TreasuryCategory.Tribute);
            foreach ((Kingdom member, float share) in Shares(war, state))
                member.AddMoney(Mathf.FloorToInt(paid * share), TreasuryCategory.Tribute);
        }
        state.ransom = paid;
        Chronicle(text, raiders.Concat(new[] { target, payer }));
        LogService.LogInfo($"{LogTag} {payer.GetKingdomName()} 向{state.leader_name}买和(第{state.bought_off}种，" +
                           $"已被洗劫{state.sacked:0.#}城，对方/本方{ratio:0.00})：{text}");
        World.world.wars.endWar(war, WarWinner.Peace);
    }

    #endregion
}
