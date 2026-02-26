using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public enum ConditionType
{
    empire_center,
    succession,
    allow_diplomacy,
    allow_army,
    support_center_army,
    another_race,
    controlled_titles,
    cities_count,
    religion_level,
    empire_royal,
    is_border,
    None
}
public class EmpireCraftKingdomBehCheckKingdomType: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    private static bool MatchConditions(Kingdom kingdom, Regime regime, List<string> conditions)
    {
        if (conditions == null || conditions.Count == 0) return false;
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        foreach (var cond in conditions)
        {
            if (string.IsNullOrEmpty(cond)) continue;
            var parts = cond.Split(':');
            var key = Enum.TryParse<ConditionType>(parts[0], out var value) ? value : ConditionType.None;
            var val = parts.Length > 1 ? parts[1] : "";
            switch (key)
            {
                case ConditionType.empire_center:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (kingdom.IsEmpire() != expect) return false;
                    break;
                }
                case ConditionType.succession:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var actual = regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession;
                    if (actual != expect) return false;
                    break;
                }
                case ConditionType.allow_diplomacy:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (regime.IsAllowDiplomacy() != expect) return false;
                    break;
                }
                case ConditionType.allow_army:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (regime.IsAllowArmy() != expect) return false;
                    break;
                }
                case ConditionType.support_center_army:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (regime.IsAllowSupportCenterArmy() != expect) return false;
                    break;
                }
                case ConditionType.another_race:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var actual = empire != null && empire.CoreKingdom != null && kingdom.species_id != empire.CoreKingdom.species_id;
                    if (actual != expect) return false;
                    break;
                }
                case ConditionType.controlled_titles:
                {
                    if (string.IsNullOrEmpty(val)) return false;
                    var segs = val.Split('|');
                    if (segs.Length != 2) return false;
                    var op = segs[0];
                    if (!int.TryParse(segs[1], out var n)) return false;
                    var c = kingdom.GetControlledTitles().Count;
                    var ok = op switch
                    {
                        ">=" => c >= n,
                        ">" => c > n,
                        "<=" => c <= n,
                        "<" => c < n,
                        "==" => c == n,
                        "=" => c == n,
                        _ => false
                    };
                    if (!ok) return false;
                    break;
                }
                case ConditionType.cities_count:
                {
                    if (string.IsNullOrEmpty(val)) return false;
                    var segs = val.Split('|');
                    if (segs.Length != 2) return false;
                    var op = segs[0];
                    if (!int.TryParse(segs[1], out var n)) return false;
                    var c = kingdom.cities.Count;
                    var ok = op switch
                    {
                        ">=" => c >= n,
                        ">" => c > n,
                        "<=" => c <= n,
                        "<" => c < n,
                        "==" => c == n,
                        "=" => c == n,
                        _ => false
                    };
                    if (!ok) return false;
                    break;
                }
                case ConditionType.religion_level:
                {
                    if (string.IsNullOrEmpty(val)) return false;
                    if (int.TryParse(val, out var eqLevel))
                    {
                        var actual = (int) regime.GetReligionLevel();
                        if (actual != eqLevel) return false;
                    }
                    else
                    {
                        var segs = val.Split('|');
                        if (segs.Length != 2) return false;
                        var op = segs[0];
                        if (!int.TryParse(segs[1], out var n)) return false;
                        var actual = (int) regime.GetReligionLevel();
                        var ok = op switch
                        {
                            ">=" => actual >= n,
                            ">" => actual > n,
                            "<=" => actual <= n,
                            "<" => actual < n,
                            "==" => actual == n,
                            "=" => actual == n,
                            _ => false
                        };
                        if (!ok) return false;
                    }
                    break;
                }
                case ConditionType.empire_royal:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var actual = empire != null && kingdom.GetSpecificClan() == empire.EmpireSpecificClan;
                    if (actual != expect) return false;
                    break;
                }
                case ConditionType.is_border:
                {
                    var expect = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var actual = kingdom.IsBorder();
                    if (actual != expect) return false;
                    break;
                }
                default:
                    return false;
            }
        }
        return true;
    }
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isRekt()) return BehResult.Continue;
        pKingdom.CacheData();
        pKingdom.CheckEmpire();
        var ked = pKingdom.GetOrCreate();
        if (ked != null && ked.last_kingdom_status_ts > 0)
        {
            if (Date.getMonthsSince(ked.last_kingdom_status_ts) < 1)
            {
                return BehResult.Continue;
            }
        }
        SyncKingdomStatus(pKingdom);
        SyncOffice(pKingdom);
        if (ked != null) ked.last_kingdom_status_ts = World.world.getCurWorldTime();
        return BehResult.Continue;
    }

    private static void SyncOffice(Kingdom pKingdom)
    {
        OfficeObject office = pKingdom.GetOffice();
        if (office == null)
        {
            pKingdom.InitialRegime();
            return;
        }
        office.meta_object = pKingdom;
        office.is_local = true;
        office.actor_id = pKingdom.king?.id??-1L;
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire == null || empire.isRekt() || empire.IsArchived()) return;
            if (empire.data.centerOffice == null)
            {
                var core = empire.CoreKingdom;
                if (core == null) return;
                empire.data.centerOffice = new CenterOffice();
                empire.data.centerOffice.Init(core);
            }
            empire.data.centerOffice.SyncMetaObject(pKingdom);
        }
    }

    public static void SyncKingdomStatus(Kingdom pKingdom)
    {
        var originalKingdomType = pKingdom.GetKingdomType();
        //计算当前国家类别
        KingdomType newkingdomType = CalcKingdomType(pKingdom);
        pKingdom.SetKingdomType(newkingdomType);
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            empire?.SetEmpireName(pKingdom.GetKingdomName());
        }
        //获取国家政体后同步国家官位
        var regime = pKingdom.GetRegime();
        if (regime == null) return;
        var needSyncOffice = pKingdom.GetOffice() == null || pKingdom.GetOffice()?.regimeType != regime.type || originalKingdomType != newkingdomType;
        if (needSyncOffice)
        {
            BureauSetting setting = null;
            if (regime.bureau_config != null && regime.bureau_config.kingdoms != null)
            {
                regime.bureau_config.kingdoms.TryGetValue(newkingdomType, out setting);
            }
            OfficeObject officeObject = new OfficeObject();
            if (setting != null)
            {
                officeObject.InitialOffice(setting);
            }
            officeObject.regimeType = regime.type;
            officeObject.meta_object = pKingdom;
            officeObject.is_local = true;
            if (officeObject.leader_select_method != LeaderSelectMethod.Default)
            {
                regime.SetLeaderSelectMethod(officeObject.leader_select_method);
            }
            if (pKingdom.hasKing())
            {
                officeObject.SetActor(pKingdom.king);
            }
            pKingdom.SetOffice(officeObject);
            foreach (var city in pKingdom.cities)
            {
                city.InitialRegime();
            }
        }
        
        var kingdomFront = pKingdom.GetKingdomName();
        if (pKingdom.hasCapital())
        {
            if (!pKingdom.capital.hasTitle())
            {
                kingdomFront = pKingdom.GetKingdomName();
            }
            else
            {
                kingdomFront = pKingdom.capital.GetCityName();
            }
        }
        
        if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
        {
            if (pKingdom.HasMainTitle())
            {
                if (pKingdom.GetMainTitle()?.name != null)
                {
                    kingdomFront = pKingdom.GetMainTitle().name;
                }
            }
        }

        if (pKingdom.IsEmpire())
        {
            kingdomFront = pKingdom.GetEmpire()?.GetEmpireName();
        }
        var kingdomBack = LM.Get(newkingdomType.ToString());
        if (!pKingdom.IsFactionRebelling()&&pKingdom.getWars().Count()<=0)
        {
            pKingdom.data.name = string.Join("\u200A", kingdomFront, kingdomBack);
        }
        foreach (var city in pKingdom.cities)
        {
            var cityBack = LM.Get(city.GetCityType().ToString());
            city.data.name = string.Join("\u200A", city.GetCityName(), cityBack);
        }
    }

    public static CityType CalcCityType(Kingdom kingdom)
    {
        Regime regime = kingdom.GetRegime();
        if (regime == null)
        {
            LogService.LogInfo("国家政策为空");
            return CityType.Feudalism_city;
        }
        KingdomType kingdomType = kingdom.GetKingdomType();
        if (regime.bureau_config != null && regime.bureau_config.kingdoms != null)
        {
            if (regime.bureau_config.kingdoms.TryGetValue(kingdomType, out var setting))
            {
                if (setting != null)
                {
                    return setting.city_type;
                }
            }
        }
        return CityType.Feudalism_city;
    }
    
    //依据制度的不同选项动态调整国家后缀
    public static KingdomType CalcKingdomType(Kingdom kingdom)
    {
        var regime = kingdom.GetRegime();
        if (regime == null) return KingdomType.LvLing_kingdom;
        if (kingdom.IsInEmpire()||regime.default_kingdom == KingdomType.default_country_post)
        {
            if (regime.bureau_config is { kingdoms: not null })
            {
                foreach (var kv in regime.bureau_config.kingdoms)
                {
                    var target = kv.Key;
                    var setting = kv.Value;
                    if (setting == null) continue;
                    if (MatchConditions(kingdom, regime, setting.condition))
                    {
                        return target;
                    } 
                }
            }
        }
        LogService.LogInfo(regime.default_kingdom.ToString());
        return  regime.default_kingdom;
    }
    private static int GetOption(Regime regime, ConditionType key)
    {
        if (regime?.options == null) return 0;
        if (!regime.options.TryGetValue(key.ToString(), out var arr) || arr == null || arr.Length == 0) return 0;
        return arr[0];
    }
}
