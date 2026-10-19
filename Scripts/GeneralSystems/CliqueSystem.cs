using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朋党(用户 2026-10-16 选"朋党取代固定派系"："派系……要符合历史命名习惯，但是整个系统和现代政党类似，而不是恒久不定的")：
// 开放党禁以前、走朝堂博弈(没有议会)的华夏帝国，朝中派系不再是制度模板里固定的几派，而像政党一样兴衰——
//   · 起点：第一次结算时，模板派系(文官派、武官派、天子派、诸侯派……)按派系类型定下理念，改作朋党(名字照旧)；
//   · 倡立：本朝官员里信某种理念(见 PartySystem 的古代理念)的人占到一成五(至少三人)、还没有代表它的朋党，
//     由其中名望最高的人倡立，带走同道至多六人，占比先有一成；每年至多一个，朝中至多六个朋党；
//   · 名字按史书习惯：头一个用这一家的称号——儒家清流、法家变法派、黄老派、墨者、农家、兵家将门、纵横之士、阴阳方士、
//     泛灵论巫祝、皇权帝党、门阀世族、外戚后党、宦官阉党；同一家再有就以领袖之姓称"某党"，姓也重了以籍贯首字称"某党"；
//   · 归属：官员每年按自己的理念转投那一家的朋党(党魁不走，每年至多四人、各三成五机会)；
//     天子派、诸侯派不靠信什么，代表的是身份(用户 2026-10-17)：宗室、寒门阁臣归天子派，贵族、世家归诸侯派(见 IdentityStance)；
//   · 合并：势弱(中央占比不到八)、立党满三年的朋党并入同理念(必定)或理念相近的大党(三成)；
//   · 分裂：中央占比四成以上、六人以上的大党里，籍贯与党魁不同的一乡三人以上，每年一成机会别立"某党"；
//   · 消亡：党员走光就散；朝中至少留两个朋党；
//   · 后党：外戚首领(见 ConsortKinSystem)得势(外戚干政、或他就是权臣)时倡立后党，外戚宗族的官员都归入；
//   · 阉党：庸弱、昏聩、荒淫之君在位，每年两成机会京城有得宠的宦官倡立阉党(宦官带"宦官"特质，见 IsEunuch)；
//     明君亲政，每年三成机会诛宦官、散阉党；
//   · 开放党禁以后，朋党照常改组为近代政党(见 PartySystem.Reorganize)。
// 阶级派系是骨架(用户 2026-10-17："无论如何这些阶级党派都是永远存在并且他们才是真正决定帝国兴衰的")：
//   天子派(皇权)、宗室派(宗亲)、诸侯派(门阀)、后党(外戚)永远都在——不合并、不分裂、不消散，没人时只是势力微弱、蛰伏；
//   宦官天然是天子派(玩家建议、用户 2026-10-17 选"阉党保留为天子派里的一支")：平时宦官归天子派；
//   昏君在位、宦官得宠时从天子派里分出阉党(带走天子派的宦官和三分之一的势力)，专权弄政；
//   明君诛宦官后阉党余众并回天子派、阉党蛰伏，日后昏君在位又会分出来。上面的倡立、合并、分裂、消亡只对学说朋党，
//   学说朋党至多四个，是附着在阶级派系之上、来来去去的思潮。
public static class CliqueSystem
{
    public const string EunuchTrait = "eunuch";
    private const float FoundShare = 0.15f, SwitchChance = 0.35f, MergeRatio = 8f, MergeChance = 0.3f, MergeDistance = 70f,
        SplitRatio = 40f, SplitChance = 0.1f, EunuchRiseChance = 0.2f, EunuchPurgeChance = 0.3f;
    private const int FoundMinMembers = 3, MinOfficials = 4, MinCliques = 2, FoundRecruits = 6,
        MaxSwitchesPerYear = 4, MinYearsBeforeMerge = 3, FoundRatio = 10, SplitMinGroup = 3, SplitMinMembers = 6,
        EunuchFoundRatio = 8;

    public static bool IsEunuch(Actor actor) => actor?.data != null && !actor.isRekt() && actor.hasTrait(EunuchTrait);

    public static bool Applies(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        Regime regime = core?.GetRegime();
        if (regime == null || empire.isRekt() || empire.IsArchived() || !CourtSystem.Applies(empire) ||
            PartySystem.IsActive(empire) || RepublicSystem.IsRepublic(empire) || RegimeManager.IsPrimitive(regime.type) ||
            regime.type == RegimeType.Modern) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(InstitutionSystem.GetPrimaryCulture(empire)) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }

