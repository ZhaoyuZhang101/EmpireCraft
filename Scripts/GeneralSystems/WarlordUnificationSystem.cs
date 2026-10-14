using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 军阀时期的统一进程(参照北伐、解放战争)，每年随 UpdateCore 结算一次：
//   1. 内战打到底：同一法统内部的战争(IsCivilWar)不受"战争年限强制停战"和原版议和限制(见 WarPatch、attacker_stop_war)；
//   2. 中央主动统一：中央每年可对接壤的法统内对手发动统一战争，不受开战间隔限制，最多同时 MaxCivilWars 场；
//      与中央交战的一方地盘被中央占得越多越可能举国归附(每月，见 FounderCapitulation)；
//      地方武装之间(都不是中央)的混战打满四年各自罢兵(SettleWarlordFeuds)；
//   3. 民心所向：城市主流理念占比过半、统治者却是别的理念时，倒向代表该理念的政府——
//      接壤就起义归附，不接壤就揭竿而起成为该理念的地方武装(之后由同理念政府吸引归附)；
//   4. 民心即正统：政府的正统每年向"本理念在法统内的支持率"靠拢；
//   5. 大势所趋：中央控制法统过半城市后，各理念的地方武装每年都可能易帜归附；
//   6. 兵败如山倒：与中央交战、只剩不到 CollapseShare 核心城市的政府，接壤中央的城市起义投向中央。
// 民族统一战线期间(NationalSentimentSystem)不打内战，2、3、6 暂停。
public static partial class WarlordEraSystem
{
    private const int MaxCivilWars = 3;
    private const float UnifyWarChance = 0.8f;
    private const float ShiftMinShare = 0.5f;
    private const float ShiftBaseChance = 0.06f;
    private const float ShiftMaxExtraChance = 0.24f;
    private const float UprisingFactor = 0.5f;
    private const int MaxShiftsPerYear = 4;
    private const int MandateBase = 20;
    private const int MandateStep = 5;
    private const float BandwagonShare = 0.4f;
    private const float CollapseShare = 0.3f;
    private const float CollapseChance = 0.25f;
    private const int MaxCollapsePerYear = 10;

    public static bool IsCivilWar(War war)
    {
        if (war == null || war.hasEnded()) return false;
        EmpireCore attacker = LawfulCoreOf(war.getMainAttacker());
        return attacker != null && attacker == LawfulCoreOf(war.getMainDefender()) &&
               attacker.fragmented_since >= 0d && !attacker.split_recognized;
    }

    private static EmpireCore LawfulCoreOf(Kingdom kingdom)
    {
        EmpireCore core = kingdom?.capital?.GetEmpireCore();
        return core?.warlord_parent_core_id > 0 ? EmpireCoreManager.Get(core.warlord_parent_core_id) ?? core : core;
    }

    private static void UpdateUnification(EmpireCore core, Dictionary<Kingdom, int> held, int total, Empire central,
        HashSet<Empire> representatives)
    {
        if (central == null || central.isRekt() || central.IsArchived() || total <= 0) return;
        List<City> cities = EmpireCoreManager.GetTitles(core).Where(title => title != null && !title.isRekt())
            .SelectMany(title => title.city_list).Where(city => city != null && !city.isRekt() && city.kingdom != null)
            .Distinct().ToList();
        if (cities.Count == 0) return;

        AlignMandates(cities, representatives);
        Bandwagon(core, held, total, central);
        if (NationalSentimentSystem.InUnitedFront(core)) return;
        LaunchUnificationWar(core, held, central, representatives);
        PopularShift(core, cities, representatives);
        Collapse(held, total, central, representatives);
    }

    // 4. 民心即正统
    private static void AlignMandates(List<City> cities, HashSet<Empire> representatives)
    {
        var dominant = cities.Select(IdeologyPopulationSystem.GetDominant).ToList();
        foreach (Empire government in representatives)
        {
            if (government == null || government.isRekt() || government.IsArchived()) continue;
            PartyIdeology ideology = GovernmentIdeology(government);
            float support = dominant.Count(value => value == ideology) / (float)dominant.Count;
            int target = Mathf.RoundToInt(MandateBase + (100 - MandateBase) * support);
            int delta = Mathf.Clamp(target - government.Mandate, -MandateStep, MandateStep);
            if (delta != 0) government.AddMandate(delta);
        }
    }

