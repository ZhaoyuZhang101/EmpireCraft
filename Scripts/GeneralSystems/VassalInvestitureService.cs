using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public enum InvestitureMode
{
    Granted,        // 册封继承：帝国说了算，嗣君受天子册封
    Petition,       // 请封继承：封国自定嗣君，报天子承认
    SelfProclaimed  // 自立继承：封国直接立君，天子只能追认或兴兵
}

// 诸侯(藩王)继承时的册封判定。
//
// 每次藩王去世、出现继承时现场比较两个 0~100 的分数：
//   宗主权威 A = 天命 35% + 尊王派中央占比 25% + 核心王国/封国实力比 25% + 已施行集权制度 15%
//   封国自主 S = 诸侯派中央占比 30% + 封国占帝国的体量 30% + 受封年数 20% + 对帝国的疏离 20%
// 差值 S-A 决定继承方式；施行「诸侯世守」前诸侯最多只能请封，施行后才可能自立。
// 继承人只有嫡系世袭一种(就是封国自己的继承人)，"自择"指的是由封国而不是帝国来定人选。
//
// 两种封国走同一套判定：
//   · 分封制/封建制的实封附庸国 —— 由 EmpireCraftKingdomBehCheckKing 在附庸国即位时调用；
//   · 律令制的虚封王爵 —— 由 Empire.ProcessLegalPeerageSuccession 在支系承袭时调用，没有军队，不会打仗。
public static class VassalInvestitureService
{
    private const float EscalationGap = 20f;
    private const int DefianceWarYears = 5;
    private const int DefianceVictoryYears = 30;
    private const float DefianceVictoryAutonomyBonus = 15f;

    // 附庸自行继承由制度特性 vassal_self_succession 提供，任何线的节点都可以声明
    public static bool IsSelfSuccessionEnacted(Empire empire) =>
        empire != null && InstitutionSystem.HasFeature(empire, InstitutionFeatures.VassalSelfSuccession);

    public static InvestitureMode Evaluate(float authority, float autonomy, bool selfSuccession)
    {
        float gap = autonomy - authority;
        if (gap <= 0f) return InvestitureMode.Granted;
        if (selfSuccession) return gap > EscalationGap ? InvestitureMode.SelfProclaimed : InvestitureMode.Petition;
        return gap > EscalationGap ? InvestitureMode.Petition : InvestitureMode.Granted;
    }

    #region 分数

    private static float GetSuzerainAuthority(Empire empire, float coreStrengthShare)
    {
        // 已施行的集权制度给宗主权威的加成：制度特性 vassal_authority 的最高值(0~100)
        float centralization = Mathf.Clamp(InstitutionSystem.GetFeature(empire, InstitutionFeatures.VassalAuthority),
            0f, 100f);
        float score = Mathf.Clamp(empire.Mandate, 0, 100) * 0.35f +
                      GetFactionShare(empire, FactionType.尊王, FactionType.中央) * 0.25f +
                      Mathf.Clamp(coreStrengthShare, 0f, 100f) * 0.25f +
                      centralization * 0.15f;
        return Mathf.Clamp(score, 0f, 100f);
    }

    private static float GetVassalAutonomy(Empire empire, float sizeShare, float tenureScore, float estrangement,
        float bonus)
    {
        float score = GetFactionShare(empire, FactionType.诸侯, FactionType.自治) * 0.30f +
                      Mathf.Clamp(sizeShare, 0f, 100f) * 0.30f +
                      Mathf.Clamp(tenureScore, 0f, 100f) * 0.20f +
                      Mathf.Clamp(estrangement, 0f, 100f) * 0.20f +
                      bonus;
        return Mathf.Clamp(score, 0f, 100f);
    }

    // 同一立场在不同政体下叫法不同(华夏"尊王/诸侯"、西方"中央/自治")，取其中最强的一支
    private static float GetFactionShare(Empire empire, params FactionType[] types)
    {
        return empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
                   ?.Where(faction => faction != null && !faction.Ban && types.Contains(faction.Type))
                   .Select(faction => (float)faction.CentralRatio).DefaultIfEmpty(0f).Max()
               ?? 0f;
    }

    private static FixedFaction GetVassalFaction(Empire empire)
    {
        return empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.Where(faction => faction != null && !faction.Ban &&
                               faction.Type is FactionType.诸侯 or FactionType.自治)
            .OrderByDescending(faction => faction.CentralRatio).FirstOrDefault();
    }

