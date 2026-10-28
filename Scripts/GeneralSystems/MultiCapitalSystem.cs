using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 多京(方案第 3 步，用户 2026-10-20 拍板)：联合天下里的华夏律令制帝国，三省六部(县道)阶段可设多京——
//   · 只有天下的正统(据有整个天下一半以上城)才行多京；
//   · 主京 = 本国都城(在本帝国法理之内，见 CoreCapitalRestorationSystem)，称京师；
//   · 陪京 = 同一天下别的帝国法理的首都：控制那座城、且据有那个帝国法理三分之二城；按据城多少挑，连京师最多四京；
//   · 京名按相对京师的方位称东京、西京、南京、北京(四方各用一次)，城名不改；
//   · 效果：各京当行政治所(与都城同起点，见 AdministrativeControlSystem)、京城自然稳定 +5、画宫城(宫城此后留着)；
//   · 设、撤都自动(每年随帝国年度结算)：京城失了、那个帝国法理退出天下、离了三省六部阶段、改了政体即撤。
// 模组设置「多京与直隶」关掉时不设(已设的撤掉)。内阁(行省)阶段改设两直隶，见 MultiCapitalSystem.Zhili。
public static partial class MultiCapitalSystem
{
    public const int MaxCapitals = 4;
    public const float JingStability = 5f;

    // 律令制华夏帝国、本帝国法理在联合天下里
    private static EmpireCore RealmLead(Empire empire)
    {
        EmpireCore own = EmpireCoreManager.Get(empire);
        EmpireCore lead = own == null ? null : TianxiaSystem.Lead(own);
        return lead != null && TianxiaSystem.IsJoint(lead) ? lead : null;
    }

    private static bool Eligible(Empire empire) =>
        RealmNamingSystem.MultiCapitalEnabled && empire?.data != null && !empire.isRekt() && !empire.IsArchived() &&
        empire.CoreKingdom?.capital != null && empire.CoreKingdom.GetRegime()?.type == RegimeType.LvLing &&
        RealmNamingSystem.IsHuaxia(empire) && !RepublicSystem.IsRepublic(empire) && RealmLead(empire) != null &&
        // 天下的正统(据有整个天下一半以上城)才行多京(用户 2026-10-20 定)
        RealmNamingSystem.HoldsHalfOfRealm(empire, RealmLead(empire));

    // 这座城是京(京师或陪京)
    public static bool IsJing(City city)
    {
        Empire empire = city?.kingdom?.GetEmpire();
        return empire?.data?.jing != null && empire.data.jing.Any(record => record != null && record.city_id == city.data.id);
    }

    // 陪京(不含京师)：行政治所用
    public static IEnumerable<City> AuxiliaryCities(Kingdom realm)
    {
        Empire empire = realm?.GetEmpire();
        if (empire?.data?.jing == null || empire.CoreKingdom != realm) yield break;
        foreach (JingRecord record in empire.data.jing)
        {
            if (record == null || string.IsNullOrEmpty(record.direction)) continue;
            City city = World.world?.cities?.get(record.city_id);
            if (city?.data != null && !city.isRekt()) yield return city;
        }
    }

    public static string JingName(JingRecord record) =>
        record == null ? "" : string.IsNullOrEmpty(record.direction) ? LM.Get("jing_capital") : LM.Get("jing_name_" + record.direction);

    // 这座城的京名(不是京返回空)
    public static string JingNameOf(City city)
    {
        Empire empire = city?.kingdom?.GetEmpire();
        JingRecord record = empire?.data?.jing?.FirstOrDefault(r => r != null && r.city_id == city.data.id);
        return record == null ? "" : JingName(record);
    }

    public static float StabilityBonus(City city) => IsJing(city) ? JingStability : 0f;

    public static string DescribeStability(City city) => string.Format(LM.Get("jing_stability_detail"), JingNameOf(city), JingStability);

