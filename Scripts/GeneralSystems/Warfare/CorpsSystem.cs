using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 军团(借鉴钢铁雄心)：无小人模式 + 帝国占领模式下，征召兵不再一团一个实体兵，而是合编成"军"——
// 兵力只是数字(从背景人口里抽，退伍还回，阵亡扣掉)，地图上最多一名统领实体(世界法则可关)；
// 交战、推进战线、攻城都由 FrontSystem 每月按数值结算。
//   · 军镇(中央/军区/军府，见 CityPopulationSystem 军制)按兵额折合：每 LegionsPerCorps 个旧制军团编一支军；
//   · 城市治安的守兵(CityStabilitySystem 出钱养的兵额)照旧是实体，留在城里不出征；
//   · 原版计兵数(City.countWarriors)把军团折算进去，战争 AI、投降、叛乱判定照常可用。
public static class CorpsSystem
{
    public const int LegionsPerCorps = 8;
    // 每月补员上限(满编的比例)、和平时每月退伍上限
    private const float ReinforceShare = 0.08f;
    private const float DemobilizeShare = 0.25f;
    // 不在交战的军每月恢复的组织度
    private const float RestOrg = 15f;
    private const float IdleOrg = 8f;
    // 统领开往军团所在地块的间隔(秒)；军团每秒行军几块地块(一个月约十块)
    private const float GeneralMoveInterval = 3f;
    private const int MarchZonesPerSecond = 2;

    public static bool Enabled => CityPopulationSystem.AbstractPopulationEnabled &&
                                  EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode?.isEnabled() == true;

    public static bool GeneralUnits => EmpireCraftWorldLawLibrary.empirecraft_law_corps_general?.isEnabled() != false;

    // 关闭「军团统领实体」：地图上不留任何军人实体——没有统领、城里的治安守兵只是人数、不再组建原版城市军队
    // (羽林大将军、都督、镇将、戍主等军职空缺)；被拉去当兵的名人恢复文职。城主、刺史等地方官不受影响
    public static bool NoMilitaryUnits => Enabled && !GeneralUnits;

    private static readonly List<CorpsData> Empty = new();

    public static List<CorpsData> Of(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return Empty;
        var data = kingdom.GetOrCreate();
        return data.corps ??= new List<CorpsData>();
    }

