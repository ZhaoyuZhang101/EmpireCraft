using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

public enum JimiMode
{
    Auto = 0,   // 按文化/种族：异文化、异族地区羁縻，其余直辖
    Jimi = 1,   // 指定羁縻
    Direct = 2  // 指定直辖
}

// 羁縻(华夏制度树"羁縻制度"，特性 jimi_system)：
//   未施行时异文化地区也按普通道/军府管理；施行后由国家逐区决定羁縻或直辖(见 jimi_policy 神力)。
//   羁縻地区：有外交权的为都护府(中央委派都护统筹，下辖各羁縻州)，无外交权的为羁縻州(本地首领世守)；
//   保留本地习俗，不参与法理文化转化；上缴中央的税少(见 TreasurySystem.TaxShares)；
//   承担边防义务：中央与之接壤的敌国开战时必须出兵，与己无涉的远方战争不强征。
public static class JimiSystem
{
    public const string Feature = "jimi_system";
    // 羁縻地区上缴中央的税收比例(%)
    public const int CentralTaxPercent = 10;
    private static double _lastScan = -1d;

    public static bool Enacted(Empire empire) =>
        empire != null && InstitutionSystem.GetFeature(empire, Feature) > 0f;

    public static JimiMode Mode(Kingdom kingdom) =>
        kingdom?.data == null ? JimiMode.Auto : (JimiMode)Math.Max(0, Math.Min(2, kingdom.GetOrCreate().jimi_mode));

    // 政体条件 jimi_region 用：这个地方政区是否按羁縻管理
    public static bool IsJimiRegion(Kingdom kingdom, Empire empire)
    {
        if (kingdom == null || empire == null || kingdom.IsEmpire() || !Enacted(empire)) return false;
        switch (Mode(kingdom))
        {
            case JimiMode.Jimi: return true;
            case JimiMode.Direct: return false;
        }
        Kingdom core = empire.CoreKingdom;
        if (core != null && kingdom.species_id != core.species_id) return true;
        string empireCulture = CultureService.GetEmpireDefaultCulture(empire);
        string realmCulture = CultureService.GetRealmCulture(kingdom);
        return CultureService.IsValidCulture(empireCulture) && CultureService.IsValidCulture(realmCulture) &&
               !string.Equals(empireCulture, realmCulture, StringComparison.Ordinal);
    }

    // 当前实际是羁縻政区(都护府或羁縻州)
    public static bool IsJimiAdministration(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        KingdomType type = kingdom.GetKingdomType();
        return type == KingdomType.LvLing_duhufu || type == KingdomType.LvLing_jimizhou;
    }

    // 神力：自动 → 羁縻 → 直辖 → 自动。返回提示文字
    public static string Cycle(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (kingdom == null || kingdom.wild || kingdom.isRekt() || empire == null || kingdom.IsEmpire())
            return LM.Get("jimi_policy_not_region");
        if (!Enacted(empire))
            return string.Format(LM.Get("jimi_policy_unavailable"), empire.GetEmpireFullName());
        var data = kingdom.GetOrCreate();
        data.jimi_mode = ((int)Mode(kingdom) + 1) % 3;
        // 下次政体检查立即按新设定重判政区类型
        data.last_kingdom_status_ts = -1d;
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        return string.Format(LM.Get("jimi_policy_set"), kingdom.GetKingdomName(), LM.Get("jimi_mode_" + Mode(kingdom).ToString().ToLowerInvariant()));
    }

    // 边防义务：每月一次(挂在 CityPatch 的世界扫描轮转上)
    public static void TryMonthlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getMonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            try
            {
                CheckFrontierDuty(empire);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][羁縻] 边防义务检查失败: {exception.Message}");
            }
        }
    }

    private static void CheckFrontierDuty(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !Enacted(empire)) return;
        Kingdom core = empire.CoreKingdom;
        if (core == null || core.isRekt() || !core.hasEnemies()) return;
        List<War> wars = null;
        foreach (Kingdom region in empire.kingdoms_list.ToArray())
        {
            if (region == null || region == core || !IsJimiAdministration(region)) continue;
            if (region.isInWarWith(core) || region.IsFactionRebelling() || region.IsLocalRebelling()) continue;
            wars ??= new List<War>(core.getWars());
            foreach (War war in wars)
            {
                if (war == null || !war.isAlive() || war.hasEnded() || war.GetEmpireWarType() == EmpireWarType.劫掠) continue;
                if (IsMember(war, region)) continue;
                bool coreAttacks = war.isAttacker(core);
                if (!coreAttacks && !war.isDefender(core)) continue;
                if (!BordersSide(region, war, !coreAttacks)) continue;
                if (coreAttacks) war.joinAttackers(region);
                else war.joinDefenders(region);
                if (!IsMember(war, region)) continue;
                TranslateHelper.LogEventMessage(string.Format(LM.Get("jimi_frontier_duty"),
                    region.GetKingdomName(), empire.GetEmpireFullName()), region);
            }
        }
    }

    private static bool IsMember(War war, Kingdom kingdom) =>
        war._list_attackers != null && war._list_attackers.Contains(kingdom) ||
        war._list_defenders != null && war._list_defenders.Contains(kingdom);

    // 本政区是否与这场战争的敌方阵营接壤
    private static bool BordersSide(Kingdom region, War war, bool enemyIsAttacker)
    {
        List<Kingdom> enemies = enemyIsAttacker ? war._list_attackers : war._list_defenders;
        if (enemies == null || enemies.Count == 0) return false;
        foreach (City city in region.cities)
        {
            if (city?.zones == null || city.isRekt()) continue;
            foreach (TileZone zone in city.zones)
            {
                if (zone?.neighbours == null) continue;
                foreach (TileZone neighbour in zone.neighbours)
                {
                    Kingdom other = neighbour?.city?.kingdom;
                    if (other != null && other != region && enemies.Contains(other)) return true;
                }
            }
        }
        return false;
    }
}