    // 每年(RealmNamingSystem.Yearly)
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null) return;
        empire.data.jing ??= new List<JingRecord>();
        HuaxiaCourtStage stage = Eligible(empire) ? DynastyNaming.StageOf(empire) : HuaxiaCourtStage.None;
        if (stage == HuaxiaCourtStage.Tang)
        {
            ReleaseZhili(empire, "jing_reason_stage");
            Arrange(empire, CandidateCapitals(empire, MaxCapitals - 1));
        }
        else if (stage == HuaxiaCourtStage.Ming)
        {
            // 内阁(行省)阶段：只留第二直隶那一京(见 MultiCapitalSystem.Zhili)
            List<(EmpireCore core, City city)> picks = CandidateCapitals(empire, 1);
            Arrange(empire, picks, "jing_reason_zhili");
            SyncZhili(empire, picks.Count > 0 ? picks[0] : default);
        }
        else
        {
            ReleaseZhili(empire, "jing_reason_stage");
            if (empire.data.jing.Count > 0) Arrange(empire, new List<(EmpireCore, City)>(), "jing_reason_stage");
        }
    }

    // 陪京候选：同一天下别的正式帝国法理，首都在本帝国、据其三分之二城；据城多的在前
    private static List<(EmpireCore core, City city)> CandidateCapitals(Empire empire, int limit)
    {
        var result = new List<(EmpireCore core, City city, int held)>();
        EmpireCore own = EmpireCoreManager.Get(empire);
        EmpireCore lead = RealmLead(empire);
        City main = empire.CoreKingdom.capital;
        if (lead == null || main == null) return new List<(EmpireCore, City)>();
        // 迁过都的(明成祖迁北京)：本帝国法理的旧都留作陪京，排在最前(南京)
        City old = own == null || own.former_capital < 0 ? null : World.world?.cities?.get(own.former_capital);
        if (old?.data != null && !old.isRekt() && old != main && old.kingdom?.GetEmpire() == empire)
            result.Add((own, old, int.MaxValue));
        foreach (EmpireCore core in TianxiaSystem.Members(lead))
        {
            if (core == own || !RealmNamingSystem.IsFormalCore(core)) continue;
            City seat = EmpireCoreManager.GetRepresentativeCity(core);
            if (seat?.data == null || seat.isRekt() || seat == main || seat.kingdom?.GetEmpire() != empire) continue;
            int held = EmpireCoreControl.HeldCities(empire, core).Count;
            int total = EmpireCoreManager.GetCities(core).Count;
            if (!RealmNameRules.HoldsShare(held, total)) continue;
            result.Add((core, seat, held));
        }
        return result.OrderByDescending(entry => entry.held).ThenBy(entry => entry.core.id).Take(limit)
            .Select(entry => (entry.core, entry.city)).ToList();
    }

    // 定下各京：与上一年比，新设的、撤掉的各记一笔
    private static void Arrange(Empire empire, List<(EmpireCore core, City city)> picks, string reasonKey = "jing_reason_lost")
    {
        City main = empire.CoreKingdom.capital;
        var records = new List<JingRecord>();
        if (picks.Count > 0)
        {
            string[] dirs = RealmNameRules.AssignDirections(
                picks.Select(pick => pick.city.city_center.x - main.city_center.x).ToList(),
                picks.Select(pick => pick.city.city_center.y - main.city_center.y).ToList());
            double now = World.world.getCurWorldTime();
            records.Add(Keep(empire, main.data.id, EmpireCoreManager.Get(empire)?.id ?? -1L, "", now));
            for (int i = 0; i < picks.Count; i++)
                if (!string.IsNullOrEmpty(dirs[i]))
                    records.Add(Keep(empire, picks[i].city.data.id, picks[i].core.id, dirs[i], now));
            if (records.Count < 2) records.Clear();
        }
        List<JingRecord> old = empire.data.jing;
        bool changed = old.Count != records.Count ||
                       old.Where(r => r != null).Any(r => !records.Any(n => n.city_id == r.city_id && n.direction == r.direction));
        if (!changed) return;
        foreach (JingRecord gone in old.Where(r => r != null && !string.IsNullOrEmpty(r.direction) &&
                                                   !records.Any(n => n.city_id == r.city_id && n.direction == r.direction)).ToList())
        {
            City city = World.world?.cities?.get(gone.city_id);
            EventRecorder.Record(empire, string.Format(LM.Get("jing_removed_history"), empire.GetEmpireFullName(), JingName(gone),
                city?.GetCityName() ?? "", LM.Get(reasonKey)));
        }
        foreach (JingRecord added in records.Where(r => !string.IsNullOrEmpty(r.direction) &&
                                                        !old.Any(o => o != null && o.city_id == r.city_id && o.direction == r.direction)))
        {
            City city = World.world?.cities?.get(added.city_id);
            if (city?.data == null) continue;
            EventRecorder.Record(empire, string.Format(LM.Get("jing_added_history"), empire.GetEmpireFullName(), city.GetCityName(),
                JingName(added), RealmNamingSystem.CoreName(EmpireCoreManager.Get(added.core_id))));
            // 宫城：京城画宫城，此后留着(同易主不拆宫城，见 PureMapPatch.HasPalace)
            var extra = city.GetOrCreate();
            extra.palace_kept = true;
            extra.palace_owner = city.kingdom?.id ?? -1L;
            PureMapPatch.Redraw(city);
        }
        empire.data.jing = records;
        AdministrativeControlSystem.Refresh(empire.CoreKingdom);
        LogService.LogInfo($"[EmpireCraft][多京] {empire.GetEmpireFullName()} " +
                           (records.Count == 0 ? "罢多京" : string.Join("；", records.Select(r =>
                               $"{JingName(r)} {World.world?.cities?.get(r.city_id)?.GetCityName()}(据 {RealmNamingSystem.CoreName(EmpireCoreManager.Get(r.core_id))})"))));
    }

    // 沿用旧的设立时间
    private static JingRecord Keep(Empire empire, long cityId, long coreId, string direction, double now)
    {
        JingRecord existing = empire.data.jing?.FirstOrDefault(r => r != null && r.city_id == cityId && r.direction == direction);
        return new JingRecord { city_id = cityId, core_id = coreId, direction = direction, since = existing?.since ?? now };
    }
}