    // 2. 中央主动统一
    private static void LaunchUnificationWar(EmpireCore core, Dictionary<Kingdom, int> held, Empire central,
        HashSet<Empire> representatives)
    {
        Kingdom centralKingdom = central.CoreKingdom;
        if (centralKingdom == null || !centralKingdom.hasKing() || Random.value >= UnifyWarChance) return;
        if (centralKingdom.getWars().Count(IsCivilWar) >= MaxCivilWars) return;
        Kingdom target = held.Keys
            .Where(holder => holder != centralKingdom && IsLocalHolder(core, holder) && !InRealm(holder, central) &&
                             !holder.isEnemy(centralKingdom) && central.IsNeighbourWith(holder) &&
                             FeudalVassalService.CanDeclareExternalWar(centralKingdom, holder))
            .OrderByDescending(holder => Government(holder) != null && representatives.Contains(Government(holder)))
            .ThenBy(holder => held[holder]).FirstOrDefault();
        if (target == null) return;
        War war = World.world.diplomacy.startWar(centralKingdom, target, WarTypeLibrary.normal);
        if (war == null) return;
        EventRecorder.Record(centralKingdom, string.Format(LM.Get("warlord_unify_war_history"),
            central.GetBaseEmpireFullName(), HolderName(target)));
    }

    // 3. 民心所向
    private static void PopularShift(EmpireCore core, List<City> cities, HashSet<Empire> representatives)
    {
        var byIdeology = new Dictionary<PartyIdeology, Empire>();
        foreach (Empire government in representatives)
            if (government != null && !government.isRekt() && !government.IsArchived())
                byIdeology[GovernmentIdeology(government)] = government;
        int shifts = 0;
        foreach (City city in cities.OrderBy(_ => Random.value))
        {
            if (shifts >= MaxShiftsPerYear) return;
            Kingdom ruler = city.kingdom;
            if (ruler == null || ruler.isRekt() || city == ruler.capital) continue;
            PartyIdeology ideology = IdeologyPopulationSystem.GetDominant(city);
            if (!byIdeology.TryGetValue(ideology, out Empire target) || InRealm(ruler, target)) continue;
            Empire rulerGovernment = RulingGovernment(ruler);
            PartyIdeology rulerIdeology = rulerGovernment != null ? GovernmentIdeology(rulerGovernment) : HolderIdeology(ruler);
            if (rulerIdeology == ideology) continue;
            float share = IdeologyPopulationSystem.GetCityShare(city, ideology);
            if (share < ShiftMinShare) continue;
            float chance = (ShiftBaseChance + ShiftMaxExtraChance * (share - ShiftMinShare) / (1f - ShiftMinShare)) *
                           Suppression(rulerGovernment);
            Kingdom neighbour = AdjacentRealmKingdom(city, target);
            if (neighbour == null) chance *= UprisingFactor;
            if (Random.value >= chance) continue;
            string cityName = city.GetCityName();
            if (neighbour != null)
            {
                city.joinAnotherKingdom(neighbour, pCaptured: true, pRebellion: true);
                EventRecorder.Record(neighbour, string.Format(LM.Get("warlord_defect_city_history"), cityName,
                    PartySystem.GetIdeologyName(ideology), target.GetBaseEmpireFullName()));
                shifts++;
            }
            else if (TryUprising(city, ruler, ideology))
            {
                EventRecorder.Record(ruler, string.Format(LM.Get("warlord_uprising_history"), cityName,
                    PartySystem.GetIdeologyName(ideology)));
                shifts++;
            }
        }
    }

