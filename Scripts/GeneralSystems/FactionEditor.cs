using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 派系的手动编辑(用户 2026-10-19 转玩家反馈"党派设置能不能再优化一下，比如可以直接添加默认派系""自定义派系为啥只有中间派"，
// 又要"派系名称允许自动用魁首名字命名比如严党这种，也可以允许使用默认名称"；看过示意图定"照这个做")：
//   · 朝堂分三种：开放党禁后是政党(组党照 PartySystem 的条件)，华夏党禁前是朋党(CliqueSystem)，其余是固定派系(至多三席)；
//   · 添加(见 AddFactionWindow)：本政体的默认派系、按理念新建、派系库里存过的；
//   · 改理念(见 FactionDetailWindow)：派系类型、诉求清单、各阶层亲疏按新理念重设，理念记为手选，不再由阶层倾向推算；
//   · 起名：自定 / 默认名(随理念) / 魁首之姓("某党"，魁首换人跟着改)。
public static class FactionEditor
{
    public enum Court { Fixed, Clique, Party }

    public const int FixedSeats = 3, CliqueSeats = 8;
    public const int NameCustom = 0, NameDefault = 1, NameLeader = 2;

    // 古代理念(诸子百家、朝堂立场、上古信仰)与近代政党理念，按窗口里的显示顺序
    public static readonly PartyIdeology[] Ancient =
    {
        PartyIdeology.Confucianism, PartyIdeology.Legalism, PartyIdeology.HuangLao, PartyIdeology.Mohism,
        PartyIdeology.Agrarianism, PartyIdeology.Strategists, PartyIdeology.Diplomatists, PartyIdeology.YinYang,
        PartyIdeology.ImperialPower, PartyIdeology.GreatClans, PartyIdeology.ConsortKin, PartyIdeology.Eunuchs,
        PartyIdeology.RoyalKin, PartyIdeology.MilitaryMerit, PartyIdeology.Animism
    };

    public static readonly PartyIdeology[] Modern =
    {
        PartyIdeology.Communism, PartyIdeology.Socialism, PartyIdeology.SocialDemocracy, PartyIdeology.SocialLiberalism,
        PartyIdeology.Centrism, PartyIdeology.ConservativeLiberalism, PartyIdeology.Capitalism,
        PartyIdeology.Libertarianism, PartyIdeology.Conservatism, PartyIdeology.ReligiousDemocracy,
        PartyIdeology.Authoritarianism, PartyIdeology.Fascism, PartyIdeology.Anarchism
    };

    // 派系挂在帝国核心上(中央占比、领袖、诉求都按帝国算)；不是帝国核心的国家返回空
    public static Empire EmpireOf(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom == kingdom ? empire : null;
    }

    public static Court CourtOf(Kingdom kingdom)
    {
        Empire empire = EmpireOf(kingdom);
        if (empire == null) return Court.Fixed;
        if (PartySystem.IsActive(empire)) return Court.Party;
        return CliqueSystem.Applies(empire) ? Court.Clique : Court.Fixed;
    }

    public static List<FixedFaction> Current(Kingdom kingdom) =>
        kingdom?.GetRegime()?.GetPlayerFactions()?.Where(faction => faction != null).ToList() ?? new List<FixedFaction>();

    public static int Seats(Kingdom kingdom) => CourtOf(kingdom) switch
    {
        Court.Party => PartySystem.PlayerMaxParties,
        Court.Clique => CliqueSeats,
        _ => FixedSeats
    };

    public static bool HasRoom(Kingdom kingdom) => Current(kingdom).Count < Seats(kingdom);

    // 朝堂的文化：帝国核心按帝国的制度文化，其余按本国文化(古代学说的叫法、有无按它，见 CultureDoctrines)
    public static string CultureOf(Kingdom kingdom) => CultureDoctrines.CultureOf(kingdom);

    // 这一朝堂按理念能立(或改成)的理念：政党时代是近代理念，此前是古代理念——本文化线的学说(华夏是诸子百家，
    // 西方是经院正统、法学派……按科技陆续出现)、朝堂立场与上古信仰，本文化线没有的(西方没有宦官……)不列；
    // 固定派系另有一格"空白"(中间派)。reason 为空表示可选
    public static List<(PartyIdeology ideology, string reason)> Options(Kingdom kingdom, FixedFaction editing = null)
    {
        var result = new List<(PartyIdeology, string)>();
        Court court = CourtOf(kingdom);
        Empire empire = EmpireOf(kingdom);
        if (court == Court.Party || editing?.IsParty == true)
        {
            foreach (PartyIdeology ideology in Modern)
            {
                string reason = null;
                if (empire == null) reason = "party_found_inactive";
                else if (editing != null) { if (!PartySystem.IsIdeologyUnlocked(empire, ideology)) reason = "party_found_locked"; }
                else if (!PartySystem.CanPlayerFound(empire, ideology, out string why)) reason = why;
                result.Add((ideology, reason));
            }
            return result;
        }
        string culture = CultureOf(kingdom);
        string set = CultureDoctrines.SetOf(culture);
        foreach (PartyIdeology ideology in Ancient)
        {
            if (!CultureDoctrines.Has(set, ideology)) continue;
            // 学说随科技出现(以前只认开放党禁后才有的理念节点，党禁前一家也选不了)
            string reason = CultureDoctrines.Emerged(culture, ideology) ? null : "faction_ideology_not_yet";
            result.Add((ideology, reason));
        }
        if (court == Court.Fixed && editing?.IsClique != true) result.Add((PartyIdeology.Centrism, null));
        return result;
    }

    // 已在朝(同一理念的派系已经有了)
    public static bool Seated(Kingdom kingdom, PartyIdeology ideology) =>
        Current(kingdom).Any(faction => PartySystem.LeaningOf(faction) == ideology);

    // 本政体的默认派系(律令制的文官派、武官派……)：只有固定派系的朝堂用
    public static List<FixedFaction> Defaults(Kingdom kingdom) =>
        CourtOf(kingdom) == Court.Fixed
            ? kingdom?.GetRegime()?.Factions?.Where(faction => faction != null).ToList() ?? new List<FixedFaction>()
            : new List<FixedFaction>();

    public static bool DefaultSeated(Kingdom kingdom, FixedFaction template) =>
        template != null && Current(kingdom).Any(faction => !faction.IsParty && !faction.IsClique && faction.Type == template.Type);

    public static FixedFaction AddDefault(Kingdom kingdom, FixedFaction template, out string reason)
    {
        reason = Check(kingdom);
        if (reason != null || template == null) return null;
        if (DefaultSeated(kingdom, template)) { reason = "faction_add_seated"; return null; }
        FixedFaction faction = template.Clone();
        faction.NameMode = NameDefault;
        Attach(kingdom, faction);
        return faction;
    }

    // 派系库里存过的：复制一份入朝(以前直接把库里那一份挂进国家，改国家的派系连库里的也改了)
    public static FixedFaction AddFromLibrary(Kingdom kingdom, FixedFaction preset, out string reason)
    {
        reason = Check(kingdom);
        if (reason != null || preset == null) return null;
        FixedFaction faction = preset.DeepClone();
        faction.TemporaryFactionTypesRecord = new List<TemporaryFactionType>(faction.TemporaryFactionTypesRecord ??
                                                                             new List<TemporaryFactionType>());
        Attach(kingdom, faction);
        return faction;
    }

    public static FixedFaction CreateByIdeology(Kingdom kingdom, PartyIdeology ideology, out string reason)
    {
        reason = Check(kingdom);
        if (reason != null) return null;
        if (Seated(kingdom, ideology)) { reason = "faction_add_seated"; return null; }
        (PartyIdeology _, string why) = Options(kingdom).FirstOrDefault(option => option.ideology == ideology);
        if (!Options(kingdom).Any(option => option.ideology == ideology)) { reason = "faction_ideology_unavailable"; return null; }
        if (why != null) { reason = why; return null; }
        Empire empire = EmpireOf(kingdom);
        switch (CourtOf(kingdom))
        {
            case Court.Party:
                FixedFaction party = PartySystem.PlayerFoundParty(empire, ideology);
                if (party == null) reason = "party_found_no_founder";
                return party;
            case Court.Clique:
                FixedFaction clique = CliqueSystem.CreateManual(empire, ideology);
                if (clique != null) clique.NameMode = NameDefault;
                return clique;
        }
        var faction = new FixedFaction
        {
            _id = Guid.NewGuid().ToString(),
            Members = new List<long>(),
            TemporaryFactions = new List<TemporaryFaction>(),
            Leader = -1L,
            NameMode = NameDefault
        };
        Apply(faction, ideology);
        faction.Name = DefaultName(kingdom, faction);
        Attach(kingdom, faction);
        return faction;
    }

    private static string Check(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetRegime() == null) return "faction_add_none";
        return HasRoom(kingdom) ? null : "faction_add_full";
    }

    // 入朝：记上所属帝国、补齐诉求、重算各派中央占比
    private static void Attach(Kingdom kingdom, FixedFaction faction)
    {
        Regime regime = kingdom.GetRegime();
        faction.EmpireId = EmpireOf(kingdom)?.getID() ?? -1L;
        faction.Members ??= new List<long>();
        faction.TemporaryFactions ??= new List<TemporaryFaction>();
        faction.FixMissedTemporaryFactions();
        regime.GetPlayerFactions().Add(faction);
        kingdom.ReconcileFactionRatios(regime.GetPlayerFactions());
        faction.Update();
    }

    // 改理念：派系类型(诉求清单)、各阶层亲疏按新理念重设；理念记为手选。已有的诉求进度作废
    public static bool ChangeIdeology(Kingdom kingdom, FixedFaction faction, PartyIdeology ideology, out string reason)
    {
        reason = null;
        if (faction == null) { reason = "faction_add_none"; return false; }
        if (faction.Ideology == ideology && (faction.IsParty || faction.IsClique || faction.IdeologyChosen)) return false;
        var option = Options(kingdom, faction).FirstOrDefault(entry => entry.ideology == ideology);
        if (!Options(kingdom, faction).Any(entry => entry.ideology == ideology)) { reason = "faction_ideology_unavailable"; return false; }
        if (option.reason != null) { reason = option.reason; return false; }
        Apply(faction, ideology);
        faction.TemporaryFactions = new List<TemporaryFaction>();
        faction.FixMissedTemporaryFactions();
        if (faction.NameMode == NameDefault) faction.Name = DefaultName(kingdom, faction);
        faction.Update();
        return true;
    }

    private static void Apply(FixedFaction faction, PartyIdeology ideology)
    {
        faction.Ideology = ideology;
        if (!faction.IsParty && !faction.IsClique) faction.IdeologyChosen = true;
        faction.Type = PartySystem.FactionTypeOf(ideology);
        faction.TemporaryFactionTypesRecord = FactionManager.FactionConfig.TryGetValue(faction.Type, out var catalog)
            ? new List<TemporaryFactionType>(catalog)
            : new List<TemporaryFactionType>();
        faction.ClaimCatalogVersion = FixedFaction.CurrentClaimCatalogVersion;
        FactionClassSystem.EnsureProfile(faction);
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
            faction.ClassAffinities[socialClass] = PartySystem.GetAffinity(ideology, socialClass);
    }

    #region 起名

    public static void SetNameMode(Kingdom kingdom, FixedFaction faction, int mode)
    {
        if (faction == null) return;
        faction.NameMode = mode;
        if (mode == NameDefault) faction.Name = DefaultName(kingdom, faction);
        else if (mode == NameLeader) FollowLeaderName(faction);
    }

    // 手改了名字：以后不再自动改
    public static void Rename(FixedFaction faction, string name)
    {
        if (faction == null || string.IsNullOrWhiteSpace(name)) return;
        faction.Name = name.Trim();
        faction.NameMode = NameCustom;
    }

    // 默认名：政党取本文化这一理念的党名；本政体默认派系里同类型的用它的名字(文官派、皇权派……)；
    // 其余按理念的史书称号(清流、变法派、帝党……；别的文化线按本线的叫法，如西方的王党、骑士派)，重名加序号；空白派系叫"新派系"
    public static string DefaultName(Kingdom kingdom, FixedFaction faction)
    {
        Regime regime = kingdom?.GetRegime();
        Empire empire = EmpireOf(kingdom);
        if (faction.IsParty && empire != null) return PartySystem.DefaultPartyName(empire, faction.Ideology);
        if (!faction.IdeologyChosen && !faction.IsClique &&
            regime?.Factions?.FirstOrDefault(template => template != null && template.Type == faction.Type) is FixedFaction match)
            return match.Name;
        string label = faction.Ideology == PartyIdeology.Centrism
            ? LM.Get("faction_blank_name")
            : CultureDoctrines.Label(faction.Ideology, CultureOf(kingdom));
        var used = new HashSet<string>(Current(kingdom).Where(other => other != faction && other.Name != null)
            .Select(other => other.Name));
        if (!used.Contains(label)) return label;
        for (int i = 2; i < 100; i++)
        {
            string numbered = string.Format(LM.Get("party_name_duplicate"), label, i);
            if (!used.Contains(numbered)) return numbered;
        }
        return label;
    }

    // 魁首之姓："严党"；没有姓的用全名
    public static string LeaderName(FixedFaction faction)
    {
        Actor leader = faction?.GetLeader();
        if (leader == null) return null;
        string surname = SurnameInheritanceSystem.FamilyNameOf(leader);
        if (string.IsNullOrWhiteSpace(surname)) surname = leader.getName();
        return string.IsNullOrWhiteSpace(surname) ? null : string.Format(LM.Get("clique_name_party"), surname);
    }

    // 按魁首之姓起名的：魁首换了人跟着改(FixedFaction.SetLeader、Update 调)
    public static void FollowLeaderName(FixedFaction faction)
    {
        if (faction == null || faction.NameMode != NameLeader) return;
        string name = LeaderName(faction);
        if (!string.IsNullOrEmpty(name) && name != faction.Name) faction.Name = name;
    }

    #endregion
}
