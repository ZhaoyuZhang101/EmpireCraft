using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 流亡政府(用户 2026-11-10 "流亡政府机制")：近代国家亡于外敌时不就此灭亡，元首带着政府流亡到盟国或同仇敌忾的国家，
// 等盟军打回来再还都。史例：大韩民国临时政府(上海→重庆)、海尔·塞拉西流亡英国、二战中波兰、荷兰、挪威、比利时、
// 希腊、南斯拉夫的政府流亡伦敦，菲律宾自治邦政府流亡华盛顿。
//   · 什么时候流亡：近代国家(现代政体、君主立宪，或科技进了电气时代，见 ExileRules.ModernEnough)在战争里丢掉最后一座城，
//     又找得到东道国(与占领者交战的、盟国、交情很好的，见 ExileRules.HostScore)。城易手之前(CityPatch.joinAnotherKingdom 的前置)
//     把元首、储君与要员带出城：原版城易手时城里的人全归新主，不先带出来元首就成了占领者的人；
//   · 流亡期间：没有城、没有兵，随员是无城之人(仍算本国的人，原版不把文明国家的人并进别的城；无小人模式的流民收拢也跳过无城之国)，
//     住在东道国都城外；外交关系、战争状态照旧；
//   · 待收复的旧城：最后丢的那座城，连同流亡时被占领者占着的本国法理之城(剧本开局流亡的，是认领地区里的城)；
//   · 还都：这些旧城落到东道国、盟国或正与占领者交战的国家手里(ExileRules.Liberator)，就交还给流亡政府；第一座交还的城作都城，
//     政府还都、随员入城；还都后 LiberationYears 年内盟军打回的旧城照样交还；
//   · 东道国亡了、与流亡政府交战、站到占领者一边：另投别国，找不到就解散(随员入籍东道国)；流亡满 ExpireYears 年还没还都也解散。
// 剧本可开局即流亡(大韩民国临时政府寄居中华民国，见 WorldMapPolity.exile 与 WorldMapScenario.BuildExiles)。
public static class ExileGovernmentSystem
{
    // 自己往回交城时不再触发流亡判定
    private static bool _restoring;

    public static bool IsExiled(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && kingdom.GetOrCreate().exile_since >= 0d;

    public static Kingdom Host(Kingdom exile)
    {
        if (exile?.data == null) return null;
        long id = exile.GetOrCreate().exile_host_id;
        Kingdom host = id < 0 ? null : World.world?.kingdoms?.get(id);
        return host?.data == null || host.isRekt() ? null : host;
    }

    private static Kingdom Enemy(Kingdom exile)
    {
        long id = exile?.data == null ? -1L : exile.GetOrCreate().exile_enemy_id;
        Kingdom enemy = id < 0 ? null : World.world?.kingdoms?.get(id);
        return enemy?.data == null || enemy.isRekt() ? null : enemy;
    }

    #region 关系判定

    // 一国所在的一方：帝国按中央；殖民地按宗主；傀儡按主国
    private static Kingdom Realm(Kingdom kingdom)
    {
        if (kingdom?.data == null) return null;
        Kingdom root = PuppetStateSystem.Master(kingdom) ?? ColonySystem.MetropoleOf(kingdom) ?? kingdom;
        Empire empire = root.GetEmpire();
        return empire?.CoreKingdom != null && !empire.isRekt() ? empire.CoreKingdom : root;
    }

    private static bool AtWar(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null || a == b) return false;
        if (a.isInWarWith(b)) return true;
        Kingdom ra = Realm(a), rb = Realm(b);
        return ra != null && rb != null && ra != rb && ra.isInWarWith(rb);
    }

    private static bool Allied(Kingdom a, Kingdom b) =>
        a?.data != null && b?.data != null && a.hasAlliance() && b.hasAlliance() && a.getAlliance() == b.getAlliance();

    // 站在占领者一边：占领者本身、同一帝国、殖民地、傀儡、同盟
    private static bool SidesWith(Kingdom kingdom, Kingdom enemy)
    {
        if (kingdom?.data == null || enemy?.data == null) return false;
        if (kingdom == enemy || Realm(kingdom) == Realm(enemy)) return true;
        return Allied(kingdom, enemy) || Allied(Realm(kingdom), Realm(enemy));
    }

    private static int Opinion(Kingdom a, Kingdom b) => World.world?.diplomacy?.getOpinion(a, b)?.total ?? 0;

