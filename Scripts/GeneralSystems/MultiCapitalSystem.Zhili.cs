using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 两直隶(方案第 4 步，用户 2026-10-20 拍板)：内阁(行省)阶段，多京只留据城最多的那个陪京，以它所在的王国法理设第二直隶，归储君：
//   · 主直隶就是帝国核心王国(称直隶省，现状)；第二直隶是新国家类别 LvLing_zhili(条件 zhili:true，见 LvLing 配置)，
//     名号"{京名}直隶"；京城原在核心王国直辖的，按那个王国法理在核心里的城划出来；原在行政区(省)里的，那个省改为直隶；
//     原在藩国、军府手里的不设；
//   · 储君就藩：储君为直隶之主；没有储君时直隶真空无主(跳过选任，仿神罗大空位)，城官照常考选、赋税全归中央、无君不立军团；
//     储君即位后直隶空出，等新储君(不传储君的子女)；
//   · 离了内阁阶段、失了那座京城、改了政体：第二直隶改回普通行政区。
public static partial class MultiCapitalSystem
{
    // 第二直隶(不调 GetKingdomType：类别判定 CalcKingdomType 会调到这里)
    public static bool IsZhili(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        long owner = kingdom.GetOrCreate().zhili_empire_id;
        return owner >= 0 && kingdom.GetEmpire()?.data?.id == owner;
    }

    public static bool IsVacantZhili(Kingdom kingdom) => IsZhili(kingdom) && !kingdom.hasKing();

    // 名号前缀：京名(如"南京"，配类别"直隶")
    public static string ZhiliFront(Kingdom kingdom) => IsZhili(kingdom) ? JingNameOf(kingdom.capital) : null;

    private static Kingdom CurrentZhili(Empire empire)
    {
        long id = empire?.data?.zhili_kingdom_id ?? -1L;
        Kingdom kingdom = id < 0 ? null : World.world?.kingdoms?.get(id);
        return kingdom != null && IsZhili(kingdom) && kingdom.GetEmpire() == empire ? kingdom : null;
    }

    private static void SyncZhili(Empire empire, (EmpireCore core, City city) pick)
    {
        Kingdom zhili = CurrentZhili(empire);
        if (pick.city == null)
        {
            ReleaseZhili(empire, "jing_reason_lost");
            return;
        }
        if (zhili != null && pick.city.kingdom != zhili)
        {
            ReleaseZhili(empire, "jing_reason_lost");
            zhili = null;
        }
        zhili ??= Establish(empire, pick.core, pick.city);
        if (zhili != null) SyncRuler(empire, zhili);
    }

    private static Kingdom Establish(Empire empire, EmpireCore core, City city)
    {
        Kingdom holder = city.kingdom;
        if (holder == empire.CoreKingdom)
        {
            KingdomTitle title = city.GetTitle();
            List<City> region = title == null
                ? new List<City>()
                : title.getCities().Where(c => c?.data != null && !c.isRekt() && c.kingdom == empire.CoreKingdom).ToList();
            if (!region.Contains(city)) region.Add(city);
            holder = empire.EstablishTerritorialDivision(region, title);
            if (holder == null || city.kingdom != holder) return null;
        }
        else if (holder == null || holder.GetEmpire() != empire || !holder.IsAdministrativeKingdomType() || IsZhili(holder))
            return null;
        if (holder.capital != city) holder.setCapital(city);
        holder.GetOrCreate().zhili_empire_id = empire.data.id;
        empire.data.zhili_kingdom_id = holder.id;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(holder);
        EventRecorder.Record(empire, string.Format(LM.Get("zhili_established_history"), empire.GetEmpireFullName(),
            holder.GetKingdomFullName(), RealmNamingSystem.CoreName(core)));
        LogService.LogInfo($"[EmpireCraft][直隶] {empire.GetEmpireFullName()} 设第二直隶 {holder.GetKingdomFullName()}({holder.cities.Count} 城)");
        return holder;
    }

    // 储君就藩；没有储君就空着
    private static void SyncRuler(Empire empire, Kingdom zhili)
    {
        Actor heir = empire.CoreKingdom.GetHeir();
        Actor current = zhili.hasKing() ? zhili.king : null;
        // 原来的直隶之主已即位或离开：只清掉记号，不动他的身份
        if (current != null && (current.isRekt() || !current.isAlive() || current.kingdom != zhili || current == empire.Emperor))
        {
            zhili.clearKingData();
            current = null;
        }
        if (heir == null || heir.isRekt() || !heir.isAlive())
        {
            if (current == null) return;
            zhili.removeKing();
            EventRecorder.Record(empire, string.Format(LM.Get("zhili_vacant_history"), zhili.GetKingdomFullName()));
            LogService.LogInfo($"[EmpireCraft][直隶] {zhili.GetKingdomFullName()} 无储君，空缺");
            return;
        }
        if (current == heir) return;
        // 储君已是别处之君(封国、军府)：不就藩
        if (heir.isKing() && heir.kingdom != zhili) return;
        if (current != null) zhili.removeKing();
        // 同继位就国：先卸下军职、城主之职(见 EmpireCraftKingdomBehCheckKing.MakeKingAndMoveToCapital)
        heir.removeFromArmy();
        if (heir.isCityLeader()) heir.city?.removeLeader();
        OfficeObject office = zhili.GetOffice();
        if (office != null)
        {
            office.is_local = true;
            office.meta_object = zhili;
            office.SetActor(heir);
        }
        if (zhili.king != heir && zhili.capital != null)
        {
            heir.joinCity(zhili.capital);
            zhili.setKing(heir);
        }
        if (zhili.king != heir) return;
        EventRecorder.Record(empire, string.Format(LM.Get("zhili_heir_history"), heir.getName(), zhili.GetKingdomFullName()));
        LogService.LogInfo($"[EmpireCraft][直隶] 储君 {heir.getName()} 就藩 {zhili.GetKingdomFullName()}");
    }

    // 王国行为选君之前(EmpireCraftKingdomBehCheckKing)：第二直隶由本系统管——有储君就请他就藩，没有就空着，不走选任
    public static bool HoldsVacancy(Kingdom kingdom)
    {
        if (!RealmNamingSystem.MultiCapitalEnabled || !IsZhili(kingdom)) return false;
        Empire empire = kingdom.GetEmpire();
        try
        {
            if (empire?.CoreKingdom != null) SyncRuler(empire, kingdom);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][直隶] 储君就藩失败: {exception.Message}");
        }
        return true;
    }

    private static void ReleaseZhili(Empire empire, string reasonKey)
    {
        Kingdom zhili = CurrentZhili(empire);
        if (empire?.data != null) empire.data.zhili_kingdom_id = -1L;
        if (zhili == null) return;
        Actor prince = zhili.hasKing() ? zhili.king : null;
        zhili.GetOrCreate().zhili_empire_id = -1L;
        // 储君回朝，直隶改回行政区，照常考选长官
        if (prince != null && prince == empire.CoreKingdom?.GetHeir())
        {
            zhili.removeKing();
            if (empire.CoreKingdom.capital != null) prince.joinCity(empire.CoreKingdom.capital);
        }
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(zhili);
        EventRecorder.Record(empire, string.Format(LM.Get("zhili_released_history"), empire.GetEmpireFullName(),
            zhili.GetKingdomFullName(), LM.Get(reasonKey)));
        LogService.LogInfo($"[EmpireCraft][直隶] {empire.GetEmpireFullName()} 撤第二直隶 {zhili.GetKingdomFullName()}");
    }
}
