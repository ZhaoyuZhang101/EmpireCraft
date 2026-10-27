using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 皇帝出行(纯净地图模式，皇帝标记见 EmperorMarkerRenderer)：皇帝的实体留在原地，地图上的皇帝标记像兵牌一样单独走——
//   · 微服私访：太平年间，皇帝离京去本国的另一座城(三十块地以内)，住三个月再回銮；
//   · 出访交流：去同一帝国的成员国都城，住两个月再回；
//   · 御驾亲征：打仗时随本国兵力最强的一支进攻的军出征，跟着这支军走；仗打完、这支军没了或亲征满三年就回銮；
//   · 每月按机会决定是否出行，两次出行至少隔三年；明君、暴君更常出巡亲征，庸弱、昏聩之君少出门；成年才出行；
//   · 走路：每个世界秒在路上走 0.2 块地(一月一块)，路外减半，按军团的寻路走(优先走路，见 CorpsSystem.PathFor)；
//   · 出发时记入史书；换了皇帝(驾崩、废立)行程作罢，标记回到新君所在的城。
// 行程存在帝国存档里(EmpireData.tour_*)。
public static class EmperorTourSystem
{
    private const float ZonesPerSecond = 0.2f, OffRoadShare = 0.5f;
    private const float CooldownYears = 3f, MaxCampaignYears = 3f;
    // 每月出行的机会
    private const float CampaignChance = 0.02f, TourChance = 0.01f, VisitChance = 0.006f;
    private const int TourStayMonths = 3, VisitStayMonths = 2, TourMaxZones = 30;

    public const string Tour = "tour", Visit = "visit", Campaign = "campaign";

    private static readonly Dictionary<long, (int target, List<int> path)> Paths = new();
    private static readonly Dictionary<long, float> Progress = new();
    private static double _lastSecond = -1d, _lastMonth = -1d;
    private static object _world;