    private static int HostScore(Kingdom candidate, Kingdom exile, Kingdom enemy) =>
        ExileRules.HostScore(enemy != null && AtWar(candidate, enemy), Allied(candidate, exile), Opinion(candidate, exile),
            AtWar(candidate, exile), enemy != null && SidesWith(candidate, enemy));

    private static bool CanHost(Kingdom candidate, Kingdom exile)
    {
        if (candidate?.data == null || candidate.isRekt() || candidate.wild || !candidate.isCiv() || candidate == exile) return false;
        if (candidate.capital == null || candidate.capital.isRekt() || (candidate.cities?.Count ?? 0) == 0) return false;
        // 东道国得自己做主：帝国中央或独立国；流亡政府、傀儡、殖民地不收留
        if (candidate.IsInEmpire() && !candidate.IsEmpire()) return false;
        return !IsExiled(candidate) && !PuppetStateSystem.IsPuppet(candidate) && !ColonySystem.IsColony(candidate);
    }

    internal static Kingdom PickHost(Kingdom exile, Kingdom enemy, Kingdom except = null)
    {
        Kingdom best = null;
        int bestScore = int.MinValue;
        double bestPower = -1d;
        foreach (Kingdom candidate in World.world.kingdoms.list)
        {
            if (candidate == except || !CanHost(candidate, exile)) continue;
            int score = HostScore(candidate, exile, enemy);
            if (!ExileRules.AcceptableHost(score)) continue;
            double power = candidate.GetNationalPower();
            if (score < bestScore || score == bestScore && power <= bestPower) continue;
            best = candidate;
            bestScore = score;
            bestPower = power;
        }
        return best;
    }

    #endregion

    #region 流亡