    // 有效兵力：只看这个王国自己的在编士兵。帝国直辖的战争不会调动下辖行政区，没钱招不到新兵，
    // 正统(天命)低落还会拖累战斗力，所以核心王国再大也可能一个能打的兵都没有。
    private static float GetMilitaryStrength(Kingdom kingdom, Empire empireOfCore = null)
    {
        if (kingdom == null || kingdom.isRekt()) return 0f;
        float strength = Math.Max(0, kingdom.countTotalWarriors());
        if (kingdom.GetMoney() <= 0) strength *= 0.7f;
        if (empireOfCore != null) strength *= 0.5f + 0.5f * Mathf.Clamp(empireOfCore.Mandate, 0, 100) / 100f;
        return strength;
    }

    private static void EvaluateKingdom(Empire empire, Kingdom vassal, out float authority, out float autonomy)
    {
        float coreStrength = GetMilitaryStrength(empire.CoreKingdom, empire);
        float vassalStrength = GetMilitaryStrength(vassal);
        float coreShare = coreStrength + vassalStrength <= 0f
            ? 50f
            : 100f * coreStrength / (coreStrength + vassalStrength);

        int empireCities = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.countCities());
        float sizeShare = empireCities <= 0 ? 0f : 100f * vassal.countCities() / empireCities;

        double fiefTimestamp = vassal.GetFiedTimestamp();
        float tenure = fiefTimestamp < 0 ? 0f : Date.getYearsSince(fiefTimestamp) * 2f;

        int opinion = World.world?.diplomacy?.getOpinion(vassal, empire.CoreKingdom)?.total ?? 0;
        float estrangement = 50f - opinion * 0.5f;

        double victory = vassal.GetOrCreate().investiture_defiance_victory_timestamp;
        float bonus = victory >= 0 && Date.getYearsSince(victory) < DefianceVictoryYears
            ? DefianceVictoryAutonomyBonus
            : 0f;

