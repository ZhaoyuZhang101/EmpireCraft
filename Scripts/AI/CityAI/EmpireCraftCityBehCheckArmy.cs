using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckArmy:GameAICityBase
{
    public override Type OriginalBeh => typeof(CityBehCheckArmy);
    public override BehResult execute(City pCity)
    {
        var ced = pCity.GetOrCreate();
        if (ced != null && ced.last_army_check_ts > 0)
        {
            if (Date.getMonthsSince(ced.last_army_check_ts) < 1)
            {
                return BehResult.Continue;
            }
        }
        if (!pCity.hasKingdom()) return BehResult.Continue;
        if (!WorldLawLibrary.world_law_civ_army.isEnabled()) return BehResult.Continue;
        Regime regime = pCity.kingdom.GetRegime();
        if (regime == null || !regime.IsAllowArmy())
        {
            pCity.disbandArmy();
            if (ced != null) ced.last_army_check_ts = World.world.getCurWorldTime();
            return BehResult.Continue;
        };
        if (pCity.kingdom.GetKingdomType() == KingdomType.LvLing_jiedushi)
        {
            pCity.units.ForEach(a =>
            {
                if (pCity.checkCanMakeWarrior(a))
                {
                    pCity.makeWarrior(a);
                } 
            });
        }
        pCity.checkArmyExistence();
        if (pCity.hasArmy())
        {
            Army army = pCity.army;
            if (pCity.isCapitalCity())
            {
                Kingdom k = pCity.kingdom;
                if (k.IsInEmpire()&&!k.IsEmpire())
                {
                    Empire empire = k.GetEmpire();
                    if (empire != null)
                    {
                        if ((empire.CoreKingdom?.GetMoney()??-1) > 0)
                        {
                            if (army == k.GetCenterArmy())
                            {
                                army._captain?.setKingdom(empire.CoreKingdom);
                                army.units.ForEach(a => a.setKingdom(empire.CoreKingdom));
                                army.name = $"{k.GetEmpire().GetEmpireName()}-{k.GetKingdomName()}驻军";
                                CreateNewArmy(pCity);
                                if (ced != null) ced.last_army_check_ts = World.world.getCurWorldTime();
                                return BehResult.Continue;
                            }
                        }
                    }
                }
            }
            InitOrUpdateArmyOffice(pCity.kingdom, army);
            if (ced != null) ced.last_army_check_ts = World.world.getCurWorldTime();
            return BehResult.Continue;
        }
        CreateNewArmy(pCity);
        if (ced != null) ced.last_army_check_ts = World.world.getCurWorldTime();
        return BehResult.Continue;
    }
    public static void CreateNewArmy(City pCity)
    {
        Actor randomWarrior = null;
        var regime = pCity.kingdom?.GetRegime();
        if (regime == null) return;
        if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Exam)
        {
            var kunits = pCity.kingdom?.units;
            if (kunits != null && kunits.Count > 0)
            {
                for (int i = 0; i < kunits.Count; i++)
                {
                    var a = kunits[i];
                    if (a == null || !a.isAlive()) continue;
                    if (a.hasTrait("juren") || a.hasTrait("gongshi"))
                    {
                        randomWarrior = a;
                        break;
                    }
                }
            }
        } 
        if (randomWarrior == null)
        {
            var cu = pCity.units;
            if (cu != null && cu.Count > 0)
            {
                for (int i = 0; i < cu.Count; i++)
                {
                    var a = cu[i];
                    if (a == null || !a.isAlive()) continue;
                    randomWarrior = a;
                    break;
                }
            }
        }
        if (randomWarrior == null)
        {
            return;
        }
        randomWarrior.setProfession(UnitProfession.Warrior);
        Army army = world.armies.newArmy(randomWarrior, pCity);
        InitOrUpdateArmyOffice(pCity.kingdom, army);
    }
    private static void InitOrUpdateArmyOffice(Kingdom kingdom, Army army)
    {
        if (kingdom?.data == null || kingdom.isRekt() || army == null) return;
        if (kingdom.GetRegime()==null) return;
        var regime = kingdom.GetRegime();
        var setting = SelectArmySetting(kingdom, regime, army);
        if (setting == null) return;
        var office = OfficeManager.Offices.Values.FirstOrDefault(o => o.meta_object == army);
        if (office != null)
        {
            office.InitialOffice(setting, isNew:false);
            office.regimeType = regime.type;
            office.meta_object = army;
            office.is_local = false;
            if (army._captain != null)
            {
                office.SetActor(army._captain);
            }
            return;
        }
        office = new OfficeObject();
        office.InitialOffice(setting);
        office.regimeType = regime.type;
        office.meta_object = army;
        office.is_local = false;
        if (army._captain != null)
        {
            office.SetActor(army._captain);
        }
    }
    private static BureauSetting SelectArmySetting(Kingdom kingdom, Regime regime, Army army)
    {
        if (regime.bureau_config?.armies == null || regime.bureau_config.armies.Count == 0)
        {
            return null;
        }
        foreach (var kv in regime.bureau_config.armies)
        {
            var setting = kv.Value;
            if (setting == null) continue;
            if (MatchArmyConditions(kingdom, regime, army, setting.condition))
            {
                return setting;
            }
        }
        return regime.bureau_config.armies.Values.FirstOrDefault();
    }
    private static bool MatchArmyConditions(Kingdom kingdom, Regime regime, Army army, List<string> conditions)
    {
        if (conditions == null || conditions.Count == 0) return true;
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        foreach (var cond in conditions)
        {
            if (string.IsNullOrEmpty(cond)) continue;
            var parts = cond.Split(':');
            var key = parts[0];
            var val = parts.Length > 1 ? parts[1] : "";
            switch (key)
            {
                case "empire_center":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (kingdom.IsEmpire() != expect) return false;
                    break;
                }
                case "kingdom_is_border":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var actual = kingdom.IsBorder();
                    if (actual != expect) return false;
                    break;
                }
                case "allow_army":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (regime.IsAllowArmy() != expect) return false;
                    break;
                }
                case "allow_diplomacy":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (regime.IsAllowDiplomacy() != expect) return false;
                    break;
                }
                case "support_center_army":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (regime.IsAllowSupportCenterArmy() != expect) return false;
                    break;
                }
                case "another_race":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var actual = empire != null && kingdom.species_id != empire.CoreKingdom.species_id;
                    if (actual != expect) return false;
                    break;
                }
                case "is_capital":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var actual = army._kingdom.capital == army._city;
                    if (actual != expect) return false;
                    break;
                }
                case "city_is_border":
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var c = army._city;
                    var actual = false;
                    if (c != null)
                    {
                        if (c.neighbours_kingdoms.Count > 0)
                        {
                            foreach (var k2 in c.neighbours_kingdoms)
                            {
                                if (!k2.IsInSameEmpire(kingdom))
                                {
                                    actual = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (actual != expect) return false;
                    break;
                }
                default:
                    return false;
            }
        }
        return true;
    }
}