    public static IEnumerable<(Kingdom kingdom, CorpsData corps)> All()
    {
        if (World.world?.kingdoms == null) yield break;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            List<CorpsData> list = kingdom.GetOrCreate().corps;
            if (list == null) continue;
            foreach (CorpsData corps in list) yield return (kingdom, corps);
        }
    }

    public static CorpsData Find(Kingdom kingdom, long id) => Of(kingdom).FirstOrDefault(corps => corps.id == id);

    public static City Home(CorpsData corps) => World.world?.cities?.get(corps.home_city_id) is City city &&
                                                city.data != null && !city.isRekt() ? city : null;

    public static TileZone Zone(CorpsData corps) => ZoneById(corps?.zone_id ?? -1);

    public static TileZone ZoneById(int id) =>
        id >= 0 && World.world?.zone_calculator != null &&
        World.world.zone_calculator._zones_dict_id.TryGetValue(id, out TileZone zone) ? zone : null;

    // 折合旧制军团数(按现有兵力)
    public static int Equivalent(CorpsData corps) => corps.full <= 0f || corps.strength <= 0f ? 0
        : Mathf.Max(1, Mathf.RoundToInt(corps.legions * corps.strength / corps.full));

    #region 计兵数缓存

    private static readonly Dictionary<long, int> EquivalentsByCity = new();
    private static readonly Dictionary<long, int> GeneralsByCity = new();
    private static readonly Dictionary<long, (long kingdom, long corps)> GeneralIndex = new();

    public static int EquivalentAt(City city) =>
        city != null && EquivalentsByCity.TryGetValue(city.id, out int count) ? count : 0;

    public static int GeneralsAt(City city) =>
        city != null && GeneralsByCity.TryGetValue(city.id, out int count) ? count : 0;

    public static bool IsGeneral(Actor actor) => actor?.data != null && GeneralIndex.ContainsKey(actor.id);

    public static float LeviedAt(City city)
    {
        if (city == null || !Enabled) return 0f;
        float total = 0f;
        foreach (CorpsData corps in Of(city.kingdom))
            if (corps.home_city_id == city.id) total += corps.strength;
        return total;
    }

    private static void RebuildIndex()
    {
        EquivalentsByCity.Clear();
        GeneralsByCity.Clear();
        GeneralIndex.Clear();
        foreach ((Kingdom kingdom, CorpsData corps) in All())
        {
            EquivalentsByCity.TryGetValue(corps.home_city_id, out int count);
            EquivalentsByCity[corps.home_city_id] = count + Equivalent(corps);
            if (corps.general_id < 0) continue;
            GeneralIndex[corps.general_id] = (kingdom.id, corps.id);
            GeneralsByCity.TryGetValue(corps.home_city_id, out int generals);
            GeneralsByCity[corps.home_city_id] = generals + 1;
        }
    }

    public static void ResetWorldState()
    {
        EquivalentsByCity.Clear();
        GeneralsByCity.Clear();
        GeneralIndex.Clear();
        _lastSecond = _lastMonth = -1d;
        GeneralMoves.Clear();
        FrontSystem.ResetWorldState();
    }

    #endregion

    #region 每帧调度

    private static double _lastSecond = -1d, _lastMonth = -1d;
    private static object _world;
    private static readonly Dictionary<long, float> GeneralMoves = new();

    public static void Update()
    {
        if (World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            ResetWorldState();
        }
        if (!Enabled)
        {
            if (_lastMonth >= 0d) DisbandAllOnDisable();
            return;
        }
        UI.Components.CorpsMapRenderer.EnsureHost();
        double now = World.world.getCurWorldTime();
        if (_lastMonth < 0d || now < _lastMonth || Date.getMonthsSince(_lastMonth) >= 1)
        {
            bool first = _lastMonth < 0d;
            _lastMonth = now;
            try
            {
                MonthTick(now);
                if (!first) FrontSystem.MonthTick(now);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][军团] 月度结算失败: {exception}");
            }
            RebuildIndex();
        }
        if (_lastSecond < 0d || now < _lastSecond || now - _lastSecond >= 1d)
        {
            _lastSecond = now;
            try { SecondTick(); }
            catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][军团] 行军失败: {exception.Message}"); }
        }
    }

    // 关闭占领模式：各军就地退伍(兵员还回人口)，统领并回人口
    private static void DisbandAllOnDisable()
    {
        foreach ((Kingdom kingdom, CorpsData corps) in All().ToList()) Disband(kingdom, corps);
        RebuildIndex();
        _lastMonth = -1d;
    }

    #endregion

    #region 月度：征召、补员、退伍、休整

    private static void MonthTick(double now)
    {
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() ||
                Compatibility.AncientWarfareCompatibility.OwnsObject(kingdom)) continue;
            List<CorpsData> list = kingdom.GetOrCreate().corps;
            bool posts = kingdom.cities?.Any(city => city != null && !city.isRekt() &&
                CityPopulationSystem.PostOf(city, CityPopulationSystem.DoctrineOf(kingdom)) != CityPopulationSystem.ArmyPost.None) == true;
            if (!posts && (list == null || list.Count == 0)) continue;
            Mobilize(kingdom, now);
            Recover(kingdom);
            AbsorbLegacyLegions(kingdom);
        }
    }

    // 各军镇应有几支军、每支多少人；缺的当场征召，多的和平时退伍
    private static void Mobilize(Kingdom kingdom, double now)
    {
        List<CorpsData> list = Of(kingdom);
        // 家乡城市已失：改驻本国首都或别的军镇
        foreach (CorpsData corps in list)
        {
            City home = Home(corps);
            if (home != null && home.kingdom == kingdom) continue;
            City fallback = kingdom.capital ?? kingdom.cities?.FirstOrDefault(city => city != null && !city.isRekt());
            if (fallback != null) corps.home_city_id = fallback.id;
        }
        foreach (CorpsData empty in list.Where(corps => corps.strength < 1f && corps.task != "attack" && corps.task != "defend").ToList())
            Disband(kingdom, empty);
        bool war = IsAtWar(kingdom);
        CityPopulationSystem.ArmyDoctrine doctrine = CityPopulationSystem.DoctrineOf(kingdom);
        var cities = kingdom.cities.Where(city => city != null && !city.isRekt()).ToList();
        int totalSlots = 0;
        foreach (City city in cities) totalSlots += Mathf.Max(0, CityPopulationSystem.LevyTarget(city));
        float pool = CityPopulationSystem.KingdomBackground(kingdom) * doctrine.LevyShare;
        float legionSize = Mathf.Clamp(pool / Mathf.Max(1, totalSlots), 1f, CityPopulationSystem.PeoplePerLegion);
        foreach (City city in cities)
        {
            if (CityPopulationSystem.PostOf(city, doctrine) == CityPopulationSystem.ArmyPost.None) continue;
            int legions = Mathf.Max(0, CityPopulationSystem.LevyTarget(city) - CityStabilitySystem.FundedSlots(city));
            int wanted = legions <= 0 ? 0 : Mathf.CeilToInt(legions / (float)LegionsPerCorps);
            var here = list.Where(corps => corps.home_city_id == city.id && !corps.militia).ToList();
            // 每支军分到的旧制军团数
            for (int i = 0; i < Mathf.Max(wanted, here.Count); i++)
            {
                int share = i < wanted ? Mathf.Min(LegionsPerCorps, legions - i * LegionsPerCorps) : 0;
                if (i >= here.Count)
                {
                    if (share <= 0) break;
                    CorpsData raised = Raise(kingdom, city, share, share * legionSize, now);
                    if (raised == null) break;
                    here.Add(raised);
                    continue;
                }
                CorpsData corps = here[i];
                corps.legions = Mathf.Max(share, war ? corps.legions : 0);
                float full = corps.legions * legionSize;
                if (war) corps.full = Mathf.Max(corps.full, full);
                else corps.full = full;
                if (corps.full <= 0f)
                {
                    if (!war && corps.task != "attack" && corps.task != "defend") Disband(kingdom, corps);
                    continue;
                }
                if (corps.strength < corps.full) Reinforce(kingdom, corps, corps.full * ReinforceShare);
                else if (!war && corps.strength > corps.full)
                    Demobilize(kingdom, corps, Mathf.Min(corps.strength - corps.full, corps.full * DemobilizeShare + 1f));
            }
        }
        // 义军：仗打完就地解散
        if (!war)
            foreach (CorpsData corps in list.Where(corps => corps.militia).ToList()) Disband(kingdom, corps);
    }

    public static bool IsAtWar(Kingdom kingdom) =>
        kingdom != null && (kingdom.hasEnemies() || CityPopulationSystem.IsMobilizing(kingdom) ||
                            FrontSystem.Realm(kingdom) is Kingdom realm && realm != kingdom && realm.hasEnemies());

    private static int NextSerial(Kingdom kingdom)
    {
        var data = kingdom.GetOrCreate();
        return ++data.corps_serial;
    }

    private static CorpsData Raise(Kingdom kingdom, City home, int legions, float people, double now, bool militia = false)
    {
        var corps = new CorpsData
        {
            id = HelperFunc.OverallHelperFunc.IdGenerator.NextId(), serial = militia ? 0 : NextSerial(kingdom),
            kingdom_id = kingdom.id, home_city_id = home.id, legions = Mathf.Max(1, legions), full = people,
            post = (int)CityPopulationSystem.PostOf(home, CityPopulationSystem.DoctrineOf(kingdom)),
            zone_id = home.getTile()?.zone?.id ?? -1, raised_at = now, militia = militia
        };
        Reinforce(kingdom, corps, people, militia ? home : null);
        if (corps.strength < 1f) return null;
        Of(kingdom).Add(corps);
        return corps;
    }

    // 从本国各城的背景人口里征兵(义军只从本城征)
    private static void Reinforce(Kingdom kingdom, CorpsData corps, float amount, City only = null)
    {
        float need = Mathf.Min(amount, corps.full - corps.strength);
        if (need < 1f) return;
        float taken = 0f;
        for (int attempt = 0; taken < need && attempt < 12; attempt++)
        {
            City source = only ?? CityPopulationSystem.DrawKingdomCity(kingdom);
            PopGroup group = source == null ? null
                : CityPopulationSystem.DrawBackground(source, candidate => candidate.Background >= 1f);
            if (group == null) break;
            float contribution = CityPopulationSystem.RemoveBackground(group, need - taken);
            if (contribution <= 0f) break;
            LegionPopulationRules.Add(corps.population, source.id, group, contribution);
            taken += contribution;
        }
        corps.strength += taken;
        if (taken > 0f) CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 退伍：按来源还回人口
    private static void Demobilize(Kingdom kingdom, CorpsData corps, float amount)
    {
        amount = Mathf.Min(amount, corps.strength);
        if (amount <= 0f) return;
        float total = LegionPopulationRules.Total(corps.population);
        if (total > 0f)
        {
            float ratio = Mathf.Clamp01(amount / total);
            foreach (LegionPopulationContribution origin in corps.population)
            {
                if (origin == null || origin.size <= 0f) continue;
                float back = origin.size * ratio;
                origin.size -= back;
                City destination = kingdom.cities?.FirstOrDefault(city => city != null && !city.isRekt() &&
                                                                          city.id == origin.city_id) ?? Home(corps);
                if (destination == null) continue;
                CityPopulationSystem.AddBackground(destination, origin.social_class == SocialClass.Army
                    ? SocialClass.Peasant : origin.social_class, origin.culture, origin.species, origin.ideology, back);
            }
            corps.population.RemoveAll(origin => origin == null || origin.size <= 0.01f);
        }
        corps.strength = Mathf.Max(0f, corps.strength - amount);
        CityPopulationSystem.InvalidateHouseholdCaches();
    }

    public static void Disband(Kingdom kingdom, CorpsData corps)
    {
        if (corps == null) return;
        if (kingdom?.data != null && !kingdom.isRekt()) Demobilize(kingdom, corps, corps.strength);
        RemoveGeneral(corps);
        Of(kingdom).Remove(corps);
    }

    // 阵亡：兵力减少，来源按比例扣减(死去的人不回人口)
    public static void Casualties(CorpsData corps, float dead)
    {
        if (corps == null || dead <= 0f) return;
        corps.strength = Mathf.Max(0f, corps.strength - dead);
        LegionPopulationRules.ReduceTo(corps.population, corps.strength);
    }

    private static void Recover(Kingdom kingdom)
    {
        foreach (CorpsData corps in Of(kingdom))
        {
            bool fighting = corps.task is "attack" or "defend" && FrontSystem.EngagedLastMonth(corps);
            if (!fighting) corps.org = Mathf.Min(100f, corps.org + (corps.task is "reserve" or "retreat" ? RestOrg : IdleOrg));
            if (corps.task == "retreat" && corps.org >= 50f) corps.task = "reserve";
            if (corps.task == "defend" && fighting) corps.entrench = Mathf.Min(0.3f, corps.entrench + 0.05f);
            else if (corps.task != "defend") corps.entrench = 0f;
        }
    }

    // 开启军团模式前征召的实体兵(一团一个)：超出守兵额的并入本国军团，实体并回人口
    private const int MaxAbsorbPerKingdom = 12;

    private static void AbsorbLegacyLegions(Kingdom kingdom)
    {
        int absorbed = 0;
        foreach (City city in kingdom.cities.ToList())
        {
            if (city?.units == null || city.isRekt()) continue;
            int guards = NoMilitaryUnits ? 0 : CityStabilitySystem.FundedSlots(city);
            var soldiers = city.units.Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                     actor.city == city && CityPopulationSystem.IsSoldier(actor) &&
                                                     !CityPopulationSystem.IsVehicle(actor) && !IsGeneral(actor) &&
                                                     !actor.isKing() && !actor.isCityLeader() && !actor.isFavorite() &&
                                                     actor.GetOrCreate().legion_size > 1f).ToList();
            if (soldiers.Count <= guards) continue;
            CorpsData target = Of(kingdom).Where(corps => !corps.militia)
                .OrderBy(corps => corps.home_city_id == city.id ? 0 : 1).FirstOrDefault();
            foreach (Actor soldier in soldiers.Skip(guards))
            {
                if (absorbed >= MaxAbsorbPerKingdom) return;
                var extra = soldier.GetOrCreate();
                float alive = CityPopulationSystem.LegionAlive(soldier) - 1f;
                if (target != null && extra.legion_population != null)
                {
                    foreach (LegionPopulationContribution origin in extra.legion_population)
                        if (origin != null && origin.size > 0f)
                            target.AddContribution(origin);
                    target.strength += Mathf.Max(0f, alive);
                    target.full = Mathf.Max(target.full, target.strength);
                    extra.legion_population = null;
                    extra.legion_size = extra.legion_full = 0f;
                    extra.legion_home_city_id = -1L;
                }
                if (CityPopulationSystem.DisbandIntoPopulation(soldier, city)) absorbed++;
            }
        }
    }

    private static void AddContribution(this CorpsData corps, LegionPopulationContribution origin)
    {
        LegionPopulationContribution same = corps.population.FirstOrDefault(other => other.city_id == origin.city_id &&
            other.social_class == origin.social_class && other.culture == origin.culture &&
            other.species == origin.species && other.ideology == origin.ideology);
        if (same != null) same.size += origin.size;
        else corps.population.Add(new LegionPopulationContribution
        {
            city_id = origin.city_id, social_class = origin.social_class, culture = origin.culture,
            species = origin.species, ideology = origin.ideology, size = origin.size
        });
    }

    #endregion

    #region 不留实体时的治安守兵：只是城里的人数(从本城背景人口里抽，撤防还回)

    public static float VirtualGarrison(City city) =>
        city?.data != null && city.GetOrCreate().stability is CityStabilityData state ? state.virtual_garrison : 0f;

    public static void RecruitVirtualGarrison(City city, int needed)
    {
        if (city?.data == null || needed <= 0 || city.GetOrCreate().stability is not CityStabilityData state) return;
        state.virtual_garrison_origins ??= new List<LegionPopulationContribution>();
        float taken = 0f;
        for (int attempt = 0; taken < needed && attempt < 8; attempt++)
        {
            PopGroup group = CityPopulationSystem.DrawBackground(city, candidate => candidate.Background >= 1f);
            if (group == null) break;
            float contribution = CityPopulationSystem.RemoveBackground(group, needed - taken);
            if (contribution <= 0f) break;
            LegionPopulationRules.Add(state.virtual_garrison_origins, city.id, group, contribution);
            taken += contribution;
        }
        state.virtual_garrison += taken;
        if (taken > 0f) CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 守兵超出需要(或重新打开统领实体、改回实体守兵)时退回人口
    public static void SyncVirtualGarrison(City city, CityStabilityData state)
    {
        if (city?.data == null || state == null || state.virtual_garrison <= 0f) return;
        float keep = NoMilitaryUnits ? Mathf.Max(0, state.garrison_required) : 0f;
        if (state.virtual_garrison <= keep) return;
        float release = state.virtual_garrison - keep;
        float total = LegionPopulationRules.Total(state.virtual_garrison_origins);
        if (total > 0f)
        {
            float ratio = Mathf.Clamp01(release / total);
            foreach (LegionPopulationContribution origin in state.virtual_garrison_origins)
            {
                if (origin == null || origin.size <= 0f) continue;
                float back = origin.size * ratio;
                origin.size -= back;
                CityPopulationSystem.AddBackground(city, origin.social_class == SocialClass.Army ? SocialClass.Peasant
                    : origin.social_class, origin.culture, origin.species, origin.ideology, back);
            }
            state.virtual_garrison_origins.RemoveAll(origin => origin == null || origin.size <= 0.01f);
        }
        state.virtual_garrison = keep;
        CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 城市军队解散：军职空缺，统领(多是被拉去带兵的名人)恢复文职
    public static void DisbandCityArmy(City city)
    {
        Army army = city?.army;
        if (army == null) return;
        foreach (var office in Regimes.OfficeManager.Offices.Values.Where(office => office?.meta_object == army).ToList())
            office.RemoveActor();
        Actor captain = army.getCaptain();
        foreach (Actor soldier in army.units.ToList())
        {
            if (soldier?.data == null || soldier.isRekt()) continue;
            soldier.removeFromArmy();
            if (soldier == captain || CityPopulationSystem.IsNotable(soldier)) soldier.stopBeingWarrior();
        }
        city.disbandArmy();
    }

    #endregion

    #region 义军

    // 起义、革命：在本城武装一支义军(取代原来的 count 个实体义军兵)，返回动员的人数
    public static int RaiseMilitia(City city, int count, float share)
    {
        if (city?.kingdom == null || city.isRekt() || count <= 0) return 0;
        float people = CityPopulationSystem.GetBackgroundTotal(city) * Mathf.Clamp01(share);
        if (people < 1f) return 0;
        CorpsData corps = Raise(city.kingdom, city, Mathf.Max(1, count), people, World.world.getCurWorldTime(), militia: true);
        if (corps == null) return 0;
        corps.task = "reserve";
        RebuildIndex();
        return count;
    }

    #endregion

    #region 统领实体与行军

    private static void SecondTick()
    {
        bool generals = GeneralUnits;
        foreach ((Kingdom kingdom, CorpsData corps) in All().ToList())
        {
            if (generals) EnsureGeneral(kingdom, corps);
            else RemoveGeneral(corps);
            for (int step = 0; step < MarchZonesPerSecond; step++) StepTowardTarget(corps);
            MoveGeneral(corps);
        }
    }

    private static void EnsureGeneral(Kingdom kingdom, CorpsData corps)
    {
        if (corps.general_id >= 0)
        {
            Actor existing = World.world.units.get(corps.general_id);
            if (existing?.data != null && !existing.isRekt() && existing.isAlive() && existing.kingdom == kingdom) return;
            corps.general_id = -1L;
        }
        City home = Home(corps);
        if (home == null || home.kingdom != kingdom) return;
        PopGroup group = CityPopulationSystem.DrawBackground(home, candidate => candidate.Background >= 1f &&
                                                                                  !string.IsNullOrEmpty(candidate.species));
        if (group == null) return;
        Actor general = CityPopulationSystem.SpawnFromGroup(home, group, soldier: true);
        if (general == null) return;
        CityPopulationSystem.RemoveBackground(group, 1f);
        corps.general_id = general.id;
        GeneralIndex[general.id] = (kingdom.id, corps.id);
        GeneralsByCity.TryGetValue(home.id, out int count);
        GeneralsByCity[home.id] = count + 1;
    }

    private static void RemoveGeneral(CorpsData corps)
    {
        if (corps.general_id < 0) return;
        Actor general = World.world?.units?.get(corps.general_id);
        GeneralIndex.Remove(corps.general_id);
        corps.general_id = -1L;
        if (general?.data == null || general.isRekt() || !general.isAlive()) return;
        if (general.isKing() || general.isCityLeader() || general.isFavorite()) return;
        City home = Home(corps) ?? general.city;
        if (home != null) CityPopulationSystem.DisbandIntoPopulation(general, home);
    }

    // 统领阵亡：组织度大挫，下个月补任
    public static void OnGeneralDied(Actor actor)
    {
        if (actor?.data == null || !GeneralIndex.TryGetValue(actor.id, out var key)) return;
        GeneralIndex.Remove(actor.id);
        Kingdom kingdom = World.world.kingdoms.get(key.kingdom);
        CorpsData corps = kingdom == null ? null : Find(kingdom, key.corps);
        if (corps == null) return;
        corps.general_id = -1L;
        corps.org = Mathf.Max(0f, corps.org - 20f);
        FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_general_killed"), Name(corps), actor.getName()));
    }

    // 军团在地块上一格一格挪向目标(跨海也照走，代表舟船转运)
    private static void StepTowardTarget(CorpsData corps)
    {
        TileZone target = FrontSystem.TargetZone(corps);
        if (target == null) return;
        TileZone current = Zone(corps);
        if (current == null)
        {
            corps.zone_id = target.id;
            return;
        }
        if (current == target) return;
        TileZone best = current;
        float bestDistance = ZoneDistance(current, target);
        foreach (TileZone neighbour in current.neighbours_all ?? Array.Empty<TileZone>())
        {
            if (neighbour == null) continue;
            float distance = ZoneDistance(neighbour, target);
            if (distance < bestDistance)
            {
                best = neighbour;
                bestDistance = distance;
            }
        }
        corps.zone_id = best.id;
    }

    public static float ZoneDistance(TileZone a, TileZone b) =>
        a == null || b == null ? float.MaxValue : Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    private static void MoveGeneral(CorpsData corps)
    {
        if (corps.general_id < 0) return;
        Actor general = World.world.units.get(corps.general_id);
        TileZone zone = Zone(corps);
        if (general?.data == null || general.isRekt() || !general.isAlive() || zone?.centerTile == null ||
            general.current_tile == null) return;
        float now = Time.unscaledTime;
        if (GeneralMoves.TryGetValue(general.id, out float next) && now < next) return;
        GeneralMoves[general.id] = now + GeneralMoveInterval;
        if (general.current_tile.zone == zone) return;
        try { general.goTo(zone.centerTile); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][军团] 统领行军失败: {exception.Message}"); }
    }

    #endregion

    #region 名称

    private static readonly string[] Digits = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    private static string Chinese(int number)
    {
        if (number <= 0) return "〇";
        if (number < 10) return Digits[number];
        if (number < 20) return "十" + Digits[number % 10];
        if (number < 100) return Digits[number / 10] + "十" + Digits[number % 10];
        return number.ToString();
    }

    private static string Ordinal(int number)
    {
        int tens = number % 100;
        string suffix = tens is >= 11 and <= 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number + suffix;
    }

    public static string Name(CorpsData corps)
    {
        if (corps.militia)
            return string.Format(LM.Get("corps_name_militia"), Home(corps)?.GetCityName() ?? "");
        string format = LM.Get("corps_name");
        return string.Format(format, format.StartsWith("{0}") ? Ordinal(corps.serial) : Chinese(corps.serial));
    }

    // 兵力简写：中文按"万"，英文按 k
    public static string ShortStrength(float people)
    {
        bool chinese = !LM.Get("corps_name").StartsWith("{0}");
        if (chinese)
            return people >= 10000f ? $"{people / 10000f:0.#}万" : $"{Mathf.RoundToInt(people)}";
        return people >= 1000f ? $"{people / 1000f:0.#}k" : $"{Mathf.RoundToInt(people)}";
    }

    #endregion
}
