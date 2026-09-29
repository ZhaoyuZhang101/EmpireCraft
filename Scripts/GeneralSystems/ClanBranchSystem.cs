using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗族分家。按历史上宗族分支的三种主要途径，每年扫描一次：
//   1. 别子为祖(enfeoff)：非宗子一系的族人受封、做了别国的君主 → 立即以封国为号另立一支
//      ("别子为祖，继别为宗"，如鲁之三桓、汉之诸侯王宗室)；
//   2. 迁徙开基(migration)：一支族人离开祖籍、在远方(他国，或离祖籍很远的城市)定居多年且人丁兴旺
//      → 以所居之城为郡望另立一支(如清河崔氏、博陵崔氏同出一姓而各为一宗)；
//   3. 五服已尽(mourning)：某一房与宗子一系的共同祖先已在五代以上("小宗五世则迁")
//      → 这一房(房祖的全部在世后裔)另立一支，以房祖为号。
// 共同约束：宗子(原版氏族的族长)永远留在原宗族；分出后原宗族须保留足够的在世族人；同一宗族两次
// 分家之间有冷却。分支记下 parent_clan_id，与原宗族同属一个世系——宗室身份、帝系继承这些
// "是不是同一家"的判断都按世系算(见 SpecificClanManager.SameLineage)，分出去的王子仍是宗室。
public static class ClanBranchSystem
{
    public const string ReasonEnfeoff = "enfeoff";
    public const string ReasonMigration = "migration";
    public const string ReasonMourning = "mourning";

    private const int BranchCooldownYears = 15;
    private const int MinRemainingMembers = 5;
    private const int MinRemainingForEnfeoff = 3;
    private const int MinMigrationBranchSize = 5;
    private const int MinMourningBranchSize = 5;
    private const int MigrationYears = 30;
    private const float MigrationDistance = 60f;
    private const int MourningGenerations = 5;

    private static double _lastScan = -1d;

