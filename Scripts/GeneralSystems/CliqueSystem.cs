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
//   · 合并：势弱(中央占比不到八)、立党满三年的朋党并入同理念(必定)或理念相近的大党(三成)；
//   · 分裂：中央占比四成以上、六人以上的大党里，籍贯与党魁不同的一乡三人以上，每年一成机会别立"某党"；
//   · 消亡：党员走光就散；朝中至少留两个朋党；
//   · 后党：外戚首领(见 ConsortKinSystem)得势(外戚干政、或他就是权臣)时倡立后党，外戚宗族的官员都归入；
//   · 阉党：庸弱、昏聩、荒淫之君在位，每年两成机会京城有得宠的宦官倡立阉党(宦官带"宦官"特质，见 IsEunuch)；
//     明君亲政，每年三成机会诛宦官、散阉党；
//   · 开放党禁以后，朋党照常改组为近代政党(见 PartySystem.Reorganize)。
public static class CliqueSystem
{
    public const string EunuchTrait = "eunuch";
    private const float FoundShare = 0.15f, SwitchChance = 0.35f, MergeRatio = 8f, MergeChance = 0.3f, MergeDistance = 70f,
        SplitRatio = 40f, SplitChance = 0.1f, EunuchRiseChance = 0.2f, EunuchPurgeChance = 0.3f;
    private const int FoundMinMembers = 3, MinOfficials = 4, MaxCliques = 6, MinCliques = 2, FoundRecruits = 6,
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

    private static List<FixedFaction> Cliques(Regime regime) =>
        regime.GetPlayerFactions().Where(faction => faction != null && faction.IsClique && !faction.Ban).ToList();

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
            Convert(empire, regime);
            List<Actor> officials = Officials(empire);
            Eunuchs(empire, regime);
            Consorts(empire, regime, officials);
            TryMerge(empire, regime);
            TryDissolve(empire, regime);
            TryFound(empire, regime, officials);
            Drift(regime, officials);
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
            if (faction == null || faction.IsClique || faction.IsParty || faction.only_king) continue;
            PartyIdeology ideology = TypeIdeology(faction.Type) ?? PartySystem.LeaningOf(faction);
            faction.IsClique = true;
            faction.Ideology = ideology;
            faction.CliqueFoundedAt = -1d;
            faction.EmpireId = empire.getID();
        }
    }

    #endregion

    #region 倡立

    private static void TryFound(Empire empire, Regime regime, List<Actor> officials)
    {
        List<FixedFaction> cliques = Cliques(regime);
        if (cliques.Count >= MaxCliques || officials.Count < MinOfficials) return;
        var represented = new HashSet<PartyIdeology>(cliques.Select(clique => clique.Ideology));
        var leaders = new HashSet<long>(cliques.Select(clique => clique.Leader));
        foreach (IGrouping<PartyIdeology, Actor> group in officials
                     .GroupBy(IdeologyPopulationSystem.Get)
                     .Where(group => IdeologyFamilies.IsAncient(group.Key) && !IdeologyFamilies.IsCourtOnly(group.Key) &&
                                     !represented.Contains(group.Key))
                     .OrderByDescending(group => group.Count()))
        {
            if (group.Count() < Mathf.Max(FoundMinMembers, Mathf.CeilToInt(officials.Count * FoundShare))) break;
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
            TemporaryFactionTypesRecord = new List<TemporaryFactionType>(),
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

    private static void Drift(Regime regime, List<Actor> officials)
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
            if (leaders.Contains(actor.id) || !byIdeology.TryGetValue(IdeologyPopulationSystem.Get(actor), out FixedFaction home)) continue;
            FixedFaction current = cliques.FirstOrDefault(clique => clique.Members.Contains(actor.id));
            if (current == home || current != null && current.Ideology == home.Ideology || Random.value > SwitchChance) continue;
            actor.SetFaction(home);
            switches++;
        }
    }

    private static void TryMerge(Empire empire, Regime regime)
    {
        List<FixedFaction> cliques = Cliques(regime);
        if (cliques.Count <= MinCliques) return;
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
        List<FixedFaction> cliques = Cliques(regime);
        if (cliques.Count <= MinCliques) return;
        FixedFaction empty = cliques.FirstOrDefault(clique => clique.CliqueFoundedAt >= 0d && clique.AllMembers.Count == 0);
        if (empty == null) return;
        string name = empty.Name;
        Remove(empire, regime, empty);
        Record(empire, string.Format(LM.Get("clique_dissolved_history"), name), null);
    }

    private static void TrySplit(Empire empire, Regime regime)
    {
        List<FixedFaction> cliques = Cliques(regime);
        if (cliques.Count >= MaxCliques || Random.value > SplitChance) return;
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

    #region 后党、阉党

    private static void Consorts(Empire empire, Regime regime, List<Actor> officials)
    {
        Actor chief = ConsortKinSystem.Chief(empire);
        List<FixedFaction> cliques = Cliques(regime);
        FixedFaction party = cliques.FirstOrDefault(clique => clique.Ideology == PartyIdeology.ConsortKin);
        bool ascendant = chief?.data != null && (ConsortKinSystem.Backed(empire) || empire.GetPowerfulMinister() == chief);
        if (party == null)
        {
            if (!ascendant || cliques.Count >= MaxCliques) return;
            IdeologyPopulationSystem.Set(chief, PartyIdeology.ConsortKin);
            party = Create(empire, regime, PartyIdeology.ConsortKin, chief, FoundRatio);
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
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(emperor);
        if (party != null)
        {
            // 明君亲政：诛宦官、散阉党
            if (grade == RulerQualitySystem.Grade.Wise && emperor.isAdult() && !empire.data.is_been_controlled &&
                Random.value < EunuchPurgeChance)
                Purge(empire, regime, party, emperor);
            return;
        }
        if (grade is not (RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute) ||
            cliques.Count >= MaxCliques || Random.value >= EunuchRiseChance) return;
        City capital = empire.CoreKingdom.capital;
        Actor eunuch = capital == null ? null : CityPopulationSystem.SpawnOfficial(capital, empire.CoreKingdom);
        if (eunuch?.data == null) return;
        eunuch.data.sex = ActorSex.Male;
        eunuch.addTrait(EunuchTrait);
        eunuch.editRenown(60);
        IdeologyPopulationSystem.Set(eunuch, PartyIdeology.Eunuchs);
        party = Create(empire, regime, PartyIdeology.Eunuchs, eunuch, EunuchFoundRatio);
        Record(empire, string.Format(LM.Get("clique_eunuch_history"), emperor.getName(), eunuch.getName(), party.Name), eunuch);
    }

    private static void Purge(Empire empire, Regime regime, FixedFaction party, Actor emperor)
    {
        string name = party.Name;
        Actor leader = party.GetLeader();
        string leaderName = leader?.getName() ?? "";
        List<Actor> eunuchs = party.AllMembers.Where(IsEunuch).ToList();
        Remove(empire, regime, party);
        foreach (Actor eunuch in eunuchs) eunuch.die(true, AttackType.Other, false, false);
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