    // CityPatch.joinAnotherKingdom 的前置里、城真正易手之前调：这座城一走，本国就一座城都没有了
    public static void BeforeCityLeaves(City city, Kingdom loser, Kingdom taker, bool captured)
    {
        if (_restoring || city?.data == null || loser?.data == null || taker?.data == null || loser == taker) return;
        try
        {
            if (loser.wild || !loser.isCiv() || loser.cities == null || loser.cities.Count != 1 || loser.cities[0] != city) return;
            if (IsExiled(loser) || PuppetStateSystem.IsPuppet(loser) || ColonySystem.IsColony(loser) || !loser.hasKing()) return;
            // 战争里丢的：攻占，或与接手的一方正在交战
            if (!captured && !AtWar(loser, taker)) return;
            // 帝国的成员、行政区不流亡(城归了谁是帝国里的事)；帝国中央连同成员一座城都不剩才算
            if (loser.IsInEmpire() && !loser.IsEmpire()) return;
            Empire empire = loser.GetEmpire();
            if (empire?.kingdoms_list != null && empire.kingdoms_list.Any(member => member != null && member != loser && !member.isRekt() &&
                                                                                    (member.cities?.Count ?? 0) > 0)) return;
            // 造反的、流寇不流亡
            if (loser.IsFactionRebelling() || loser.IsLocalRebelling() || WorldMap.ScenarioEventSystem.Roving(loser)) return;
            if (!StateModernization.IsModernPower(loser)) return;
            Kingdom enemy = Realm(taker);
            Kingdom host = PickHost(loser, enemy);
            if (host == null) return;
            var lost = new List<City> { city };
            // 待收复：流亡时被占领者一边占着的本国法理之城
            HashSet<long> titles = new HashSet<long>((loser.GetControlledTitle() ?? new List<KingdomTitle>())
                .Concat(new[] { loser.GetMainTitle(), city.GetTitle() }).Where(title => title?.data != null).Select(title => title.data.id));
            foreach (City other in World.world.cities.list)
            {
                if (other?.data == null || other.isRekt() || other == city || other.kingdom == null) continue;
                KingdomTitle title = other.GetTitle();
                if (title?.data != null && titles.Contains(title.data.id) && SidesWith(other.kingdom, enemy)) lost.Add(other);
            }
            Begin(loser, host, enemy, lost.Select(entry => entry.data.id), city);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][流亡政府] {loser?.data?.name} 流亡失败: {exception.Message}");
        }
    }

    // 开始流亡：记下东道国、占领者、待收复的旧城，随员出城去东道国都城。leaving 是正在丢的城(剧本开局流亡的为空，quiet 不记史书)
    public static void Begin(Kingdom exile, Kingdom host, Kingdom enemy, IEnumerable<long> claims, City leaving, bool quiet = false)
    {
        KingdomExtension.KingdomExtraData data = exile.GetOrCreate();
        data.exile_host_id = host.data.id;
        data.exile_since = World.world.getCurWorldTime();
        data.exile_enemy_id = enemy?.data?.id ?? -1L;
        data.exile_returned_at = -1d;
        data.exile_city_ids = (data.exile_city_ids ?? new List<long>()).Concat(claims ?? Enumerable.Empty<long>()).Distinct().ToList();
        Evacuate(exile, leaving, host);
        if (quiet) return;
        string text = string.Format(LM.Get("exile_begin_history"), exile.GetKingdomName(), host.GetKingdomName(),
            host.capital?.GetCityName() ?? host.GetKingdomName());
        EventRecorder.Record(exile, text, exile.king);
        TranslateHelper.LogEventMessage(text, host);
        LogService.LogInfo($"[EmpireCraft][流亡政府] {exile.GetKingdomName()} 流亡 {host.GetKingdomName()}，待收复 {data.exile_city_ids.Count} 城");
    }

    // 随员：元首、储君、元首的家人，再按声望取本国的要人(官员、名将名臣……)，至多 MaxEntourage 人
    private static List<Actor> Entourage(Kingdom exile)
    {
        Actor king = exile.king, heir = exile.GetHeir();
        return exile.units.ToList()
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.asset?.civ == true && !actor.asset.is_boat &&
                            actor.kingdom == exile)
            .Select(actor => (actor, rank: actor == king ? 0 : actor == heir ? 1 :
                king?.clan != null && actor.clan == king.clan ? 2 : actor == king?.lover ? 2 :
                CityPopulationSystem.IsNotable(actor) ? 3 : 9))
            .Where(entry => entry.rank < 9)
            .OrderBy(entry => entry.rank).ThenByDescending(entry => entry.actor.renown)
            .Take(ExileRules.MaxEntourage).Select(entry => entry.actor).ToList();
    }

    private static void Evacuate(Kingdom exile, City leaving, Kingdom host)
    {
        WorldTile at = host?.capital?.getTile();
        foreach (Actor actor in Entourage(exile))
        {
            try
            {
                // 离城不离国：无城之人仍算本国的人(原版 joinCity(null) 不改国家)
                if (actor.city != null) actor.joinCity(null);
                if (at != null) actor.setCurrentTilePosition(at);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][流亡政府] {actor.getName()} 随行失败: {exception.Message}");
            }
        }
    }

    #endregion

    #region 每月

    private static object _world;
    private static double _lastMonth = -1d;

    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = -1d;
    }

    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.kingdoms == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _lastMonth = -1d;
        }
        double now = world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (Kingdom kingdom in world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
            try
            {
                if (data.exile_since >= 0d) Upkeep(kingdom, data);
                else if (data.exile_returned_at >= 0d)
                {
                    // 还都后的收复期：盟军打回来的旧城照样交还；期满不再记
                    if (ExileRules.InLiberationWindow(Date.getYearsSince(data.exile_returned_at))) Liberate(kingdom, data);
                    else
                    {
                        data.exile_returned_at = -1d;
                        data.exile_city_ids = null;
                    }
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][流亡政府] {kingdom.data.name} 月结失败: {exception.Message}");
            }
        }
    }

    private static void Upkeep(Kingdom exile, KingdomExtension.KingdomExtraData data)
    {
        // 别的途径拿回了城(剧情献城、自己打回来)：就此还都
        if ((exile.cities?.Count ?? 0) > 0)
        {
            Return(exile, data, exile.capital ?? exile.cities[0]);
            return;
        }
        if (!exile.hasKing() && !Succeed(exile))
        {
            Dissolve(exile, data, "exile_dissolved_leaderless_history");
            return;
        }
        if (ExileRules.Expired(Date.getYearsSince(data.exile_since)))
        {
            Dissolve(exile, data, "exile_dissolved_expired_history");
            return;
        }
        Kingdom host = Host(exile), enemy = Enemy(exile);
        if (!CanHost(host, exile) || ExileRules.HostScore(false, false, 0, AtWar(host, exile), enemy != null && SidesWith(host, enemy)) == int.MinValue)
        {
            Kingdom next = PickHost(exile, enemy, host);
            if (next == null)
            {
                Dissolve(exile, data, "exile_dissolved_homeless_history");
                return;
            }
            data.exile_host_id = next.data.id;
            Evacuate(exile, null, next);
            string text = string.Format(LM.Get("exile_move_history"), exile.GetKingdomName(), next.GetKingdomName(),
                next.capital?.GetCityName() ?? next.GetKingdomName());
            EventRecorder.Record(exile, text, exile.king);
            TranslateHelper.LogEventOnly(text, next);
        }
        Liberate(exile, data);
    }

    // 元首死了：随员里的储君、年长的要人接任
    private static bool Succeed(Kingdom exile)
    {
        Actor heir = exile.GetHeir();
        Actor next = heir != null && heir.isAlive() && heir.kingdom == exile && heir.isAdult() ? heir
            : exile.units.Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                         actor.asset?.civ == true && !actor.asset.is_boat && actor.kingdom == exile)
                .OrderByDescending(actor => actor.renown).FirstOrDefault();
        if (next == null) return false;
        exile.setKing(next);
        return exile.hasKing();
    }

    // 待收复的旧城落在东道国、盟国、与占领者交战的国家手里：交还
    private static void Liberate(Kingdom exile, KingdomExtension.KingdomExtraData data)
    {
        if (data.exile_city_ids == null || data.exile_city_ids.Count == 0) return;
        Kingdom host = Host(exile), enemy = Enemy(exile);
        var returned = new List<City>();
        foreach (long id in data.exile_city_ids.ToList())
        {
            City city = World.world.cities.get(id);
            if (city?.data == null || city.isRekt()) continue;
            Kingdom holder = city.kingdom;
            if (holder == null || holder == exile || holder.wild) continue;
            bool liberator = ExileRules.Liberator(holder == host || Realm(holder) == host, Allied(holder, exile),
                enemy != null && AtWar(holder, enemy), enemy != null && SidesWith(holder, enemy), AtWar(holder, exile));
            if (!liberator) continue;
            // 不夺人家的都城
            if (holder.capital == city && (holder.cities?.Count ?? 0) > 1) continue;
            returned.Add(city);
        }
        if (returned.Count == 0) return;
        _restoring = true;
        try
        {
            foreach (City city in returned)
            {
                Kingdom from = city.kingdom;
                city.joinAnotherKingdom(exile);
                if (city.kingdom == exile)
                    LogService.LogInfo($"[EmpireCraft][流亡政府] {from?.GetKingdomName()} 把 {city.GetCityName()} 交还 {exile.GetKingdomName()}");
            }
        }
        finally
        {
            _restoring = false;
        }
        City seat = returned.FirstOrDefault(city => city.kingdom == exile);
        if (seat != null && IsExiled(exile)) Return(exile, data, seat);
    }

    // 还都：第一座交还的城作都城，随员入城
    private static void Return(Kingdom exile, KingdomExtension.KingdomExtraData data, City seat)
    {
        data.exile_since = -1d;
        data.exile_host_id = -1L;
        data.exile_returned_at = World.world.getCurWorldTime();
        if (seat != null && seat.kingdom == exile)
        {
            exile.setCapital(seat);
            foreach (Actor actor in exile.units.ToList())
                if (actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.city == null && actor.kingdom == exile &&
                    actor.asset?.civ == true && !actor.asset.is_boat)
                    actor.joinCity(seat);
        }
        string text = string.Format(LM.Get("exile_return_history"), exile.GetKingdomName(), seat?.GetCityName() ?? "");
        EventRecorder.Record(exile, text, exile.king);
        TranslateHelper.LogEventMessage(text, exile);
    }

    // 解散：随员入籍东道国(进东道国都城)，本国随之无人而亡
    private static void Dissolve(Kingdom exile, KingdomExtension.KingdomExtraData data, string historyKey)
    {
        Kingdom host = Host(exile);
        string text = string.Format(LM.Get(historyKey), exile.GetKingdomName(), host?.GetKingdomName() ?? "");
        EventRecorder.Record(exile, text, exile.king);
        TranslateHelper.LogEventOnly(text, exile);
        data.exile_since = -1d;
        data.exile_host_id = -1L;
        data.exile_city_ids = null;
        City refuge = host?.capital;
        if (refuge == null || refuge.isRekt()) return;
        foreach (Actor actor in exile.units.ToList())
            if (actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.city == null && actor.asset?.civ == true && !actor.asset.is_boat)
                actor.joinCity(refuge);
    }

    #endregion
}