    // 挂在城市更新上(每座城都会调)，这里自己限流成一年一次
    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan < 0d || now < _lastScan)
        {
            _lastScan = now;
            return;
        }
        if (Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (SpecificClan clan in SpecificClanManager._specificClans.ToList())
        {
            try
            {
                ScanClan(clan);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"宗族分家检查失败 ({clan?.name}): {exception}");
            }
        }
    }

    private static void ScanClan(SpecificClan clan)
    {
        if (clan == null) return;
        List<PersonalClanIdentity> living = LivingMembers(clan);
        UpdateSettlements(clan, living);
        if (living.Count == 0) return;
        if (clan.last_branch_timestamp >= 0d && Date.getYearsSince(clan.last_branch_timestamp) < BranchCooldownYears)
            return;
        Actor chief = GetChief(clan, living);
        PersonalClanIdentity chiefIdentity = chief?.GetPersonalIdentity();
        if (chiefIdentity == null) return;

        if (TryEnfeoffBranch(clan, living, chief)) return;
        if (TryMigrationBranch(clan, living, chief)) return;
        TryMourningBranch(clan, living, chiefIdentity);
    }

    #region 别子为祖

    private static bool TryEnfeoffBranch(SpecificClan clan, List<PersonalClanIdentity> living, Actor chief)
    {
        // 宗子本人得是一国之君，"别子"才有意义；宗子自己的国家之外受封为君的族人另立一支
        if (!chief.isKing() || chief.kingdom == null) return false;
        foreach (PersonalClanIdentity identity in living.Where(identity => identity.is_main))
        {
            Actor actor = identity._actor;
            if (actor == null || actor == chief || !actor.isKing() || actor.kingdom == null ||
                actor.kingdom == chief.kingdom || actor.kingdom.isRekt()) continue;
            List<PersonalClanIdentity> members = CollectDescendants(clan, identity, living);
            if (living.Count - members.Count < MinRemainingForEnfeoff) continue;
            string fief = actor.kingdom.GetMainTitle()?.data?.name;
            if (string.IsNullOrWhiteSpace(fief)) fief = actor.kingdom.capital?.GetCityName();
            if (string.IsNullOrWhiteSpace(fief)) fief = actor.kingdom.GetKingdomName();
            SpecificClan branch = Split(clan, identity, identity, members, ReasonEnfeoff,
                string.Format(LM.Get("clan_branch_label_fief"), fief), actor.kingdom.capital);
            if (branch == null) continue;
            Announce(branch, actor.kingdom, string.Format(LM.Get("clan_branch_enfeoff_history"),
                identity.name, fief, clan.GetDisplayName(), branch.GetDisplayName()), actor);
            return true;
        }
        return false;
    }

    #endregion

    #region 迁徙开基

    // 记下族人在每座城最早定居的时间；一座城里一个族人都不剩了就清掉
    private static void UpdateSettlements(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        clan.city_settle_since ??= new Dictionary<long, double>();
        double now = World.world.getCurWorldTime();
        var present = new HashSet<long>(living.Select(identity => identity._actor?.city)
            .Where(city => city != null && !city.isRekt()).Select(city => city.id));
        foreach (long cityId in present)
            if (!clan.city_settle_since.ContainsKey(cityId)) clan.city_settle_since[cityId] = now;
        foreach (long cityId in clan.city_settle_since.Keys.Where(id => !present.Contains(id)).ToList())
            clan.city_settle_since.Remove(cityId);
    }

    private static bool TryMigrationBranch(SpecificClan clan, List<PersonalClanIdentity> living, Actor chief)
    {
        City ancestral = clan.GetAncestralCity();
        if (ancestral == null) return false;
        foreach (IGrouping<City, PersonalClanIdentity> group in living
                     .Where(identity => identity._actor?.city != null && !identity._actor.city.isRekt())
                     .GroupBy(identity => identity._actor.city))
        {
            City city = group.Key;
            if (city == ancestral || city == chief.city) continue;
            bool far = city.kingdom != ancestral.kingdom ||
                       Vector2.Distance(city.city_center, ancestral.city_center) >= MigrationDistance;
            if (!far) continue;
            if (!clan.city_settle_since.TryGetValue(city.id, out double since) ||
                Date.getYearsSince(since) < MigrationYears) continue;
            List<PersonalClanIdentity> settlers = group.Where(identity => identity.is_main).ToList();
            if (settlers.Count < MinMigrationBranchSize) continue;
            List<PersonalClanIdentity> members = WithSpouses(clan, settlers, living);
            if (living.Count - members.Count < MinRemainingMembers) continue;
            // 开基祖：这一支里辈分最高、年纪最大的人
            PersonalClanIdentity founder = settlers.OrderBy(identity => identity.generation)
                .ThenByDescending(identity => identity._actor?.getAge() ?? 0).First();
            SpecificClan branch = Split(clan, founder, founder, members, ReasonMigration,
                string.Format(LM.Get("clan_branch_label_place"), city.GetCityName()), city);
            if (branch == null) continue;
            Announce(branch, city.kingdom, string.Format(LM.Get("clan_branch_migration_history"),
                founder.name, city.GetCityName(), (int)Date.getYearsSince(since), clan.GetDisplayName(),
                branch.GetDisplayName()), founder._actor);
            return true;
        }
        return false;
    }

    #endregion

    #region 五服已尽

    private static bool TryMourningBranch(SpecificClan clan, List<PersonalClanIdentity> living,
        PersonalClanIdentity chief)
    {
        // 宗子一系：宗子和他的直系祖先，记下各自离宗子几代
        var mainLine = new Dictionary<long, int>();
        PersonalClanIdentity cursor = chief;
        for (int depth = 0; cursor != null && depth < 64 && !mainLine.ContainsKey(cursor.id); depth++)
        {
            mainLine[cursor.id] = depth;
            cursor = GetLineParent(cursor);
        }

        // 每个在世族人往上找，碰到宗子一系的第一个人就是共同祖先；共同祖先在五代以上 → 出了五服。
        // 这一房的房祖 = 通往他的路径上、共同祖先之下的那一位。
        var houses = new Dictionary<long, PersonalClanIdentity>();
        foreach (PersonalClanIdentity identity in living.Where(identity => identity.is_main && identity.id != chief.id))
        {
            PersonalClanIdentity previous = null;
            PersonalClanIdentity node = identity;
            int generations = 0;
            while (node != null && generations < 64 && !mainLine.ContainsKey(node.id))
            {
                previous = node;
                node = GetLineParent(node);
                generations++;
            }
            if (node == null || previous == null || generations < MourningGenerations) continue;
            houses[previous.id] = previous;
        }

        foreach (PersonalClanIdentity houseFounder in houses.Values.OrderBy(identity => identity.generation))
        {
            List<PersonalClanIdentity> members = CollectDescendants(clan, houseFounder, living);
            if (members.Count(identity => identity.is_main) < MinMourningBranchSize) continue;
            if (members.Any(identity => identity.id == chief.id)) continue;
            if (living.Count - members.Count < MinRemainingMembers) continue;
            // 房祖可能早已过世：分支的创始人记房祖，原版氏族交给这一房辈分最高的在世者
            PersonalClanIdentity eldest = members.Where(identity => identity.is_main)
                .OrderBy(identity => identity.generation)
                .ThenByDescending(identity => identity._actor?.getAge() ?? 0).First();
            City seat = members.Select(identity => identity._actor?.city)
                .Where(city => city != null && !city.isRekt())
                .GroupBy(city => city).OrderByDescending(group => group.Count()).FirstOrDefault()?.Key;
            SpecificClan branch = Split(clan, houseFounder, eldest, members, ReasonMourning,
                string.Format(LM.Get("clan_branch_label_house"), houseFounder.name), seat);
            if (branch == null) continue;
            Announce(branch, eldest._actor?.kingdom, string.Format(LM.Get("clan_branch_mourning_history"),
                houseFounder.name, clan.GetDisplayName(), branch.GetDisplayName()), eldest._actor);
            return true;
        }
        return false;
    }

    // 顺着宗族的血脉往上走：优先留在本宗族里、作为主方(is_main)的那一位父母
    private static PersonalClanIdentity GetLineParent(PersonalClanIdentity identity)
    {
        PersonalClanIdentity father = SpecificClanManager.getPerson(identity.father);
        PersonalClanIdentity mother = SpecificClanManager.getPerson(identity.mother);
        bool InLine(PersonalClanIdentity parent) => parent != null && parent.is_main &&
                                                    parent.specific_clan_id == identity.specific_clan_id;
        if (InLine(father)) return father;
        if (InLine(mother)) return mother;
        return null;
    }

    #endregion

    #region 分家本身

    // founder 记为分支创始人(可以是已故的房祖)；vanillaFounder 必须在世，用来建原版氏族
    private static SpecificClan Split(SpecificClan parent, PersonalClanIdentity founder,
        PersonalClanIdentity vanillaFounder, List<PersonalClanIdentity> members, string reason, string label,
        City seat)
    {
        Actor founderActor = vanillaFounder?._actor;
        if (founderActor == null || founderActor.isRekt() || members.Count == 0) return null;

        // 土地按各城迁出人数的比例分过去，先算好(移人之前)
        var landRatios = new Dictionary<City, float>();
        foreach (IGrouping<City, PersonalClanIdentity> group in members
                     .Where(identity => identity._actor?.city != null && !identity._actor.city.isRekt())
                     .GroupBy(identity => identity._actor.city))
        {
            int total = group.Key.units?.Count(unit => unit != null && !unit.isRekt() &&
                                                       unit.GetSpecificClan() == parent) ?? 0;
            if (total > 0) landRatios[group.Key] = Mathf.Clamp01((float)group.Count() / total);
        }

        Clan vanillaClan;
        try
        {
            vanillaClan = World.world.clans.newClan(founderActor, true);
        }
        catch
        {
            return null;
        }
        if (vanillaClan == null) return null;

        var branch = new SpecificClan
        {
            _cache = new Dictionary<long, PersonalClanIdentity>(),
            id = IdGenerator.NextId(),
            name = parent.name,
            established_timestamp = World.world.getCurWorldTime(),
            clan_sex_priority = parent.clan_sex_priority,
            color = ColorSelector.NextColor(),
            parent_clan_id = parent.id,
            branch_label = label,
            branch_reason = reason,
            founder = founder.id
        };
        branch.RecordAncestralCity(seat ?? founderActor.city);
        vanillaClan.SetSpecificClan(branch);
        SpecificClanManager.AddClan(branch);

        foreach (PersonalClanIdentity identity in members)
        {
            if (!parent.TakePerson(identity.id)) continue;
            identity.specific_clan_id = branch.id;
            branch.Upsert(identity);
            Actor actor = identity._actor;
            if (actor != null && !actor.isRekt() && actor.clan != vanillaClan) actor.setClan(vanillaClan);
        }
        // 已故的房祖不搬家(留在原宗族的谱上)，但分支要能从他查起
        parent.last_branch_timestamp = World.world.getCurWorldTime();
        branch.last_branch_timestamp = World.world.getCurWorldTime();

        foreach (KeyValuePair<City, float> pair in landRatios)
            LandEconomySystem.TransferClanLand(pair.Key, parent, branch, pair.Value);
        return branch;
    }

    private static void Announce(SpecificClan branch, Kingdom kingdom, string content, Actor actor)
    {
        actor?.RecordPersonalHistory(content, "clan_branch");
        TranslateHelper.LogEventMessage(content, kingdom);
    }

    #endregion

    #region 工具

    private static List<PersonalClanIdentity> LivingMembers(SpecificClan clan) =>
        clan.SnapshotPeople().Where(identity => identity != null && identity.is_alive &&
                                                identity._actor != null && !identity._actor.isRekt()).ToList();

    // 宗子 = 原版氏族族长；取本宗族在世成员所在的原版氏族
    private static Actor GetChief(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        Clan vanilla = living.Select(identity => identity._actor?.clan)
            .FirstOrDefault(candidate => candidate != null && candidate.GetSpecificClan() == clan);
        Actor chief = vanilla?.getChief();
        return chief != null && !chief.isRekt() && chief.GetSpecificClan() == clan ? chief : null;
    }

    // root 及其在本宗族里的全部在世后裔(沿 is_main 血脉往下)，再带上他们嫁/入进来的配偶
    private static List<PersonalClanIdentity> CollectDescendants(SpecificClan clan, PersonalClanIdentity root,
        List<PersonalClanIdentity> living)
    {
        var livingIds = new HashSet<long>(living.Select(identity => identity.id));
        var result = new List<PersonalClanIdentity>();
        var visited = new HashSet<long>();
        var queue = new Queue<PersonalClanIdentity>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            PersonalClanIdentity node = queue.Dequeue();
            if (node == null || !visited.Add(node.id)) continue;
            if (node.specific_clan_id != clan.id) continue;
            if (livingIds.Contains(node.id)) result.Add(node);
            if (!node.is_main) continue;
            foreach (long childId in node.children)
                queue.Enqueue(SpecificClanManager.getPerson(childId));
        }
        return WithSpouses(clan, result, living);
    }

    private static List<PersonalClanIdentity> WithSpouses(SpecificClan clan, List<PersonalClanIdentity> members,
        List<PersonalClanIdentity> living)
    {
        var ids = new HashSet<long>(members.Select(identity => identity.id));
        var result = new List<PersonalClanIdentity>(members);
        foreach (PersonalClanIdentity identity in living)
        {
            if (identity.is_main || ids.Contains(identity.id) || identity.specific_clan_id != clan.id) continue;
            bool spouse = ids.Contains(identity.lover.identity) ||
                          members.Any(member => member.concubines.Any(concubine => concubine.identity == identity.id));
            if (!spouse) continue;
            ids.Add(identity.id);
            result.Add(identity);
        }
        return result;
    }

    #endregion
}
