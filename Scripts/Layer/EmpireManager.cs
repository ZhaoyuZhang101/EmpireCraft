using db;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using HarmonyLib;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

public class EmpireManager : MetaSystemManager<Empire, EmpireData>
{
    private readonly List<Empire> _empiresToProcess = new();

    public EmpireManager() 
    {
        this.type_id = "empire";
    }
    public bool _dirty_cities = false;
    public override void updateDirtyUnits()
    {
    }

    public override void startCollectHistoryData()
    {
    }

    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        // 创建集合副本进行遍历
        _empiresToProcess.Clear();
        foreach (Empire empire in this)
        {
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(empire)) continue;
            empire.RefreshCompatibilityMembership();
            _empiresToProcess.Add(empire);
        }
        ClearStaleLegitimacyRivalries();
        double worldTime = World.world.getCurWorldTime();
        if (_lastStatsCacheTimestamp <= 0 || Date.getMonthsSince(_lastStatsCacheTimestamp) >= 1)
        {
            // 禁止原版结盟时，旧存档遗留或其他途径漏进来的原版同盟也一并清掉(模组同盟与神力强制同盟保留)
            if (ModAllianceService.IsVanillaAllianceBanned()) ModAllianceService.DissolveVanillaAlliances();
            // 帝国本身及其成员国不留在同盟里(兜底：称帝、继承、读档等途径进帝国的)
            ModAllianceService.RemoveEmpireMembersFromAlliances();
            foreach (Empire e in _empiresToProcess)
            {
                if (e.IsArchived()) continue;
                int pop = 0;
                int warriors = 0;
                int warriorsMax = 0;
                var tKingdoms = e.kingdoms_list;
                for (int i = 0; i < tKingdoms.Count; i++)
                {
                    var k = tKingdoms[i];
                    var ked = KingdomExtension.GetOrCreate(k);
                    ked.last_cached_timestamp = -1L;
                    int kp = k.getPopulationPeople();
                    int kw = k.countTotalWarriors();
                    int km = k.countWarriorsMax();
                    pop += kp;
                    warriors += kw;
                    warriorsMax += km;
                    ked.cached_population = kp;
                    ked.cached_warriors = kw;
                    ked.last_cached_timestamp = worldTime;
                    var cities = k.cities;
                    for (int j = 0; j < cities.Count; j++)
                    {
                        var c = cities[j];
                        var ced = CityExtension.GetOrCreate(c);
                        ced.last_cached_timestamp = -1L;
                        ced.cached_population = c.CountLivingPopulation();
                        ced.cached_warriors = c.CountLivingWarriors();
                        ced.last_cached_timestamp = worldTime;
                    }
                }
                e.data.cached_population = pop;
                e.data.cached_warriors = warriors;
                e.data.cached_warriors_max = warriorsMax;
                e.data.last_cached_timestamp = worldTime;
            }
            _lastStatsCacheTimestamp = worldTime;
        }

        foreach (Empire current in _empiresToProcess)
        {
            if (current.IsArchived() || _to_dissolve.Contains(current)) continue;
            current.clearCursorOver();

            if (!current.checkActive())
            {
                if (!current.TryRepairState("EmpireManager.update fallback"))
                {
                    _to_dissolve.Add(current);
                }
            }
            else
            {
                current.update();
            }
        }
        // 处理需要解散的帝国
        foreach (Empire item in _to_dissolve)
        {
            dissolveEmpire(item);
        }
        _to_dissolve.Clear();
        _empiresToProcess.Clear();
    }

    // 清理失效的正统对手，并为旧存档中的既有僭越者补齐伪法理标记。
    private void ClearStaleLegitimacyRivalries()
    {
        foreach (Empire empire in _empiresToProcess)
        {
            if (empire?.data == null || empire.IsArchived() || empire.isRekt() ||
                !empire.data.legitimacy_rivalry_recognized) continue;
            Empire rival = ModClass.EMPIRE_MANAGER.get(empire.data.legitimacy_rival_empire_id);
            if (rival == null || rival.IsArchived() || rival.isRekt())
            {
                ImperialLegitimacyChallengeService.ClearRivalry(empire);
                continue;
            }
            if (!empire.data.legitimacy_challenger) continue;
            EmpireCore core = EmpireCoreManager.Get(empire);
            EmpireCore legitimateCore = EmpireCoreManager.Get(rival);
            KingdomTitle mainTitle = empire.CoreKingdom?.GetMainTitle();
            if (core == null || core.false_core_against_empire_id == rival.id ||
                !EmpireCoreManager.ContainsTitle(legitimateCore, mainTitle)) continue;
            core.false_core_against_empire_id = rival.id;
            EmpireCoreManager.SyncCitiesFromTitles(legitimateCore);
        }
    }


    public void dissolveEmpire(Empire pEmpire)
    {
        if (pEmpire == null) return;
        EmpireFormationService.OnEmpireDissolving(pEmpire);
        pEmpire.dissolve();
        pEmpire.Dispose();
        pEmpire.Archive();
        removeObject(pEmpire);
    }

    private List<Empire> _to_dissolve = new List<Empire>();

    public override void clear()
    {
        base.clear();
    }

    public List<TileZone> GetAllZones()
    {
        List<TileZone> zones = new();
        foreach (Empire e in this)
        {
            foreach(Kingdom k in e.kingdoms_list)
            {
                foreach(City c in k.cities)
                {
                    zones.AddRange(c.zones);
                }
            }
        }
        return zones;
    }

    public override void addObject(Empire pObject)
    {
        base.addObject(pObject);
        World.world.zone_calculator?.setDrawnZonesDirty();
    }
    public override void removeObject(Empire pKingdom)
    {
        base.removeObject(pKingdom);
        World.world.zone_calculator?.setDrawnZonesDirty();
    }
    
    public void RemoveArchivedEmpire(Empire pEmpire)
    {
        if (pEmpire == null) return;
        if (!pEmpire.IsArchived()) return;
        removeObject(pEmpire);
    }
    
    public void PurgeArchivedOlderThanYears(int years)
    {
        var list = this.ToList();
        foreach (var e in list)
        {
            if (!e.IsArchived()) continue;
            if (Date.getYearsSince(e.data.timestamp_established_time) > years)
            {
                removeObject(e);
            }
        }
    }

    // Token: 0x0400230C RID: 8972
    public Sprite[] _cached_banner_backgrounds;

    // Token: 0x0400230D RID: 8973
    public Sprite[] _cached_banner_icons;


    // replacingEmpire：由该帝国延续而来（核心王国灭亡后由皇族王国接续等）。它此刻仍然存在，
    // 不能把它当成"同文化已有帝国"而拒绝建立继承者。
    public Empire NewEmpire(Kingdom pKingdom, bool isSplit = false,
        bool allowCultureRival = false, bool forceNewCore = false, Empire replacingEmpire = null)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(pKingdom)) return null;
        if (pKingdom == null || !pKingdom.isAlive())
        {
            return null;
        }
        string culture = CultureService.GetRealmCulture(pKingdom);
        if (!CultureService.IsValidCulture(culture) ||
            (!allowCultureRival && CultureService.HasActiveEmpireForCulture(culture, replacingEmpire)))
        {
            return null;
        }
        if (!EnsureKingForNewEmpire(pKingdom))
        {
            LogService.LogWarning($"[EmpireManager] Failed to create empire for kingdom {pKingdom?.name}: no valid king candidate.");
            return null;
        }
        long id = OverallHelperFunc.IdGenerator.NextId();
        var empire = newObjectFromID(id);
        empire.CreateNewEmpire(pKingdom, isSplit);
        if (empire.CoreKingdom == null)
        {
            LogService.LogWarning($"[EmpireManager] Failed to initialize empire core kingdom for {pKingdom?.name}.");
            return null;
        }
        empire.addFounder(pKingdom);
        empire.updateColor(pKingdom.getColor());
        empire.data.timestamp_given_time = World.world.getCurWorldTime();
        var riseCore = forceNewCore ? null : EmpireCoreManager.GetRiseCandidateCore(pKingdom);
        // 候选核心已归属别的现存帝国时不能抢过来(主法理在别国核心里的根本不能称帝，见 EmpireFormationService)；
        // 此时只可能是都城/其他法理落在别国核心里，主法理本身没有核心，按规则新建核心
        if (riseCore != null && EmpireCoreManager.GetEmpires(riseCore).Any(other => other != empire && !other.IsArchived()))
            riseCore = null;
        if (riseCore != null)
        {
            EmpireCoreManager.RebindEmpire(empire, riseCore);
        }
        else
        {
            EmpireCoreManager.newEmpireCore(empire);
        }
        pKingdom.GetOrCreate().isEmpire = true;
        pKingdom.GetOrCreate().EmpireID = empire.id;
        EmpireFormationService.OnEmpireCreated(empire);
        if (empire.data.has_year_name)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_log, pKingdom.king.name, empire.GetEmpireName())
            {
                location = pKingdom.location,
                color_special1 = pKingdom.getColor().getColorText()
            }.add();
        }
        else
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_west_log, pKingdom.king.name, empire.GetEmpireName())
            {
                location = pKingdom.location,
                color_special1 = pKingdom.getColor().getColorText()
            }.add();
        }
        
        return empire;
    }

    private static bool EnsureKingForNewEmpire(Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.isAlive())
        {
            return false;
        }
        if (kingdom.hasKing() && kingdom.king != null && !kingdom.king.isRekt())
        {
            return true;
        }
        Actor fallbackKing = FindFallbackKingCandidate(kingdom);
        if (fallbackKing == null)
        {
            return false;
        }

        fallbackKing.removeFromArmy();
        if (fallbackKing.isCityLeader() && fallbackKing.city != null)
        {
            fallbackKing.city.removeLeader();
        }
        kingdom.setKing(fallbackKing);
        if (kingdom.capital != null)
        {
            fallbackKing.joinCity(kingdom.capital);
        }
        fallbackKing.joinKingdom(kingdom);
        return kingdom.hasKing() && kingdom.king != null && !kingdom.king.isRekt();
    }

    private static Actor FindFallbackKingCandidate(Kingdom kingdom)
    {
        if (kingdom?.cities != null)
        {
            for (int i = 0; i < kingdom.cities.Count; i++)
            {
                City city = kingdom.cities[i];
                if (city == null || city.isRekt() || city.units == null)
                {
                    continue;
                }
                for (int j = 0; j < city.units.Count; j++)
                {
                    Actor actor = city.units[j];
                    if (IsValidFallbackKingCandidate(actor))
                    {
                        return actor;
                    }
                }
            }
        }

        if (kingdom?.units != null)
        {
            for (int i = 0; i < kingdom.units.Count; i++)
            {
                Actor actor = kingdom.units[i];
                if (IsValidFallbackKingCandidate(actor))
                {
                    return actor;
                }
            }
        }

        return null;
    }

    private static bool IsValidFallbackKingCandidate(Actor actor)
    {
        return actor != null
               && !actor.isRekt()
               && actor.isAlive()
               && actor.isAdult()
               && actor.isUnitFitToRule();
    }

    public bool forceEmpire(Kingdom pKingdom1, Kingdom pKingdom2)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(pKingdom1) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(pKingdom2)) return false;
        Empire empire = ModClass.EMPIRE_MANAGER.get(pKingdom1.GetEmpireID());
        if (empire == null)
        {
            empire = ModClass.EMPIRE_MANAGER.get(pKingdom2.GetEmpireID());
        }
        bool result = false;
        if (empire == null)
        {
            empire = this.NewEmpire(pKingdom1);
            if (empire == null)
            {
                return false;
            }
            empire.join(pKingdom2, pForce: true);
            result = true;
        }
        else
        {
            empire.join(pKingdom1, true, true);
            empire.join(pKingdom2, true, true);
        }
        return result;
    }
    public Sprite[] getBackgroundsList()
    {
        if (_cached_banner_backgrounds == null)
        {
            _cached_banner_backgrounds = SpriteTextureLoader.getSpriteList("alliances/backgrounds/");
        }
        return _cached_banner_backgrounds;
    }

    public Sprite[] getIconsList()
    {
        if (_cached_banner_icons == null)
        {
            _cached_banner_icons = SpriteTextureLoader.getSpriteList("alliances/icons/");
        }
        return _cached_banner_icons;
    }
    
    private double _lastStatsCacheTimestamp = -1L;
}