    public static void Tick()
    {
        if (!PureMapMode.Enabled || ModClass.EMPIRE_MANAGER == null || World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Paths.Clear();
            Progress.Clear();
            _lastSecond = _lastMonth = -1d;
        }
        double now = World.world.getCurWorldTime();
        bool month = _lastMonth < 0d || now < _lastMonth || Date.getMonthsSince(_lastMonth) >= 1;
        bool second = _lastSecond < 0d || now < _lastSecond || now - _lastSecond >= 1d;
        if (!month && !second) return;
        if (month) _lastMonth = now;
        if (second) _lastSecond = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            try { Update(empire, month, second, now); }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][皇帝出行] 结算失败: {exception.Message}");
            }
        }
    }

    // 皇帝出行在外时所在的地块(在宫里返回 null，标记画在城上)
    public static TileZone Away(Empire empire) =>
        empire?.data == null || string.IsNullOrEmpty(empire.data.tour_kind) ? null : CorpsSystem.ZoneById(empire.data.tour_zone);

    // 画皇帝标记用：出行途中按进度画在所在地块与路线上下一块之间(一块地要走好几秒，只在换地块时跳一下会一卡一卡的)；
    // 在宫里返回 null(画在城上)
    public static Vector3? DrawPosition(Empire empire)
    {
        TileZone zone = Away(empire);
        if (zone?.centerTile == null) return null;
        Vector3 here = zone.centerTile.posV;
        long id = empire.data.id;
        if (!Paths.TryGetValue(id, out var cached) || cached.path == null || cached.path.Count < 2 || cached.path[0] != zone.id)
            return here;
        TileZone next = CorpsSystem.ZoneById(cached.path[1]);
        if (next?.centerTile == null) return here;
        Progress.TryGetValue(id, out float progress);
        return Vector3.Lerp(here, next.centerTile.posV, Mathf.Clamp01(progress));
    }

    public static City Home(Empire empire)
    {
        Actor emperor = empire?.Emperor;
        return emperor?.city?.data != null && !emperor.city.isRekt() ? emperor.city : empire?.CoreKingdom?.capital;
    }

    private static void Update(Empire empire, bool month, bool second, double now)
    {
        var data = empire?.data;
        if (data == null) return;
        Actor emperor = empire.Emperor;
        if (emperor?.data == null || emperor.isRekt() || !emperor.isAlive() || RepublicSystem.IsRepublic(empire))
        {
            Clear(empire, false);
            return;
        }
        if (!string.IsNullOrEmpty(data.tour_kind) && data.tour_emperor != emperor.id) Clear(empire, false);
        TileZone home = Home(empire)?.getTile()?.zone;
        if (home == null) return;
        if (string.IsNullOrEmpty(data.tour_kind))
        {
            if (month) TryDepart(empire, emperor, home);
            return;
        }
        if (month) CheckStay(empire, now);
        if (second) Move(empire, home, now);
    }

    private static void TryDepart(Empire empire, Actor emperor, TileZone home)
    {
        var data = empire.data;
        if (RealisticAgeSystem.IsMinor(emperor) || data.tour_last >= 0d && Date.getYearsSince(data.tour_last) < CooldownYears) return;
        Kingdom core = empire.CoreKingdom;
        if (core?.data == null) return;
        float mood = RulerQualitySystem.Of(emperor) switch
        {
            RulerQualitySystem.Grade.Wise => 1.5f,
            RulerQualitySystem.Grade.Tyrant => 1.3f,
            RulerQualitySystem.Grade.Weak => 0.4f,
            RulerQualitySystem.Grade.Fatuous => 0.6f,
            RulerQualitySystem.Grade.Dissolute => 0.8f,
            _ => 1f
        };
        if (CorpsSystem.IsAtWar(core))
        {
            if (UnityEngine.Random.value >= CampaignChance * mood) return;
            CorpsData corps = CorpsSystem.Of(core)
                .Where(item => item.task == "attack" && !item.embarked && CorpsSystem.SameLand(home, CorpsSystem.Zone(item)))
                .OrderByDescending(item => item.strength).FirstOrDefault();
            if (corps == null) return;
            Start(empire, emperor, home, Campaign, -1L, corps.id);
            Record(empire, emperor, "emperor_campaign_history", CorpsSystem.Name(corps));
            return;
        }
        float roll = UnityEngine.Random.value;
        if (roll < TourChance * mood)
        {
            City city = core.cities?.Where(item => item?.data != null && !item.isRekt() && item.getTile()?.zone is { } zone &&
                                                   zone != home && CorpsSystem.ZoneDistance(zone, home) <= TourMaxZones &&
                                                   CorpsSystem.SameLand(zone, home)).ToList() is { Count: > 0 } cities
                ? cities[UnityEngine.Random.Range(0, cities.Count)]
                : null;
            if (city == null) return;
            Start(empire, emperor, home, Tour, city.data.id, -1L);
            Record(empire, emperor, "emperor_tour_history", city.GetCityName());
        }
        else if (roll < (TourChance + VisitChance) * mood)
        {
            List<Kingdom> members = World.world.kingdoms.list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                kingdom != core && kingdom.GetEmpire() == empire && kingdom.capital?.getTile()?.zone is { } zone &&
                CorpsSystem.SameLand(zone, home)).ToList();
            if (members.Count == 0) return;
            Kingdom host = members[UnityEngine.Random.Range(0, members.Count)];
            Start(empire, emperor, home, Visit, host.capital.data.id, -1L);
            Record(empire, emperor, "emperor_visit_history", host.data.name);
        }
    }

    private static void Start(Empire empire, Actor emperor, TileZone home, string kind, long city, long corps)
    {
        var data = empire.data;
        data.tour_kind = kind;
        data.tour_zone = home.id;
        data.tour_city = city;
        data.tour_corps = corps;
        data.tour_since = World.world.getCurWorldTime();
        data.tour_arrived = -1d;
        data.tour_returning = false;
        data.tour_emperor = emperor.id;
        Paths.Remove(empire.data.id);
        Progress.Remove(empire.data.id);
    }

    private static void Record(Empire empire, Actor emperor, string key, string where)
    {
        string text = string.Format(LM.Get(key), emperor.getName(), where);
        empire.RecordHistory(directContent: text, actorId: emperor.id);
    }

    // 住满了、仗打完了、随驾的军没了：回銮
    private static void CheckStay(Empire empire, double now)
    {
        var data = empire.data;
        if (data.tour_returning) return;
        if (data.tour_kind == Campaign)
        {
            Kingdom core = empire.CoreKingdom;
            if (!CorpsSystem.IsAtWar(core) || CorpsSystem.Find(core, data.tour_corps) == null ||
                Date.getYearsSince(data.tour_since) >= MaxCampaignYears)
                data.tour_returning = true;
            return;
        }
        int stay = data.tour_kind == Visit ? VisitStayMonths : TourStayMonths;
        if (data.tour_arrived >= 0d && Date.getMonthsSince(data.tour_arrived) >= stay) data.tour_returning = true;
    }

    private static TileZone Target(Empire empire, TileZone home)
    {
        var data = empire.data;
        if (data.tour_returning) return home;
        if (data.tour_kind == Campaign) return CorpsSystem.Zone(CorpsSystem.Find(empire.CoreKingdom, data.tour_corps));
        City city = World.world.cities.get(data.tour_city);
        return city?.data != null && !city.isRekt() ? city.getTile()?.zone : null;
    }

    private static void Move(Empire empire, TileZone home, double now)
    {
        var data = empire.data;
        TileZone current = CorpsSystem.ZoneById(data.tour_zone) ?? home;
        TileZone target = Target(empire, home);
        if (target == null)
        {
            data.tour_returning = true;
            target = home;
        }
        if (current == target)
        {
            if (data.tour_returning) Clear(empire, true);
            else if (data.tour_arrived < 0d) data.tour_arrived = now;
            return;
        }
        long id = data.id;
        Progress.TryGetValue(id, out float progress);
        progress += ZonesPerSecond * (RoadNetwork.IsRoad(current) ? 1f : OffRoadShare);
        while (progress >= 1f && current != target)
        {
            progress -= 1f;
            TileZone next = Step(id, current, target);
            if (next == null) break;
            current = next;
        }
        Progress[id] = Mathf.Min(progress, 1f);
        data.tour_zone = current.id;
    }

    private static TileZone Step(long id, TileZone current, TileZone target)
    {
        if (!Paths.TryGetValue(id, out var cached) || cached.target != target.id || cached.path == null ||
            cached.path.Count < 2 || cached.path[0] != current.id)
        {
            cached = (target.id, CorpsSystem.PathFor(current, target));
            Paths[id] = cached;
        }
        // 找不到路(隔海等)：直接到
        if (cached.path == null || cached.path.Count < 2) return target;
        cached.path.RemoveAt(0);
        return CorpsSystem.ZoneById(cached.path[0]);
    }

    private static void Clear(Empire empire, bool arrived)
    {
        var data = empire?.data;
        if (data == null || string.IsNullOrEmpty(data.tour_kind)) return;
        data.tour_kind = "";
        data.tour_zone = -1;
        data.tour_city = -1L;
        data.tour_corps = -1L;
        data.tour_returning = false;
        if (arrived) data.tour_last = World.world.getCurWorldTime();
        Paths.Remove(data.id);
        Progress.Remove(data.id);
    }
}
