using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国内行政区按王国法理划界(与国家海外飞地的维护、放弃分开，见 ExclaveMaintenanceSystem)：
//   每座城有自己所属的王国法理；帝国里管这片法理的成员(持有该法理的行政区，或王畿/都城法理所在的中央)
//   才是它该归的行政区。城落在别的行政区或中央名下时，改隶管它法理的那个成员。
//   本区法理里的城(哪怕不连成一片)不动；没有法理的城、帝国里还没有成员管的法理也不动，
//   留待"按法理整合行政区"决议整体重划。首都、王畿等受保护的直辖城、战时代管的城不动。
//   和平时才做，每个帝国每年最多改隶两座城，免得版图一夜大变。
public static class InternalBoundarySystem
{
    private const int MovesPerYear = 2;
    private static double _lastScan = -1d;

    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            try
            {
                FixOne(empire);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][行政区划界] 检查失败: {exception.Message}");
            }
        }
    }

    // 行政区自己的法理：持有的主法理，或受托管理的法理
    private static KingdomTitle RegionTitle(Kingdom kingdom) =>
        kingdom.GetMainTitle() ?? kingdom.GetAdministrativeTitle();

    private static bool Usable(KingdomTitle title) => title != null && !title.isRekt();

    private static void FixOne(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.HasTerritorialWar() ||
            AdministrationReorganizationSystem.IsRunning(empire)) return;
        Kingdom core = empire.CoreKingdom;
        if (core == null || core.isRekt() || core.hasEnemies()) return;

        // 法理 → 帝国里管它的成员
        var holders = new Dictionary<KingdomTitle, Kingdom>();
        foreach (Kingdom member in empire.kingdoms_list.ToArray())
        {
            if (member == null || member == core || member.isRekt() || !member.IsAdministrativeKingdomType() ||
                member.IsFactionRebelling() || member.IsLocalRebelling()) continue;
            KingdomTitle own = RegionTitle(member);
            if (Usable(own) && !holders.ContainsKey(own)) holders[own] = member;
        }
        // 王畿(中央主法理)与都城所在法理归中央直辖
        KingdomTitle coreMain = core.GetMainTitle();
        KingdomTitle coreCapital = core.capital?.GetTitle();
        if (Usable(coreMain)) holders[coreMain] = core;
        if (Usable(coreCapital)) holders[coreCapital] = core;

        HashSet<long> protectedCities = empire.GetProtectedDirectCityIds();
        int moves = 0;
        foreach (Kingdom source in empire.kingdoms_list.ToArray())
        {
            if (source == null || source.isRekt() || source.cities == null) continue;
            if (source != core && !source.IsAdministrativeKingdomType()) continue;
            KingdomTitle own = source == core ? null : RegionTitle(source);
            foreach (City city in source.cities.ToArray())
            {
                if (city == null || city.isRekt() || city == source.capital || city == core.capital ||
                    protectedCities.Contains(city.id) || WartimeCustodySystem.InCustody(city)) continue;
                KingdomTitle title = city.GetTitle();
                // 没有法理、或本就在本区法理里(含不连片的飞地)：法理归属没错，不动
                if (!Usable(title) || title == own) continue;
                // 中央名下的王畿/都城法理城也不动
                if (source == core && (title == coreMain || title == coreCapital)) continue;
                // 帝国里还没有成员管这片法理：留待按法理整合行政区时整体重划
                if (!holders.TryGetValue(title, out Kingdom target) || target == source) continue;
                city.joinAnotherKingdom(target);
                if (city.kingdom != target) continue;
                FiscalDiagnostics.Count("行政区划界");
                TranslateHelper.LogEventMessage(string.Format(LM.Get("internal_boundary_dejure"),
                    city.GetCityName(), title.data.name, source.GetKingdomName(), target.GetKingdomName()), core);
                if (++moves >= MovesPerYear) return;
            }
        }
    }
}