    private static bool TryUprising(City city, Kingdom ruler, PartyIdeology ideology)
    {
        Actor leader = city.units?.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
                                                  actor.isAdult() && actor.CanFoundCivKingdom())
            .OrderByDescending(actor => IdeologyPopulationSystem.Get(actor) == ideology)
            .ThenByDescending(actor => actor.renown).FirstOrDefault();
        if (leader == null) return false;
        Kingdom rebel = city.makeOwnKingdom(leader, pRebellion: true);
        if (rebel == null) return false;
        if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱))
        {
            RebellionStartupService.RollbackCitySplit(city, ruler, rebel);
            return false;
        }
        War war = World.world.diplomacy.startWar(rebel, ruler, WarTypeLibrary.rebellion);
        if (war == null)
        {
            RebellionStartupService.RollbackCitySplit(city, ruler, rebel);
            return false;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        return true;
    }

    // 统治者宪法的意识形态强度越高，越压得住异见民心
    private static float Suppression(Empire government) => government == null ? 1f :
        ConstitutionSystem.GetIdeologyIntensity(government) switch
        {
            ConstitutionIdeologyIntensity.High => 0.5f,
            ConstitutionIdeologyIntensity.Low => 1.3f,
            _ => 1f
        };

    // 5. 大势所趋
    private static void Bandwagon(EmpireCore core, Dictionary<Kingdom, int> held, int total, Empire central)
    {
        Kingdom centralKingdom = central.CoreKingdom;
        if (centralKingdom == null || !held.TryGetValue(centralKingdom, out int centralHeld)) return;
        float share = centralHeld / (float)total;
        if (share < BandwagonShare) return;
        float chance = Mathf.Clamp(0.15f + (share - BandwagonShare) * 1.5f, 0f, 0.6f);
        foreach (Kingdom holder in held.Keys.ToList())
        {
            if (holder == centralKingdom || !IsLocalHolder(core, holder) || Government(holder) != null ||
                holder.IsInEmpire() || FeudalVassalService.GetOverlord(holder) != null ||
                holder.GetRegime()?.type != RegimeType.Modern || !holder.hasKing() || Random.value >= chance) continue;
            War war = FindActiveWar(holder, centralKingdom);
            if (war != null)
                World.world.wars.endWar(war, war.isAttacker(centralKingdom) ? WarWinner.Attackers : WarWinner.Defenders);
            if (holder.isEnemy(centralKingdom) || !AbsorbDefector(central, holder)) continue;
            EmpireCoreControl.Invalidate(core);
            EventRecorder.Record(centralKingdom, string.Format(LM.Get("warlord_bandwagon_history"),
                HolderName(holder), central.GetBaseEmpireFullName()));
        }
    }

    // 开国雄主的望风归附(古代王朝，不限军阀时期)：开国雄主在位的帝国控制本核心过半城市时，
    // 接壤、尚未入帝国、没有宗主的本核心割据政权每年按控制比例有一定概率归附(与"大势所趋"同一套概率与归附方式)
    private static void FounderBandwagon(EmpireCore core, Dictionary<Kingdom, int> held, int total)
    {
        if (total <= 0 || NationalSentimentSystem.InUnitedFront(core)) return;
        Kingdom founderKingdom = held.Keys
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom.IsEmpire() &&
                              kingdom.GetEmpire()?.CoreKingdom == kingdom && RulerTraitSystem.FounderReigns(kingdom) &&
                              IsLocalHolder(core, kingdom))
            .OrderByDescending(kingdom => held[kingdom]).FirstOrDefault();
        if (founderKingdom == null) return;
        Empire founder = founderKingdom.GetEmpire();
        float share = held[founderKingdom] / (float)total;
        if (share < BandwagonShare) return;
        float chance = Mathf.Clamp(0.10f + (share - BandwagonShare) * 1.25f, 0f, 0.5f);
        foreach (Kingdom holder in held.Keys.ToList())
        {
            if (holder == null || holder == founderKingdom || holder.isRekt() || !holder.hasKing() ||
                !IsLocalHolder(core, holder) || holder.IsInEmpire() || FeudalVassalService.GetOverlord(holder) != null ||
                holder.GetRegime()?.type == RegimeType.Modern || !Borders(holder, founder) ||
                Random.value >= chance) continue;
            War war = FindActiveWar(holder, founderKingdom);
            if (war != null)
                World.world.wars.endWar(war, war.isAttacker(founderKingdom) ? WarWinner.Attackers : WarWinner.Defenders);
            if (holder.isEnemy(founderKingdom) || !AbsorbDefector(founder, holder)) continue;
            EmpireCoreControl.Invalidate(core);
            EventRecorder.Record(founderKingdom, string.Format(LM.Get("founder_bandwagon_history"),
                holder.GetKingdomFullName(), founder.GetBaseEmpireFullName(), founder.Emperor?.getName() ?? "",
                RulerTraitSystem.FounderTitle(founder.Emperor)));
        }
    }

    // 开国雄主志在一统(古代王朝，不限军阀时期)：开国雄主在位的帝国每年几乎必定对接壤的本核心割据政权(不在帝国里、
    // 不是自己的附庸，含同核心的对立王朝)兴兵讨伐，同时至多 MaxFounderWars 场；不看兵力对比(雄主不等时机)，
    // 只是不打同盟。这类战争不受战争年限强制停战(见 RulerTraitSystem.IsFounderUnificationWar)
    private const int MaxFounderWars = 2;
    private const float FounderWarChance = 0.9f;

    private static void FounderDrive(EmpireCore core, Dictionary<Kingdom, int> held)
    {
        if (NationalSentimentSystem.InUnitedFront(core)) return;
        Kingdom founderKingdom = held.Keys
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom.IsEmpire() && kingdom.hasKing() &&
                              kingdom.GetEmpire()?.CoreKingdom == kingdom && RulerTraitSystem.FounderReigns(kingdom) &&
                              IsLocalHolder(core, kingdom))
            .OrderByDescending(kingdom => held[kingdom]).FirstOrDefault();
        if (founderKingdom == null) return;
        Empire founder = founderKingdom.GetEmpire();
        int wars = founderKingdom.getWars().Count(RulerTraitSystem.IsFounderUnificationWar);
        foreach (Kingdom target in held.Keys
                     .Where(holder => holder != null && holder != founderKingdom && !holder.isRekt() && holder.hasKing() &&
                                      IsLocalHolder(core, holder) && holder.GetEmpire() != founder &&
                                      FeudalVassalService.GetOverlord(holder) != founderKingdom &&
                                      !holder.isEnemy(founderKingdom) && Borders(holder, founder) &&
                                      (founderKingdom.getAlliance() == null || holder.getAlliance() != founderKingdom.getAlliance()))
                     .OrderBy(holder => held[holder]).ToList())
        {
            if (wars >= MaxFounderWars || Random.value >= FounderWarChance) break;
            War war = World.world.diplomacy.startWar(founderKingdom, target, WarTypeLibrary.normal);
            if (war == null) continue;
            wars++;
            EventRecorder.Record(founderKingdom, string.Format(LM.Get("founder_unify_war_history"),
                founder.GetBaseEmpireFullName(), RulerTraitSystem.FounderTitle(founder.Emperor),
                founder.Emperor?.getName() ?? "", target.GetKingdomFullName()));
        }
    }

    // 每月：开国雄主的统一战争里，被讨伐的割据政权按丢掉的地盘(被雄主一方占着的地块比例)、都城是否失陷，
    // 有一定概率举国归附(战争以雄主获胜结束，归附方式同"望风归附")
    // 军阀时期的内战同样：与中央交战的一方地盘被中央占得越多越可能举国归附
    public static void FounderCapitulation()
    {
        if (World.world?.wars == null) return;
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war == null || war.hasEnded()) continue;
            Kingdom attacker = war.getMainAttacker(), defender = war.getMainDefender();
            bool founderWar = RulerTraitSystem.IsFounderUnificationWar(war);
            bool attackerFounder;
            if (founderWar)
                attackerFounder = attacker?.GetEmpire()?.CoreKingdom == attacker && RulerTraitSystem.FounderReigns(attacker);
            else if (IsCivilWar(war))
            {
                EmpireCore lawful = LawfulCoreOf(attacker);
                Empire centralGovernment = lawful == null ? null : ModClass.EMPIRE_MANAGER?.get(lawful.central_empire_id);
                Kingdom centralKingdom = centralGovernment?.CoreKingdom;
                if (centralKingdom == null || attacker != centralKingdom && defender != centralKingdom) continue;
                attackerFounder = attacker == centralKingdom;
            }
            else continue;
            Kingdom founderKingdom = attackerFounder ? attacker : defender, rival = attackerFounder ? defender : attacker;
            Empire founder = founderKingdom?.GetEmpire();
            if (founder == null || rival == null || rival.isRekt() || rival.cities == null || rival.cities.Count == 0) continue;
            int zones = 0, lost = 0;
            foreach (City city in rival.cities)
            {
                if (city?.zones == null) continue;
                foreach (TileZone zone in city.zones)
                {
                    if (zone == null) continue;
                    zones++;
                    Kingdom occupier = city.GetTileZoneOccupier(zone);
                    if (occupier != null && (occupier == founderKingdom || occupier.GetEmpire() == founder)) lost++;
                }
            }
            float share = zones == 0 ? 0f : lost / (float)zones;
            TileZone seat = rival.capital?.getTile()?.zone;
            Kingdom seatHolder = seat == null ? null : rival.capital.GetTileZoneOccupier(seat);
            bool capitalFallen = rival.capital == null || seatHolder != null && (seatHolder == founderKingdom || seatHolder.GetEmpire() == founder);
            float chance = 0.04f + 0.6f * share + (capitalFallen ? 0.3f : 0f);
            if (lost == 0 && !capitalFallen || Random.value >= chance) continue;
            World.world.wars.endWar(war, attackerFounder ? WarWinner.Attackers : WarWinner.Defenders);
            if (!AbsorbDefector(founder, rival)) continue;
            EmpireCore core = rival.capital?.GetEmpireCore();
            if (core != null) EmpireCoreControl.Invalidate(core);
            if (founderWar)
                EventRecorder.Record(founderKingdom, string.Format(LM.Get("founder_capitulation_history"),
                    rival.GetKingdomFullName(), founder.GetBaseEmpireFullName(), RulerTraitSystem.FounderTitle(founder.Emperor),
                    founder.Emperor?.getName() ?? ""));
            else
                EventRecorder.Record(founderKingdom, string.Format(LM.Get("warlord_capitulation_history"),
                    HolderName(rival), founder.GetBaseEmpireFullName()));
        }
    }

    // 与这个帝国接壤：本国有城市与帝国的城市相邻
    private static bool Borders(Kingdom kingdom, Empire empire) =>
        kingdom?.cities != null && kingdom.cities.Any(city =>
            city?.neighbours_cities != null && city.neighbours_cities.Any(neighbour =>
                neighbour?.kingdom != null && neighbour.kingdom.GetEmpire() == empire));

    // 6. 兵败如山倒
    private static void Collapse(Dictionary<Kingdom, int> held, int total, Empire central,
        HashSet<Empire> representatives)
    {
        Kingdom centralKingdom = central.CoreKingdom;
        int defected = 0;
        foreach (Empire loser in representatives)
        {
            if (loser == null || loser == central || loser.isRekt() || loser.IsArchived()) continue;
            Kingdom loserKingdom = loser.CoreKingdom;
            if (loserKingdom == null || FindActiveWar(loserKingdom, centralKingdom) == null) continue;
            int count = held.TryGetValue(loserKingdom, out int value) ? value : 0;
            if (count >= total * CollapseShare) continue;
            foreach (City city in loser.AllCities().ToList())
            {
                if (defected >= MaxCollapsePerYear) return;
                if (city == null || city.isRekt() || city == city.kingdom?.capital || Random.value >= CollapseChance)
                    continue;
                Kingdom neighbour = AdjacentRealmKingdom(city, central);
                if (neighbour == null) continue;
                string cityName = city.GetCityName();
                city.joinAnotherKingdom(neighbour, pCaptured: true, pRebellion: true);
                defected++;
                EventRecorder.Record(neighbour, string.Format(LM.Get("warlord_collapse_history"),
                    loser.GetBaseEmpireFullName(), cityName, central.GetBaseEmpireFullName()));
            }
        }
    }

    // 易帜归附：先按宗属绑定并尝试并入政府；并入不了(法统判定对不上等)就直接加入政府成为正式成员，
    // 只有加入也失败才保留附庸身份。返回是否已归附(成员或附庸)。
    internal static bool AbsorbDefector(Empire central, Kingdom holder)
    {
        Kingdom centralKingdom = central?.CoreKingdom;
        if (centralKingdom == null || holder == null || holder.isRekt()) return false;
        if (!FeudalVassalService.Bind(centralKingdom, holder)) return false;
        FeudalVassalService.MarkDefected(holder);
        ModernStateFormationSystem.TryMergeIntoOverlordGovernment(holder);
        if (holder.GetEmpire() == central) return true;
        if (holder.IsEmpire()) return true;                    // 自有政府的保留附庸身份
        FeudalVassalService.Break(holder);
        if (holder.IsInEmpire()) holder.GetEmpire()?.leave(holder);
        central.join(holder, pForce: true, pLegitimacyTransfer: true);
        if (holder.GetEmpire() == central)
        {
            holder.GetOrCreate().ideology_country_suffix = "";
            holder.updateColor(centralKingdom.getColor());
            EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(holder);
            return true;
        }
        FeudalVassalService.Bind(centralKingdom, holder);
        FeudalVassalService.MarkDefected(holder);
        return true;
    }

    private static Empire RulingGovernment(Kingdom kingdom) =>
        kingdom.GetEmpire() ?? Government(FeudalVassalService.GetOverlord(kingdom));

    private static bool InRealm(Kingdom kingdom, Empire government) =>
        kingdom != null && government != null &&
        (kingdom == government.CoreKingdom || kingdom.GetEmpire() == government ||
         FeudalVassalService.GetOverlord(kingdom) == government.CoreKingdom);

    private static Kingdom AdjacentRealmKingdom(City city, Empire government) =>
        city.neighbours_cities?.FirstOrDefault(neighbour => neighbour != null && !neighbour.isRekt() &&
                                                             InRealm(neighbour.kingdom, government))?.kingdom;
}