    // 身份立场：寒门阁臣、宦官 → 天子派(皇权；阉党分出时宦官归阉党)；宗室 → 宗室派；
    // 贵族(受封诸侯、世袭王族)、世家 → 诸侯派(门阀)；后党(外戚宗族)另有专门的归入规则(见 Consorts)。其余官员按自己信的学说归属
    public static PartyIdeology? IdentityStance(Empire empire, Actor actor)
    {
        if (empire == null || actor?.data == null || actor.isRekt()) return null;
        if (IsEunuch(actor)) return EunuchFactionRisen(empire) ? PartyIdeology.Eunuchs : PartyIdeology.ImperialPower;
        if (empire.EmpireSpecificClan != null && SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan))
            return PartyIdeology.RoyalKin;
        if (actor.isKing() || GentrySystem.IsGentry(actor) || EmpireCraft.Scripts.AI.ActorAI.EmpireCaftActorJudgeClass.IsNoble(actor))
            return PartyIdeology.GreatClans;
        if (empire.GetCabinetMembers().Contains(actor)) return PartyIdeology.ImperialPower;
        return null;
    }

    // 派系里按什么归属：身份立场优先，否则按信的学说
    private static PartyIdeology Stance(Empire empire, Actor actor) =>
        IdentityStance(empire, actor) ?? IdeologyPopulationSystem.Get(actor);

    // 派系类型 → 起点理念
    private static PartyIdeology? TypeIdeology(FactionType type) => type switch
    {
        FactionType.绥靖 => PartyIdeology.Confucianism,
        FactionType.攘夷 or FactionType.僭主 or FactionType.同化 => PartyIdeology.Strategists,
        FactionType.尊王 => PartyIdeology.ImperialPower,
        FactionType.诸侯 or FactionType.自治 or FactionType.血脉 => PartyIdeology.GreatClans,
        FactionType.中央 => PartyIdeology.Legalism,
        FactionType.神权 => PartyIdeology.YinYang,
        FactionType.融入 => PartyIdeology.Diplomatists,
        FactionType.原始 => PartyIdeology.Animism,
        FactionType.外戚 => PartyIdeology.ConsortKin,
        FactionType.宦官 => PartyIdeology.Eunuchs,
        _ => null
    };

    private const int MaxSchoolCliques = 4;
    private static readonly PartyIdeology[] ClassStances =
        { PartyIdeology.ImperialPower, PartyIdeology.RoyalKin, PartyIdeology.GreatClans, PartyIdeology.ConsortKin, PartyIdeology.Eunuchs };

    private static List<FixedFaction> Cliques(Regime regime) =>
        regime.GetPlayerFactions().Where(faction => faction != null && faction.IsClique && !faction.Ban).ToList();

    public static bool IsClassClique(FixedFaction faction) => faction != null && IdeologyFamilies.IsIdentityStance(faction.Ideology);

    // 学说朋党(会倡立、合并、分裂、消亡的那些)
    private static List<FixedFaction> Schools(Regime regime) => Cliques(regime).Where(clique => !IsClassClique(clique)).ToList();

    private static FixedFaction ClassClique(Regime regime, PartyIdeology stance) =>
        Cliques(regime).FirstOrDefault(clique => clique.Ideology == stance);

    // 阉党已从天子派里分出(有人领头)
    private static bool EunuchFactionRisen(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        return regime != null && ClassClique(regime, PartyIdeology.Eunuchs)?.GetLeader() != null;
    }

    private static float Ratio(Empire empire, FixedFaction faction) => empire.CoreKingdom.GetFactionRatioValue(faction);

    private static bool Settled(FixedFaction faction) =>
        faction.CliqueFoundedAt < 0d || Date.getYearsSince(faction.CliqueFoundedAt) >= MinYearsBeforeMerge;

    // 每年(帝国例行检查，见 EmpireCraftEmpireBehCheckMandate)
    public static void Yearly(Empire empire)
    {
        if (!Applies(empire) || World.world == null) return;
        Regime regime = empire.CoreKingdom.GetRegime();
        try
        {
            RenameSchoolLabels(regime);
            Convert(empire, regime);
            EnsureIssues(regime);
            List<Actor> officials = Officials(empire);
            EnsureClassCliques(empire, regime, officials);
            Eunuchs(empire, regime);
            Consorts(empire, regime, officials);
            TryMerge(empire, regime);
            TryDissolve(empire, regime);
            TryFound(empire, regime, officials);
            Drift(empire, regime, officials);
            AssignPatrons(empire, regime);
            MandateCourt(empire, regime, officials);
            DrainDormant(empire, regime);
            TrySplit(empire, regime);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][朋党] {empire.GetEmpireFullName()} 结算失败: {exception}");
        }
    }

    // 本朝官员(在任、成年，皇帝除外)
    private static List<Actor> Officials(Empire empire) =>
        empire.getUnits().Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                         actor.IsOnOffice() && actor.id != empire.Emperor?.id).ToList();

    #region 起点

    // 模板派系改作朋党(换政体后模板派系重建，也再改一次)
    private static void Convert(Empire empire, Regime regime)
    {
        foreach (FixedFaction faction in regime.GetPlayerFactions())
        {
            if (faction != null && faction.IsClique) EnsureCatalog(faction);
            if (faction == null || faction.IsClique || faction.IsParty || faction.only_king) continue;
            PartyIdeology ideology = TypeIdeology(faction.Type) ?? PartySystem.LeaningOf(faction);
            faction.IsClique = true;
            faction.Ideology = ideology;
            faction.CliqueFoundedAt = -1d;
            faction.EmpireId = empire.getID();
        }
    }

    // 四个阶级派系缺哪个补哪个：有这身份的官员就由名望最高的人领头，没有就先蛰伏(势力为零)
    private static void EnsureClassCliques(Empire empire, Regime regime, List<Actor> officials)
    {
        foreach (PartyIdeology stance in ClassStances)
        {
            if (ClassClique(regime, stance) != null) continue;
            Actor leader = stance is PartyIdeology.ConsortKin or PartyIdeology.Eunuchs ? null
                : officials.Where(actor => IdentityStance(empire, actor) == stance).OrderByDescending(actor => actor.renown).FirstOrDefault();
            Create(empire, regime, stance, leader, 0);
        }
    }

    // 以前新建的朋党诉求清单是空的(一条本类型的诉求都没有)：补上本类型的诉求；手动删过的不再补回
    private static void EnsureCatalog(FixedFaction faction)
    {
        if (faction.CliqueFoundedAt < 0d || !FactionManager.FactionConfig.TryGetValue(faction.Type, out List<TemporaryFactionType> catalog))
            return;
        faction.TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
        if (catalog.Any(claim => faction.TemporaryFactionTypesRecord.Contains(claim))) return;
        List<TemporaryFactionType> missing = catalog.ToList();
        if (missing.Count == 0) return;
        faction.TemporaryFactionTypesRecord.AddRange(missing);
        faction.TemporaryFactions = faction.ConvertToObjectFromFactionType();
        foreach (TemporaryFaction claim in faction.TemporaryFactions) claim?.Init(faction);
        faction.FixMissedTemporaryFactions();
    }

    #endregion

    #region 倡立

    private static void TryFound(Empire empire, Regime regime, List<Actor> officials)
    {
        List<FixedFaction> cliques = Cliques(regime);
        bool contention = HundredSchools(empire);
        if (Schools(regime).Count >= MaxSchoolCliques + (contention ? 1 : 0) || officials.Count < MinOfficials) return;
        var represented = new HashSet<PartyIdeology>(cliques.Select(clique => clique.Ideology));
        var leaders = new HashSet<long>(cliques.Select(clique => clique.Leader));
        foreach (IGrouping<PartyIdeology, Actor> group in officials
                     .GroupBy(actor => Stance(empire, actor))
                     .Where(group => IdeologyFamilies.IsAncient(group.Key) && !IdeologyFamilies.IsIdentityStance(group.Key) &&
                                     !represented.Contains(group.Key))
                     .OrderByDescending(group => group.Count()))
        {
            if (group.Count() < Mathf.Max(FoundMinMembers, Mathf.CeilToInt(officials.Count * (contention ? ContentionFoundShare : FoundShare)))) break;
            Actor founder = group.Where(actor => !leaders.Contains(actor.id)).OrderByDescending(actor => actor.renown).FirstOrDefault();
            if (founder == null) continue;
            FixedFaction clique = Create(empire, regime, group.Key, founder, FoundRatio);
            foreach (Actor member in group.Where(actor => actor != founder && !leaders.Contains(actor.id))
                         .OrderByDescending(actor => actor.renown).Take(FoundRecruits).ToList())
                member.SetFaction(clique);
            Record(empire, string.Format(LM.Get("clique_founded_history"), founder.getName(), clique.Name,
                PartySystem.GetIdeologyName(group.Key)), founder);
            return;
        }
    }

    private static FixedFaction Create(Empire empire, Regime regime, PartyIdeology ideology, Actor leader, int ratio,
        string name = null)
    {
        var clique = new FixedFaction
        {
            _id = Guid.NewGuid().ToString(),
            EmpireId = empire.getID(),
            Members = new List<long>(),
            TemporaryFactions = new List<TemporaryFaction>(),
            // 诉求清单照派系类型(后党有太后临朝、阉党有宦官掌兵……)
            TemporaryFactionTypesRecord = FactionManager.FactionConfig.TryGetValue(PartySystem.FactionTypeOf(ideology), out var catalog)
                ? new List<TemporaryFactionType>(catalog) : new List<TemporaryFactionType>(),
            ClaimCatalogVersion = FixedFaction.CurrentClaimCatalogVersion,
            Type = PartySystem.FactionTypeOf(ideology),
            IsClique = true,
            Ideology = ideology,
            CliqueFoundedAt = World.world.getCurWorldTime()
        };
        clique.Name = name ?? NameFor(regime, ideology, leader);
        FactionClassSystem.EnsureProfile(clique);
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
            clique.ClassAffinities[socialClass] = PartySystem.GetAffinity(ideology, socialClass);
        clique.FixMissedTemporaryFactions();
        regime.GetPlayerFactions().Add(clique);
        empire.CoreKingdom.ReconcileFactionRatios(regime.GetPlayerFactions());
        if (leader != null)
        {
            leader.SetFaction(clique);
            clique.SetLeader(leader);
        }
        if (ratio > 0) empire.CoreKingdom.TryIncreaseFactionRatio(clique, ratio);
        return clique;
    }

    // 旧称号用诸子百家的名字，玩家看不出是干什么的(用户 2026-10-17："不要用诸子百家的名字只按功能来就行，
    // 至少玩家一看就知道干嘛的")：旧存档里还叫这些名字的朋党改成按功能的新称号
    private static readonly Dictionary<string, string> OldSchoolLabels = new()
    {
        ["农家"] = "Agrarianism", ["農家"] = "Agrarianism", ["Tillers"] = "Agrarianism",
        ["黄老派"] = "HuangLao", ["黃老派"] = "HuangLao", ["Huang-Lao Circle"] = "HuangLao",
        ["墨者"] = "Mohism", ["Mohists"] = "Mohism",
        ["纵横之士"] = "Diplomatists", ["縱橫之士"] = "Diplomatists", ["Persuaders"] = "Diplomatists",
    };

    private static void RenameSchoolLabels(Regime regime)
    {
        foreach (FixedFaction faction in regime.GetPlayerFactions())
            if (faction?.Name != null && OldSchoolLabels.TryGetValue(faction.Name, out string key))
                faction.Name = LM.Get("clique_label_" + key);
    }

    // 史书习惯的名字：这一家的称号；重了称领袖之姓"某党"；再重了称籍贯首字"某党"
    private static string NameFor(Regime regime, PartyIdeology ideology, Actor leader)
    {
        var used = new HashSet<string>(regime.GetPlayerFactions().Where(faction => faction != null).Select(faction => faction.Name));
        string label = LM.Get("clique_label_" + ideology);
        if (!used.Contains(label)) return label;
        string surname = SurnameInheritanceSystem.FamilyNameOf(leader);
        if (!string.IsNullOrWhiteSpace(surname))
        {
            string bySurname = string.Format(LM.Get("clique_name_party"), surname);
            if (!used.Contains(bySurname)) return bySurname;
        }
        string home = Hometown(leader);
        if (!string.IsNullOrEmpty(home))
        {
            string byHome = string.Format(LM.Get("clique_name_party"), home);
            if (!used.Contains(byHome)) return byHome;
        }
        for (int i = 2; i < 100; i++)
        {
            string numbered = string.Format(LM.Get("party_name_duplicate"), label, i);
            if (!used.Contains(numbered)) return numbered;
        }
        return label;
    }

    // 籍贯首字(族谱记的出生地，没有就看所在的城)
    private static string Hometown(Actor actor)
    {
        string place = actor?.GetPersonalIdentity()?.birthplace;
        if (string.IsNullOrWhiteSpace(place)) place = actor?.city?.GetCityName();
        return string.IsNullOrWhiteSpace(place) ? "" : place.Substring(0, 1);
    }

    #endregion

    #region 归属、合并、分裂、消亡

    private static void Drift(Empire empire, Regime regime, List<Actor> officials)
    {
        List<FixedFaction> cliques = Cliques(regime);
        if (cliques.Count < 2) return;
        var byIdeology = cliques.GroupBy(clique => clique.Ideology)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(clique => clique.Count).First());
        var leaders = new HashSet<long>(cliques.Select(clique => clique.Leader));
        int switches = 0;
        foreach (Actor actor in officials.OrderBy(_ => Random.value))
        {
            if (switches >= MaxSwitchesPerYear) break;
            if (leaders.Contains(actor.id) || !byIdeology.TryGetValue(Stance(empire, actor), out FixedFaction home)) continue;
            FixedFaction current = cliques.FirstOrDefault(clique => clique.Members.Contains(actor.id));
            if (current == home || current != null && current.Ideology == home.Ideology || Random.value > SwitchChance) continue;
            actor.SetFaction(home);
            switches++;
        }
    }

    private static void TryMerge(Empire empire, Regime regime)
    {
        List<FixedFaction> cliques = Schools(regime);
        if (cliques.Count < MinCliques) return;
        foreach (FixedFaction small in cliques.OrderBy(clique => Ratio(empire, clique)).ThenBy(clique => clique.Count))
        {
            if (Ratio(empire, small) >= MergeRatio || !Settled(small)) continue;
            FixedFaction target = cliques.Where(other => other != small && Ratio(empire, other) > Ratio(empire, small))
                .Where(other => other.Ideology == small.Ideology ||
                                PartySystem.IdeologyDistance(other.Ideology, small.Ideology) <= MergeDistance)
                .OrderBy(other => PartySystem.IdeologyDistance(other.Ideology, small.Ideology))
                .ThenByDescending(other => Ratio(empire, other)).FirstOrDefault();
            if (target == null || target.Ideology != small.Ideology && Random.value >= MergeChance) continue;
            string name = small.Name;
            Absorb(empire, regime, target, small);
            Record(empire, string.Format(LM.Get("clique_merged_history"), name, target.Name), target.GetLeader());
            return;
        }
    }

    private static void Absorb(Empire empire, Regime regime, FixedFaction target, FixedFaction absorbed)
    {
        foreach (Actor member in absorbed.AllMembers.ToList()) member.SetFaction(target);
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        if (ratios != null && ratios.TryGetValue(absorbed, out int ratio))
            ratios[target] = (ratios.TryGetValue(target, out int current) ? current : 0) + ratio;
        Remove(empire, regime, absorbed);
    }

    private static void Remove(Empire empire, Regime regime, FixedFaction clique)
    {
        foreach (long id in clique.Members.ToList()) World.world.units.get(id)?.RemoveFaction();
        clique.Members.Clear();
        regime.GetPlayerFactions().Remove(clique);
        empire.CoreKingdom.ReconcileFactionRatios(regime.GetPlayerFactions());
        empire.CoreKingdom.ClampFactionRatio();
    }

    private static void TryDissolve(Empire empire, Regime regime)
    {
        List<FixedFaction> cliques = Schools(regime);
        FixedFaction empty = cliques.FirstOrDefault(clique => clique.CliqueFoundedAt >= 0d && clique.AllMembers.Count == 0);
        if (empty == null) return;
        string name = empty.Name;
        Remove(empire, regime, empty);
        Record(empire, string.Format(LM.Get("clique_dissolved_history"), name), null);
    }

    private static void TrySplit(Empire empire, Regime regime)
    {
        List<FixedFaction> cliques = Schools(regime);
        if (cliques.Count >= MaxSchoolCliques || Random.value > SplitChance) return;
        FixedFaction big = cliques.Where(clique => Ratio(empire, clique) >= SplitRatio && clique.Count >= SplitMinMembers)
            .OrderByDescending(clique => Ratio(empire, clique)).FirstOrDefault();
        Actor boss = big?.GetLeader();
        if (big == null) return;
        string bossHome = Hometown(boss);
        IGrouping<string, Actor> faction = big.AllMembers.Where(actor => actor != boss && !string.IsNullOrEmpty(Hometown(actor)))
            .GroupBy(Hometown).Where(group => group.Key != bossHome && group.Count() >= SplitMinGroup)
            .OrderByDescending(group => group.Count()).FirstOrDefault();
        if (faction == null) return;
        Actor rebel = faction.OrderByDescending(actor => actor.renown).First();
        string name = string.Format(LM.Get("clique_name_party"), faction.Key);
        if (regime.GetPlayerFactions().Any(other => other?.Name == name)) name = null;
        int share = Mathf.Max(3, Mathf.RoundToInt(Ratio(empire, big) * faction.Count() / (float)Mathf.Max(1, big.Count) * 0.8f));
        FixedFaction split = Create(empire, regime, big.Ideology, rebel, 0, name);
        foreach (Actor member in faction.Where(actor => actor != rebel).ToList()) member.SetFaction(split);
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        if (ratios != null && ratios.TryGetValue(big, out int bigRatio))
        {
            int moved = Mathf.Min(share, bigRatio);
            ratios[big] = bigRatio - moved;
            ratios[split] = (ratios.TryGetValue(split, out int current) ? current : 0) + moved;
        }
        Record(empire, string.Format(LM.Get("clique_split_history"), rebel.getName(), faction.Count(), big.Name, split.Name), rebel);
    }

    #endregion

    #region 天命与朝堂

    // 天命与朝堂(用户 2026-10-17 选"天命与朝堂挂钩")：
    //   · 官学 = 本文化定于一尊的正统(见 AncientSchoolsSystem)；受天命的治世里学说朋党可请"独尊官学"改尊本家
    //     (TempFac_独尊官学)；有正统时每年至多三名官员各两成机会改宗；
    //   · 天命动摇(动荡)：方士得势，方士一派每年势力 +2；
    //   · 失天命、天下无主(乱世)：正统逐年废弛(见 AncientSchoolsSystem)；乱世或百家争鸣时学说朋党倡立的门槛从一成五降到一成，名额多一个。
    private const float ContentionFoundShare = 0.1f, OfficialConvertChance = 0.2f;
    private const int OfficialConvertsPerYear = 3, DiviningRise = 2;

    public static PartyIdeology? OfficialSchool(Empire empire) =>
        empire?.data == null ? null : AncientSchoolsSystem.Orthodox(InstitutionSystem.GetPrimaryCulture(empire));

    // 百家争鸣：本朝所在的天下无主，或本文化正值百家争鸣
    private static bool HundredSchools(Empire empire) => TianxiaSystem.PhaseOf(empire.CoreKingdom) == TianxiaSystem.Chaos ||
                                                         AncientSchoolsSystem.Contending(InstitutionSystem.GetPrimaryCulture(empire));

    private static void MandateCourt(Empire empire, Regime regime, List<Actor> officials)
    {
        int phase = TianxiaSystem.PhaseOf(empire.CoreKingdom);
        PartyIdeology? school = OfficialSchool(empire);
        if (school != null)
        {
            int converts = 0;
            foreach (Actor actor in officials.OrderBy(_ => Random.value))
            {
                if (converts >= OfficialConvertsPerYear) break;
                PartyIdeology own = IdeologyPopulationSystem.Get(actor);
                if (own == school.Value || IdeologyFamilies.IsIdentityStance(own) || IdentityStance(empire, actor) != null ||
                    Random.value >= OfficialConvertChance) continue;
                IdeologyPopulationSystem.Set(actor, school.Value);
                converts++;
            }
        }
        if (phase == TianxiaSystem.Unrest)
        {
            FixedFaction diviners = Schools(regime).FirstOrDefault(clique => clique.Ideology == PartyIdeology.YinYang);
            if (diviners != null) empire.CoreKingdom.TryIncreaseFactionRatio(diviners, DiviningRise);
        }
    }

    #endregion

    #region 议题

    // 朋党各自能上奏的朝堂议题(派系类型自带的之外；各议题的效果写在 Claims/TempFac_*)
    private static readonly Dictionary<PartyIdeology, TemporaryFactionType[]> CliqueIssues = new()
    {
        [PartyIdeology.Confucianism] = new[] { TemporaryFactionType.诛宦官, TemporaryFactionType.大赦天下, TemporaryFactionType.兴修水利,
            TemporaryFactionType.重农抑商, TemporaryFactionType.裁省用度, TemporaryFactionType.独尊官学 },
        [PartyIdeology.Legalism] = new[] { TemporaryFactionType.盐铁专营, TemporaryFactionType.重定律令, TemporaryFactionType.屯田实边,
            TemporaryFactionType.抑豪强, TemporaryFactionType.独尊官学 },
        [PartyIdeology.HuangLao] = new[] { TemporaryFactionType.大赦天下, TemporaryFactionType.裁省用度, TemporaryFactionType.独尊官学 },
        [PartyIdeology.Mohism] = new[] { TemporaryFactionType.裁省用度, TemporaryFactionType.兴修水利, TemporaryFactionType.独尊官学 },
        [PartyIdeology.Agrarianism] = new[] { TemporaryFactionType.兴修水利, TemporaryFactionType.重农抑商, TemporaryFactionType.独尊官学 },
        [PartyIdeology.Strategists] = new[] { TemporaryFactionType.屯田实边, TemporaryFactionType.独尊官学 },
        [PartyIdeology.Diplomatists] = new[] { TemporaryFactionType.独尊官学 },
        [PartyIdeology.YinYang] = new[] { TemporaryFactionType.封禅, TemporaryFactionType.独尊官学 },
        [PartyIdeology.Animism] = new[] { TemporaryFactionType.封禅 },
        [PartyIdeology.ImperialPower] = new[] { TemporaryFactionType.封禅, TemporaryFactionType.盐铁专营, TemporaryFactionType.重定律令,
            TemporaryFactionType.修氏族志, TemporaryFactionType.抑豪强 },
        [PartyIdeology.RoyalKin] = new[] { TemporaryFactionType.供养宗室, TemporaryFactionType.分封, TemporaryFactionType.封禅 },
        [PartyIdeology.GreatClans] = new[] { TemporaryFactionType.诛宦官 },
        [PartyIdeology.ConsortKin] = new[] { TemporaryFactionType.诛宦官, TemporaryFactionType.大赦天下 },
        [PartyIdeology.Eunuchs] = new[] { TemporaryFactionType.党锢 }
    };

    private static void EnsureIssues(Regime regime)
    {
        foreach (FixedFaction clique in Cliques(regime))
        {
            if (!CliqueIssues.TryGetValue(clique.Ideology, out TemporaryFactionType[] issues)) continue;
            clique.TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
            List<TemporaryFactionType> missing = issues.Where(issue => !clique.TemporaryFactionTypesRecord.Contains(issue)).ToList();
            if (missing.Count == 0) continue;
            clique.TemporaryFactionTypesRecord.AddRange(missing);
            clique.TemporaryFactions = clique.ConvertToObjectFromFactionType();
            foreach (TemporaryFaction claim in clique.TemporaryFactions) claim?.Init(clique);
            clique.FixMissedTemporaryFactions();
        }
    }

    // 诛宦官：阉党有人领头时才能诛
    public static bool CanPurgeEunuchs(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        return regime != null && empire.Emperor?.data != null && ClassClique(regime, PartyIdeology.Eunuchs)?.GetLeader() != null;
    }

    public static string PurgeEunuchs(Empire empire)
    {
        if (!CanPurgeEunuchs(empire)) return null;
        Regime regime = empire.CoreKingdom.GetRegime();
        FixedFaction party = ClassClique(regime, PartyIdeology.Eunuchs);
        string name = party.Name;
        Purge(empire, regime, party, empire.Emperor);
        return name;
    }

    // 党锢：禁锢朝中势力最大、又不投靠上奏一派的学说朋党(至少占一成)
    public static FixedFaction ProscriptionTarget(Empire empire, string proposerId)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null) return null;
        FixedFaction proposer = CourtSystem.FindFaction(empire, proposerId);
        return Schools(regime).Where(school => school != proposer && Ratio(empire, school) >= 10f && Patron(empire, school) != proposer)
            .OrderByDescending(school => Ratio(empire, school)).FirstOrDefault();
    }

    public static string Proscribe(Empire empire, string proposerId)
    {
        FixedFaction target = ProscriptionTarget(empire, proposerId);
        if (target == null) return null;
        Regime regime = empire.CoreKingdom.GetRegime();
        FixedFaction proposer = CourtSystem.FindFaction(empire, proposerId);
        string name = target.Name;
        foreach (Actor member in target.AllMembers.ToList()) member.RemoveFaction();
        target.Members.Clear();
        target.Leader = -1L;
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        if (ratios != null && ratios.TryGetValue(target, out int power) && power > 0)
        {
            int moved = power * 2 / 3;
            ratios[target] = power - moved;
            if (proposer != null) ratios[proposer] = (ratios.TryGetValue(proposer, out int current) ? current : 0) + moved;
        }
        empire.AddMandate(-3);
        Record(empire, string.Format(LM.Get("clique_proscribed_history"), proposer?.Name ?? "", name), proposer?.GetLeader());
        return name;
    }

    #endregion

    #region 权与影响

    // 权与影响(用户 2026-10-17："他们既然投靠了别人那么在这个国家主导的就是那个阶级派系而不是下面的学说派系，
    // 因为他们没有权但是又影响力""这意味着他们的百分比是并入上面的阶级派系的")：
    //   · 权只归阶级派系：自己的占比 + 投靠它的学说朋党的占比；主导派系在阶级派系里按权比(见 Regime.GetDominateFaction)；
    //   · 学说朋党只有影响(自己的占比)：廷议照样按它表态、算分量，但不争主导，卡片写"影响 x%"；
    //   · 不投靠任何一方的学说朋党，占比不并入任何一家。
    public static int Power(FixedFaction faction)
    {
        Empire empire = faction?.Empire;
        if (faction == null || !faction.IsClique || empire?.CoreKingdom == null) return faction?.CentralRatio ?? 0;
        if (!IsClassClique(faction)) return 0;
        Regime regime = empire.CoreKingdom.GetRegime();
        int power = faction.CentralRatio;
        if (regime == null) return power;
        foreach (FixedFaction school in Schools(regime))
            if (Patron(empire, school) == faction) power += school.CentralRatio;
        return power;
    }

    // 朝中有阶级派系时，主导的是权最大的阶级派系
    public static FixedFaction DominantClass(IEnumerable<FixedFaction> factions) =>
        factions?.Where(faction => faction != null && !faction.Ban && faction.IsClique && IsClassClique(faction))
            .OrderByDescending(Power).FirstOrDefault();

    // 没人领头的后党、阉党不该白占份额：每年把占比归入天子派
    private static void DrainDormant(Empire empire, Regime regime)
    {
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        FixedFaction crown = ClassClique(regime, PartyIdeology.ImperialPower);
        if (ratios == null || crown == null) return;
        foreach (PartyIdeology stance in new[] { PartyIdeology.ConsortKin, PartyIdeology.Eunuchs })
        {
            FixedFaction dormant = ClassClique(regime, stance);
            if (dormant == null || dormant.GetLeader() != null || !ratios.TryGetValue(dormant, out int power) || power <= 0) continue;
            ratios[dormant] = 0;
            ratios[crown] = (ratios.TryGetValue(crown, out int current) ? current : 0) + power;
        }
    }

    #endregion

    #region 投靠

    // 学说朋党投靠利义最合得来的阶级派系(一致程度至少 0.15，否则不投靠任何一方)；玩家指定过的不动
    private const float PatronAgreement = 0.15f;

    private static void AssignPatrons(Empire empire, Regime regime)
    {
        List<FixedFaction> classes = Cliques(regime).Where(IsClassClique).ToList();
        foreach (FixedFaction school in Schools(regime))
        {
            CourtFactionState data = CourtSystem.FactionState(empire, school);
            if (data.patron_locked && (data.patron == "" || classes.Any(clique => clique.GetID() == data.patron))) continue;
            data.patron_locked = false;
            FixedFaction best = classes.Select(clique => (clique, agreement: CourtDoctrine.Agreement(school, clique) ?? 0f))
                .Where(item => item.agreement >= PatronAgreement)
                .OrderByDescending(item => item.agreement).Select(item => item.clique).FirstOrDefault();
            data.patron = best?.GetID() ?? "";
        }
    }

    public static FixedFaction Patron(Empire empire, FixedFaction school)
    {
        if (empire == null || school == null || !school.IsClique || IsClassClique(school)) return null;
        CourtFactionState data = CourtSystem.FactionState(empire, school);
        // 还没结算过(新朋党、旧存档)：当场按利义算一次
        if (!data.patron_locked && string.IsNullOrEmpty(data.patron) && empire.CoreKingdom?.GetRegime() is Regime regime)
            data.patron = Cliques(regime).Where(IsClassClique)
                .Select(clique => (clique, agreement: CourtDoctrine.Agreement(school, clique) ?? 0f))
                .Where(item => item.agreement >= PatronAgreement).OrderByDescending(item => item.agreement)
                .Select(item => item.clique.GetID()).FirstOrDefault() ?? "";
        string id = data.patron;
        return string.IsNullOrEmpty(id) ? null : CourtSystem.FindFaction(empire, id);
    }

    public static bool PatronLocked(Empire empire, FixedFaction school) =>
        empire != null && school != null && CourtSystem.FactionState(empire, school).patron_locked;

    // 两派互为投靠关系(学说朋党与它投靠的阶级派系)
    public static bool Patronage(Empire empire, FixedFaction a, FixedFaction b) =>
        a != null && b != null && (Patron(empire, a) == b || Patron(empire, b) == a);

    // 手动改投(神力)：按 自动 → 各阶级派系 → 不投靠 → 自动 轮换
    public static void CyclePatron(Empire empire, FixedFaction school)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null || school == null || IsClassClique(school)) return;
        CourtFactionState data = CourtSystem.FactionState(empire, school);
        List<FixedFaction> classes = Cliques(regime).Where(IsClassClique).ToList();
        if (!data.patron_locked)
        {
            data.patron_locked = true;
            data.patron = classes.FirstOrDefault()?.GetID() ?? "";
            return;
        }
        int index = classes.FindIndex(clique => clique.GetID() == data.patron);
        if (index >= 0 && index + 1 < classes.Count) data.patron = classes[index + 1].GetID();
        else if (index >= 0) data.patron = "";
        else
        {
            data.patron_locked = false;
            AssignPatrons(empire, regime);
        }
    }

    #endregion

    #region 后党、阉党

    private static void Consorts(Empire empire, Regime regime, List<Actor> officials)
    {
        Actor chief = ConsortKinSystem.Chief(empire);
        List<FixedFaction> cliques = Cliques(regime);
        FixedFaction party = cliques.FirstOrDefault(clique => clique.Ideology == PartyIdeology.ConsortKin);
        bool ascendant = chief?.data != null && (ConsortKinSystem.Backed(empire) || empire.GetPowerfulMinister() == chief);
        if (party == null) return;
        // 蛰伏的后党：外戚首领得势时复起
        if (ascendant && party.GetLeader() == null)
        {
            IdeologyPopulationSystem.Set(chief, PartyIdeology.ConsortKin);
            chief.SetFaction(party);
            party.SetLeader(chief);
            empire.CoreKingdom.TryIncreaseFactionRatio(party, FoundRatio);
            Record(empire, string.Format(LM.Get("clique_consort_history"), chief.getName(), party.Name), chief);
        }
        // 外戚宗族的官员都归入后党
        foreach (Actor actor in officials.Where(actor => !party.Members.Contains(actor.id) && ConsortKinSystem.IsConsortKin(empire, actor)))
        {
            IdeologyPopulationSystem.Set(actor, PartyIdeology.ConsortKin);
            actor.SetFaction(party);
        }
    }

    private static void Eunuchs(Empire empire, Regime regime)
    {
        Actor emperor = empire.Emperor;
        if (emperor?.data == null || !emperor.isAlive()) return;
        List<FixedFaction> cliques = Cliques(regime);
        FixedFaction party = cliques.FirstOrDefault(clique => clique.Ideology == PartyIdeology.Eunuchs);
        if (party == null) return;
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(emperor);
        if (party.GetLeader() != null)
        {
            // 明君亲政：诛宦官、散阉党
            if (grade == RulerQualitySystem.Grade.Wise && emperor.isAdult() && !empire.data.is_been_controlled &&
                Random.value < EunuchPurgeChance)
                Purge(empire, regime, party, emperor);
            return;
        }
        if (grade is not (RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute) ||
            Random.value >= EunuchRiseChance) return;
        City capital = empire.CoreKingdom.capital;
        Actor eunuch = capital == null ? null : CityPopulationSystem.SpawnOfficial(capital, empire.CoreKingdom);
        if (eunuch?.data == null) return;
        eunuch.data.sex = ActorSex.Male;
        eunuch.addTrait(EunuchTrait);
        eunuch.editRenown(60);
        IdeologyPopulationSystem.Set(eunuch, PartyIdeology.Eunuchs);
        // 从天子派里分出阉党：得宠的宦官领头，天子派里的宦官都跟过来，带走天子派三分之一的势力
        eunuch.SetFaction(party);
        party.SetLeader(eunuch);
        FixedFaction crown = ClassClique(regime, PartyIdeology.ImperialPower);
        if (crown != null)
            foreach (Actor member in crown.AllMembers.Where(IsEunuch).ToList()) member.SetFaction(party);
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        if (crown != null && ratios != null && ratios.TryGetValue(crown, out int crownPower) && crownPower >= 3)
        {
            int moved = crownPower / 3;
            ratios[crown] = crownPower - moved;
            ratios[party] = (ratios.TryGetValue(party, out int current) ? current : 0) + moved;
        }
        else empire.CoreKingdom.TryIncreaseFactionRatio(party, EunuchFoundRatio);
        Record(empire, string.Format(LM.Get("clique_eunuch_history"), emperor.getName(), eunuch.getName(), party.Name), eunuch);
    }

    private static void Purge(Empire empire, Regime regime, FixedFaction party, Actor emperor)
    {
        string name = party.Name;
        Actor leader = party.GetLeader();
        string leaderName = leader?.getName() ?? "";
        List<Actor> eunuchs = party.AllMembers.Where(IsEunuch).ToList();
        // 阉党不消失，只是打回蛰伏：首恶伏诛，余众与势力并回天子派
        FixedFaction crown = ClassClique(regime, PartyIdeology.ImperialPower);
        foreach (Actor member in party.AllMembers.ToList())
        {
            if (crown != null && !IsEunuch(member)) member.SetFaction(crown);
            else member.RemoveFaction();
        }
        party.Members.Clear();
        party.Leader = -1L;
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        if (ratios != null && ratios.TryGetValue(party, out int power) && power > 0)
        {
            ratios[party] = 0;
            if (crown != null) ratios[crown] = (ratios.TryGetValue(crown, out int current) ? current : 0) + power;
        }
        foreach (Actor eunuch in eunuchs)
            BiographySystem.Kill(eunuch, BiographySystem.Format("death_cause_eunuch_purge", emperor.getName()), emperor,
                executed: true, destroy: true, countDeath: false);
        BiographySystem.Note(emperor, BiographySystem.Format("bio_eunuchs_purged", leaderName));
        empire.AddMandate(5);
        Record(empire, string.Format(LM.Get("clique_eunuch_purged_history"), emperor.getName(), leaderName, name), emperor);
    }

    #endregion

    private static void Record(Empire empire, string content, Actor actor)
    {
        LogService.LogInfo("[EmpireCraft][朋党] " + content);
        empire.RecordHistory(directContent: content, actorId: actor?.id ?? empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        actor?.RecordPersonalHistory(content);
        if (EventRecorder.ShowRoutine(empire)) TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }
}