        authority = GetSuzerainAuthority(empire, coreShare);
        autonomy = GetVassalAutonomy(empire, sizeShare, tenure, estrangement, bonus);
    }

    #endregion

    #region 实封附庸国

    // 附庸国的嗣君刚刚即位(已经是国王)时调用。
    public static void OnVassalSuccession(Kingdom vassal, Actor heir)
    {
        if (vassal == null || vassal.isRekt() || vassal.IsEmpire() || !vassal.IsInEmpire()) return;
        if (vassal.IsLocalRebelling() || vassal.IsFactionRebelling()) return;
        Empire empire = vassal.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null ||
            empire.CoreKingdom.isRekt() || empire.CoreKingdom == vassal) return;
        if (heir == null || heir.isRekt() || !heir.isAlive() || vassal.king != heir) return;

        EvaluateKingdom(empire, vassal, out float authority, out float autonomy);
        InvestitureMode mode = Evaluate(authority, autonomy, IsSelfSuccessionEnacted(empire));
        string vassalName = vassal.GetKingdomName();
        switch (mode)
        {
            case InvestitureMode.Granted:
                Record(empire, vassal, heir, "investiture_granted_history", heir.getName(), vassalName);
                return;
            case InvestitureMode.Petition:
            {
                Actor replacement = RefusesPetition(authority, autonomy) ? FindReplacement(empire, heir) : null;
                if (replacement == null || !ReplaceKing(vassal, replacement))
                {
                    empire.AddMandate(2);
                    Record(empire, vassal, heir, "investiture_petition_approved_history", heir.getName(), vassalName);
                    return;
                }
                PunishRefusal(empire);
                Record(empire, vassal, replacement, "investiture_petition_refused_history", heir.getName(),
                    vassalName, replacement.getName());
                return;
            }
            case InvestitureMode.SelfProclaimed:
            {
                Actor replacement = WillWageDefianceWar(empire, vassal) ? FindReplacement(empire, heir) : null;
                if (replacement == null || !StartDefianceWar(empire, vassal, heir, replacement))
                {
                    empire.AddMandate(-2);
                    Record(empire, vassal, heir, "investiture_self_ratified_history", heir.getName(), vassalName);
                    return;
                }
                Record(empire, vassal, heir, "investiture_defiance_war_history", heir.getName(), vassalName,
                    replacement.getName());
                return;
            }
        }
    }

    // 请封：封国越占上风，天子越不敢驳回。差值 0~20 时驳回概率约 25%~50%，更大则更低。
    private static bool RefusesPetition(float authority, float autonomy)
    {
        float chance = Mathf.Clamp((40f - (autonomy - authority)) / 80f, 0.05f, 0.5f);
        return Randy.randomChance(chance);
    }

    // 自立：只有核心王国自己有兵、有钱养兵、天命尚在，且有效兵力达到封国 1.5 倍时才敢兴兵，否则只能追认。
    private static bool WillWageDefianceWar(Empire empire, Kingdom vassal)
    {
        if (empire.Mandate < 40 || empire.CoreKingdom.GetMoney() <= 0) return false;
        float core = GetMilitaryStrength(empire.CoreKingdom, empire);
        if (core <= 0f) return false;
        return core >= GetMilitaryStrength(vassal) * 1.5f && Randy.randomChance(0.6f);
    }

    private static void PunishRefusal(Empire empire)
    {
        empire.AddMandate(-3);
        FixedFaction vassalFaction = GetVassalFaction(empire);
        if (vassalFaction != null) empire.CoreKingdom.TryIncreaseFactionRatio(vassalFaction, 3);
    }

    private static bool ReplaceKing(Kingdom vassal, Actor replacement)
    {
        if (vassal == null || vassal.isRekt() || replacement == null || replacement.isRekt()) return false;
        replacement.removeFromArmy();
        if (replacement.isCityLeader()) replacement.city.removeLeader();
        if (vassal.capital != null && !vassal.capital.isRekt() && replacement.city != vassal.capital)
            replacement.joinCity(vassal.capital);
        OfficeObject office = vassal.GetOffice();
        if (office == null)
        {
            vassal.InitialRegime();
            office = vassal.GetOffice();
        }
        if (office != null)
        {
            office.meta_object = vassal;
            office.SetActor(replacement);
        }
        else
        {
            vassal.setKing(replacement);
        }
        return vassal.king == replacement;
    }

    #endregion

    #region 不奉诏战争

    private static bool StartDefianceWar(Empire empire, Kingdom vassal, Actor heir, Actor replacement)
    {
        War war = DiplomacyHelpers.diplomacy.startWar(empire.CoreKingdom, vassal, WarTypeLibrary.normal);
        if (war == null) return false;
        war.SetEmpireWarType(EmpireWarType.不奉诏);
        WarExtension.WarExtraData data = war.GetOrCreate();
        data.investiture_vassal_kingdom_id = vassal.id;
        data.investiture_heir_id = heir.id;
        data.investiture_replacement_id = replacement.id;
        return true;
    }

    // 帝国一方控制了抗命封国都城过半的区块，就算王师平定。
    public static bool IsDefiantCapitalTaken(War war)
    {
        Kingdom vassal = war?.getMainDefender();
        City capital = vassal?.capital;
        if (capital?.zones == null || capital.isRekt()) return false;
        if (war._list_attackers.Contains(capital.kingdom)) return true;
        int total = 0;
        int controlled = 0;
        foreach (TileZone zone in capital.zones)
        {
            if (zone == null || zone.world_edge || zone.city != capital) continue;
            total++;
            Kingdom occupier = capital.GetTileZoneOccupier(zone);
            if (occupier != null && war._list_attackers.Contains(occupier)) controlled++;
        }
        return total > 0 && controlled * 2 >= total;
    }

    // 由 WarPatch.update 每帧调用：封国被灭/都城失守→帝国胜；坚持满 5 年→封国胜。
    public static void CheckDefianceWar(War war)
    {
        if (war == null || war.hasEnded()) return;
        if (war.IsMainDefenderEliminated() || IsDefiantCapitalTaken(war))
            World.world.wars.endWar(war, WarWinner.Attackers);
        else if (war.getDuration() >= DefianceWarYears)
            World.world.wars.endWar(war, WarWinner.Defenders);
    }

    public static void ResolveDefianceWar(War war, WarWinner winner)
    {
        WarExtension.WarExtraData data = war.GetOrCreate();
        Kingdom vassal = World.world.kingdoms.get(data.investiture_vassal_kingdom_id);
        Kingdom core = war.getMainAttacker();
        Empire empire = core?.GetEmpire();
        if (vassal == null || vassal.isRekt() || empire == null || empire.isRekt()) return;
        Actor heir = World.world.units.get(data.investiture_heir_id);
        string heirName = heir?.getName() ?? "";
        string vassalName = vassal.GetKingdomName();

        if (winner == WarWinner.Attackers)
        {
            Actor replacement = World.world.units.get(data.investiture_replacement_id);
            if (replacement == null || replacement.isRekt() || !replacement.isAlive() || replacement.isKing())
                replacement = heir != null ? FindReplacement(empire, heir) : null;
            if (replacement != null && ReplaceKing(vassal, replacement))
            {
                Record(empire, vassal, replacement, "investiture_defiance_empire_won_history", heirName, vassalName,
                    replacement.getName());
                return;
            }
        }

        // 封国守住了(或者帝国胜了却找不到人可立)：天子只能承认，封国此后三十年更加自主
        vassal.GetOrCreate().investiture_defiance_victory_timestamp = World.world.getCurWorldTime();
        empire.AddMandate(-5);
        Record(empire, vassal, vassal.king, "investiture_defiance_vassal_won_history", vassal.king?.getName() ?? heirName,
            vassalName);
    }

    #endregion

    #region 律令制虚封王爵

    // 律令制王爵的支系承袭。branchHeir 是老藩王这一支的后代，crownCandidate 是按"皇帝兄弟→皇子"
    // 顺序由朝廷另选的人(可能为 null)。返回最终承袭的人；改立时 predecessor 置空(视为新封)。
    public static Actor ResolveVirtualSuccession(Empire empire, KingdomTitle title, Actor branchHeir,
        Actor crownCandidate, ref PersonalClanIdentity predecessor)
    {
        if (empire?.CoreKingdom == null || title == null || branchHeir == null) return branchHeir;
        int empireCities = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.countCities());
        int titleCities = title.getCities().Count(city => city != null && !city.isRekt());
        float sizeShare = empireCities <= 0 ? 0f : 100f * titleCities / empireCities;
        float coreShare = 100f - sizeShare;
        float authority = GetSuzerainAuthority(empire, coreShare);
        // 虚封王爵没有独立王国：受封年数和好感取中值
        float autonomy = GetVassalAutonomy(empire, sizeShare, 50f, 50f, 0f);
        InvestitureMode mode = Evaluate(authority, autonomy, IsSelfSuccessionEnacted(empire));
        string titleName = title.data?.name ?? "";
        if (mode == InvestitureMode.Granted) return branchHeir;

        // 请封与自立在律令制下没有军队可恃，驳回时一律直接改立
        bool refuse = mode == InvestitureMode.Petition
            ? RefusesPetition(authority, autonomy)
            : empire.Mandate >= 40 && Randy.randomChance(0.6f);
        if (!refuse || crownCandidate == null)
        {
            string key = mode == InvestitureMode.Petition
                ? "investiture_petition_approved_history"
                : "investiture_self_ratified_history";
            empire.AddMandate(mode == InvestitureMode.Petition ? 2 : -2);
            Record(empire, null, branchHeir, key, branchHeir.getName(), titleName);
            return branchHeir;
        }
        PunishRefusal(empire);
        Record(empire, null, crownCandidate, "investiture_petition_refused_history", branchHeir.getName(), titleName,
            crownCandidate.getName());
        predecessor = null;
        return crownCandidate;
    }

    #endregion

    // 改立人选，跟律令制王爵的顺序一致：原嗣君的兄弟(同出老藩王一支) → 皇帝的兄弟 → 太子以外的皇子
    private static Actor FindReplacement(Empire empire, Actor heir)
    {
        long crownPrinceId = empire.CoreKingdom?.GetHeir()?.id ?? -1L;
        bool IsAvailable(PersonalClanIdentity identity)
        {
            Actor actor = identity?._actor;
            return actor != null && !actor.isRekt() && actor.isAlive() && !actor.isKing() && actor != heir &&
                   actor.id != crownPrinceId && identity.CanHeir() && actor.kingdom?.GetEmpire() == empire &&
                   !actor.HasVirtualEnfeoff(empire);
        }
        Actor Pick(IEnumerable<(ClanRelation, PersonalClanIdentity)> relatives) => relatives
            .Select(item => item.Item2).Where(IsAvailable).OrderBy(identity => identity.rank)
            .Select(identity => identity._actor).FirstOrDefault();

        PersonalClanIdentity heirIdentity = heir?.GetPersonalIdentity();
        PersonalClanIdentity emperorIdentity = empire.Emperor?.GetPersonalIdentity();
        return (heirIdentity == null ? null : Pick(SpecificClanManager.GetSiblingsWithRelation(heirIdentity)))
               ?? (emperorIdentity == null ? null : Pick(SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)))
               ?? (emperorIdentity == null ? null : Pick(SpecificClanManager.getChildren(emperorIdentity)));
    }

    private static void Record(Empire empire, Kingdom vassal, Actor actor, string key, params object[] args)
    {
        // 西方帝国(古典共和、西方封建)用西式措辞：皇帝授封而非"天子册封"
        if (PersonalUnionService.IsUnionRegime(empire.CoreKingdom) && LM.Has(key + "_western")) key += "_western";
        string content = string.Format(LM.Get(key), args);
        empire.RecordHistory(directContent: content, actorId: actor?.id ?? -1L,
            kingdomId: vassal?.id ?? empire.CoreKingdom?.id ?? -1L);
        // 正常册封是例行公事，只记入史书，不弹屏幕提示
        if (!key.StartsWith("investiture_granted_history")) ActionLibrary.showWhisperTip(content);
    }
}
