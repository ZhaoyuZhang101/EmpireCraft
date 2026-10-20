using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 神圣罗马式的选帝制帝国(封建制、帝位由选帝侯选举——施行了金玺诏书)：
//   · 家族领地：当选的诸侯带着自家领国成为帝国中央，帝都随之搬到他的都城；原来的中央降为普通诸侯国、仍归原皇族。
//     选举换了家族不算篡位(不额外扣正统、旧皇族不出走、不改国号，见 Empire.NewEmperor)；
//   · 家族领地占帝国城数：不到一成正统 -8，二成五以上 +5，四成以上 +8；
//     不到一成时，城比中央多的诸侯不听调遣(见 EmpirePeacePatch.Obeys)；皇帝远征加冕在外时，城比中央一半多的也不听；
//   · 选举让步：帝位换了一家(或选立储君)时随机让出一项——帝国税降一档、某选帝侯所在国恢复外交与开战权、
//     自治派系 +5、中央一座非都城的城封给最强的选帝侯；十年内至多让一次，各项有底线(见 Concede)；
//   · 选立储君(原型"罗马人民的国王")：皇帝在世、有成年子嗣时，每年有两成可能让选帝侯先把长子选为储君
//     (让出一项、花一成年度经常收入)；皇帝死后他直接即位，不再另选。同一家族连续当选记届数。
// 其余见各分部：Diet 帝国议会与禁令、Members 自由城市与教会诸侯、Reform 帝国改革与分割继承、
// Leagues 诸侯同盟与和约、加冕远征。选帝制只管上面几条；其余凡封建制帝国(HasDiet)都适用。
public static partial class HolyRomanSystem
{
    private const float KingOfRomansChance = 0.2f;
    private const float KingOfRomansCost = 0.1f;
    private const float WeakHouse = 0.1f, StrongHouse = 0.25f, DominantHouse = 0.4f;
    // 选举让步：两次之间至少隔几年；帝国税最低让到几成、自治派系占到几成不再让、中央至少几城才封城给选帝侯
    private const int ConcessionGapYears = 10, ConcessionAutonomyCap = 40, ConcessionFiefMinCities = 4;
    private const float ConcessionTaxFloor = 0.1f;

    // 正在按选帝侯的推举立皇帝(OfficeSelector 选出、SetActor 即位的那一刻)：即位不扣正统，见 Empire.NewEmperor
    public static bool Electing;

    public static HolyRomanState State(Empire empire)
    {
        if (empire?.data == null) return null;
        return empire.data.holy_roman ??= new HolyRomanState();
    }

    public static bool IsElective(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        return regime?.type == RegimeType.Feudalism && regime.GetLeaderSelectMethod() == LeaderSelectMethod.Vote;
    }

    // 家族领地：中央(皇帝自家领国)的城占帝国城数的比例
    public static float HouseShare(Empire empire)
    {
        int total = empire?.countCities() ?? 0;
        return total <= 0 ? 1f : (empire.CoreKingdom?.cities?.Count ?? 0) / (float)total;
    }

    public static IEnumerable<(string key, int value)> LegitimacyItems(Empire empire)
    {
        if (IsElective(empire))
        {
            float share = HouseShare(empire);
            if (share < WeakHouse) yield return ("mandate_house_weak", -8);
            else if (share >= DominantHouse) yield return ("mandate_house_dominant", 8);
            else if (share >= StrongHouse) yield return ("mandate_house_strong", 5);
        }
        if (!HasDiet(empire)) yield break;
        HolyRomanState state = State(empire);
        if (ReformEnacted(empire)) yield return ("mandate_imperial_reform", ReformLegitimacy);
        if (state.sovereign_princes) yield return ("mandate_sovereign_princes", SovereignLegitimacy);
        if (RomzugUnderway(empire)) yield return ("mandate_romzug", RomzugLegitimacy);
    }

    // 家族领地薄弱时，比皇帝自家领国城多的诸侯不听调遣；皇帝远征在外时，比中央一半大的也不听
    public static bool Defies(Empire empire, Kingdom member)
    {
        if (member?.cities == null || empire?.CoreKingdom == null) return false;
        int core = empire.CoreKingdom.cities?.Count ?? 0;
        if (IsElective(empire) && HouseShare(empire) < WeakHouse && member.cities.Count > core) return true;
        return member.cities.Count * 2 > core && RomzugUnderway(empire);
    }

    public static Actor KingOfRomans(Empire empire)
    {
        HolyRomanState state = State(empire);
        Actor actor = state?.king_of_romans_id >= 0 ? World.world.units.get(state.king_of_romans_id) : null;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.kingdom?.GetEmpire() == empire ? actor : null;
    }

    // 帝位选出的人(OfficeObject.SetActor 调用)：是别的成员国的君主时，带着自家领国成为中央，不调进原中央
    public static bool TryElectHouse(Kingdom seat, Actor elected)
    {
        try
        {
            Empire empire = seat?.GetEmpire();
            if (empire == null || empire.CoreKingdom != seat || !IsElective(empire) || elected?.data == null ||
                elected.isRekt() || !elected.isKing()) return false;
            Kingdom house = elected.kingdom;
            if (house == null || house == seat || house.GetEmpire() != empire || house.king != elected || !house.hasCapital())
                return false;
            // 已经坐在中央的皇帝不再"当选"一次：同一次选举里 SetActor 会被再调几回，以前帝国重心在两三家之间来回搬
            if (seat.king == elected) return false;
            // 中央顶着帝国的国号，原家族领地按它的法理称呼；新家族的名字要在交出国号之前取
            string former = seat.GetMainTitle()?.name;
            if (string.IsNullOrWhiteSpace(former)) former = seat.GetKingdomName();
            string houseName = house.GetKingdomName();
            // 选帝换家族不改国号(帝国本身照旧，见文件头)
            if (!empire.TransferCoreTo(house, keepName: true)) return false;
            HolyRomanState state = State(empire);
            // 选举让步只在帝位真的换了一家(不同世系)时给，同一世系的分支连选不算
            bool newHouse = NewHouse(state, elected);
            Elected(empire, state, elected);
            if (newHouse) Concede(empire, state);
            Record(empire, string.Format(LM.Get("hre_election_history"), elected.getName(), houseName,
                house.capital?.GetCityName() ?? "", former));
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 选举换家族失败: {exception.Message}");
            return false;
        }
    }

    private static void Elected(Empire empire, HolyRomanState state, Actor emperor)
    {
        long clan = emperor.GetSpecificClan()?.id ?? -1L;
        state.house_terms = clan >= 0 && clan == state.house_clan_id ? state.house_terms + 1 : 1;
        state.house_clan_id = clan;
        state.last_election_at = World.world.getCurWorldTime();
    }

    // 每年(ConstitutionalEconomySystem 年度结算)：选帝制的储君；封建制帝国的成员构成、帝国改革、诸侯同盟、帝国议会
    public static void Yearly(Empire empire)
    {
        try
        {
            if (!IsElective(empire) && !HasDiet(empire)) return;
            HolyRomanState state = State(empire);
            if (IsElective(empire)) ElectiveYearly(empire, state);
            if (!HasDiet(empire)) return;
            ImperialPeaceYearly(empire, state);
            MembersYearly(empire, state);
            ReformYearly(empire, state);
            LeaguesYearly(empire, state);
            DietYearly(empire, state);
            Diagnose(empire, state);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 年度结算失败: {exception.Message}");
        }
    }

    // 储君：已即位的记一届；皇帝在世、有继承人时选立
    private static void ElectiveYearly(Empire empire, HolyRomanState state)
    {
        Actor emperor = empire.Emperor;
        if (emperor == null) return;
        // 储君已经即位
        if (state.king_of_romans_id == emperor.id)
        {
            state.king_of_romans_id = -1L;
            Elected(empire, state, emperor);
        }
        if (KingOfRomans(empire) != null) return;
        state.king_of_romans_id = -1L;
        // 先掷骰再挑人：挑人可能落成虚拟族人
        if (UnityEngine.Random.value >= KingOfRomansChance) return;
        Actor heir = KingOfRomansCandidate(empire);
        if (heir?.data == null || heir.isRekt() || !heir.isAlive()) return;
        long cost = Math.Min(Math.Max(0L, (long)empire.CoreKingdom.GetMoney()),
            (long)(StateDebtSystem.AnnualStableIncome(empire.CoreKingdom) * KingOfRomansCost));
        if (cost > 0) empire.CoreKingdom.SubMoney((int)cost, TreasuryCategory.Gift);
        state.king_of_romans_id = heir.id;
        Concede(empire, state);
        Record(empire, string.Format(LM.Get("hre_king_of_romans_history"), heir.getName(), emperor.getName()));
        heir.RecordPersonalHistory(LM.Get("personal_history_king_of_romans"));
    }

    // 储君人选：皇帝成年可继位、身在本帝国的子嗣，年长的先。选帝制下帝位不世袭，中央一直没有继承人
    // (EmpireCraftKingdomBehCheckHeir 只给世袭的国家立继承人)，以前按"有继承人"判，罗马人民的国王从来选不出来。
    // 无小人模式下皇子多是虚拟族人：实体里没有就按长幼落成一位(最多试两人，不合格的并回人口)
    public static Actor KingOfRomansCandidate(Empire empire)
    {
        bool Fit(Actor actor) => actor.isAdult() && actor.isUnitFitToRule() && actor.kingdom?.GetEmpire() == empire;
        Actor pick = EmpireCraftKingdomBehCheckHeir.HeirCandidates(empire.CoreKingdom).Where(Fit)
            .OrderByDescending(actor => actor.getAge()).FirstOrDefault();
        if (pick != null || !CityPopulationSystem.AbstractPopulationEnabled) return pick;
        PersonalClanIdentity parent = empire.Emperor?.GetPersonalIdentity();
        if (parent == null) return null;
        return VirtualGenealogySystem.RealizeFirst(SpecificClanManager.getChildren(parent).Select(item => item.Item2)
                .Where(identity => identity != null && identity.is_alive && identity.CanHeir())
                .OrderByDescending(identity => identity.age),
            actor => Fit(actor) && actor != empire.Emperor, 2);
    }

    private static bool NewHouse(HolyRomanState state, Actor elected)
    {
        SpecificClan previous = state.house_clan_id >= 0 ? SpecificClanManager.Get(state.house_clan_id) : null;
        return previous == null || !SpecificClanManager.SameLineage(previous, elected.GetSpecificClan());
    }

    // 选举让步：随机让出一项(能让的才让)。以前每次当选都让，选帝侯推举的多是老迈的诸侯，一两年一选，
    // 二十来年攒下七八条(玩家日志)。现在：换了一家(或选立储君)才让，十年内至多一次；
    // 各项有底线——帝国税最多让到一成、自治派系占到四成就不再让、中央剩不到三城不再封城
    private static void Concede(Empire empire, HolyRomanState state)
    {
        if (state.last_concession_at >= 0d && World.world.getCurWorldTime() >= state.last_concession_at &&
            Date.getYearsSince(state.last_concession_at) < ConcessionGapYears) return;
        var options = new List<Func<string>> { LowerTax, RestoreWarRight, Autonomy, GrantFief };
        while (options.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, options.Count);
            Func<string> option = options[index];
            options.RemoveAt(index);
            string text = option();
            if (string.IsNullOrEmpty(text)) continue;
            state.concessions ??= new List<string>();
            state.concessions.Add(text);
            if (state.concessions.Count > 20) state.concessions.RemoveAt(0);
            state.last_concession_at = World.world.getCurWorldTime();
            Record(empire, string.Format(LM.Get("hre_concession_history"), text));
            return;
        }

        // 以前最低让到 0.02，比帝国税下限(Empire.MinimumTaxRate)还低，读档时又被重置回两成
        string LowerTax()
        {
            if (empire.data.TaxRate <= ConcessionTaxFloor + 0.001f) return null;
            empire.data.TaxRate = Mathf.Max(ConcessionTaxFloor, empire.data.TaxRate - 0.02f);
            return LM.Get("hre_concession_tax");
        }

        string RestoreWarRight()
        {
            // 主教领的选帝侯本就不开战，不在此列；治安令施行期间记为豁免
            Kingdom member = ElectorRealms(empire).FirstOrDefault(kingdom => SecularPrince(kingdom) &&
                                                                             kingdom.GetRegime()?.IsAllowDiplomacy() == false);
            if (member == null) return null;
            member.GetRegime().SetAllowDiplomacy(true);
            Exempt(empire, member);
            return string.Format(LM.Get("hre_concession_war_right"), member.GetKingdomName());
        }

        string Autonomy()
        {
            FixedFaction faction = empire.CoreKingdom.GetRegime()?.GetPlayerFactions()
                ?.FirstOrDefault(item => item.Type == FactionType.自治);
            if (faction == null || empire.CoreKingdom.GetFactionRatioValue(faction) >= ConcessionAutonomyCap ||
                !empire.CoreKingdom.TryIncreaseFactionRatio(faction, 5)) return null;
            return LM.Get("hre_concession_autonomy");
        }

        string GrantFief()
        {
            Kingdom core = empire.CoreKingdom;
            City city = core.cities?.Where(item => item != null && !item.isRekt() && item != core.capital)
                .OrderBy(item => item.zones?.Count ?? 0).FirstOrDefault();
            Kingdom elector = ElectorRealms(empire).Where(SecularPrince).OrderByDescending(kingdom => kingdom.cities.Count)
                .FirstOrDefault();
            if (city == null || elector == null || core.cities.Count < ConcessionFiefMinCities) return null;
            city.joinAnotherKingdom(elector);
            return string.Format(LM.Get("hre_concession_fief"), city.GetCityName(), elector.GetKingdomName());
        }
    }

    #region 帝国治安令

    // 帝国治安令(诉求「颁布帝国治安令」)：施行期间世俗诸侯没有外交开战权、须向中央出兵；自由城市、主教领本就不开战、
    // 不给中央出兵，不在其内。以前诉求只看"还有没有诸侯能开战或不出兵"：新封的、分割出来的、换家族后降为诸侯的旧中央，
    // 连同本就不出兵的自由城市，一出现就又能提，同一帝国几年里颁布三次(玩家日志 2026-10-20)。现在：
    //   · 施行期间新入帝国、新立的世俗诸侯每年一查，照令办理；
    //   · 以诸侯特权(确认诸侯特权、允许诸侯自由开战)或选举让步取回开战权的记为豁免——从未施行、或有了豁免(被部分撤销)
    //     才能再提，颁布后十五年内不再提(见 TempFac_颁布帝国治安令)；
    //   · 和约之后诸侯取得完全主权，治安令作废，也不再能提。
    public static bool SecularPrince(Kingdom kingdom) => kingdom?.data != null && Estate(kingdom) == EstateNone;

    public static bool ImperialPeaceInForce(Empire empire) => State(empire)?.imperial_peace_at >= 0d;

    // 可以再提：未施行，或已被特权、让步部分撤销；和约之后不再有
    public static bool ImperialPeaceOpen(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state != null && !state.sovereign_princes && (state.imperial_peace_at < 0d || state.privileged_ids?.Count > 0);
    }

    // 颁布(诉求执行时调用)：诸侯一律受令，豁免清零
    public static void EnactImperialPeace(Empire empire, IEnumerable<Kingdom> princes)
    {
        HolyRomanState state = State(empire);
        if (state == null || World.world == null) return;
        foreach (Kingdom prince in princes.ToList()) Bind(prince);
        state.imperial_peace_at = World.world.getCurWorldTime();
        state.privileged_ids?.Clear();
    }

    // 某诸侯取回外交开战权(诸侯特权、选举让步)：施行期间记为豁免
    public static void Exempt(Empire empire, Kingdom prince)
    {
        HolyRomanState state = State(empire);
        if (state == null || prince?.data == null || state.imperial_peace_at < 0d) return;
        state.privileged_ids ??= new List<long>();
        if (!state.privileged_ids.Contains(prince.id)) state.privileged_ids.Add(prince.id);
    }

    private static void Bind(Kingdom prince)
    {
        Regime regime = prince?.GetRegime();
        if (regime == null) return;
        regime.SetAllowDiplomacy(false);
        regime.SetAllowSupportCenterArmy(true);
    }

    // 每年：和约后作废；施行期间新来的世俗诸侯照令办理，豁免名单清掉已不在帝国的
    private static void ImperialPeaceYearly(Empire empire, HolyRomanState state)
    {
        if (state.imperial_peace_at < 0d) return;
        state.privileged_ids ??= new List<long>();
        if (state.sovereign_princes)
        {
            state.imperial_peace_at = -1d;
            state.privileged_ids.Clear();
            return;
        }
        state.privileged_ids.RemoveAll(id => World.world.kingdoms.get(id)?.GetEmpire() != empire);
        foreach (Kingdom member in Members(empire))
        {
            if (!SecularPrince(member) || !member.hasKing() || state.privileged_ids.Contains(member.id) ||
                member.IsFactionRebelling() || member.IsLocalRebelling()) continue;
            Regime regime = member.GetRegime();
            if (regime == null || !regime.IsAllowDiplomacy() && regime.IsAllowSupportCenterArmy()) continue;
            Bind(member);
        }
    }

    #endregion

    // 选帝侯所在的成员国(不含中央、被禁者)
    public static List<Kingdom> ElectorRealms(Empire empire) =>
        empire.GetCabinetMembers().Where(actor => actor?.kingdom != null && !actor.isRekt())
            .Select(actor => actor.kingdom)
            .Where(kingdom => kingdom != empire.CoreKingdom && kingdom.GetEmpire() == empire && !kingdom.isRekt() &&
                              !IsBanned(kingdom))
            .Distinct().ToList();

    #region 诊断

    // 每年一行(随年度结算，每个封建制帝国一行)：各项现状、卡在哪一条，进游戏看 Player.log 的 [EmpireCraft][神罗诊断]
    private static void Diagnose(Empire empire, HolyRomanState state)
    {
        try
        {
            Kingdom core = empire.CoreKingdom;
            List<Kingdom> members = Members(empire);
            var parts = new List<string>
            {
                $"成员{members.Count}国、共{empire.countCities()}城，中央{core.cities?.Count ?? 0}城(家族领地{HouseShare(empire):P0})，" +
                (IsElective(empire) ? "选帝制" : "世袭"),
                "治安令：" + (state.sovereign_princes ? "和约后作废" : state.imperial_peace_at >= 0d
                    ? $"施行中，豁免{state.privileged_ids?.Count ?? 0}家" : "未施行"),
                $"选举让步：累计{state.concessions?.Count ?? 0}条" +
                (state.last_concession_at >= 0d ? $"，距上次{Date.getYearsSince(state.last_concession_at)}年" : "")
            };
            if (IsElective(empire)) parts.Add("储君：" + KingOfRomansStatus(empire));
            List<Kingdom> free = members.Where(IsFreeCity).ToList();
            parts.Add($"自由城市{free.Count}座" + (free.Count == 0 ? "" : "(" + string.Join("、", free.Take(4).Select(city =>
                $"{city.GetKingdomName()} 年收{StateDebtSystem.AnnualStableIncome(city)}/债{StateDebtSystem.Outstanding(city)}")) + ")"));
            parts.Add($"主教领{members.Count(IsChurchEstate)}家：" + (ChurchBlocker(empire) ?? "条件已齐，每年一成可能"));
            parts.Add("帝国改革：" + ReformStatus(empire, state));
            PartibleTally.TryGetValue(empire.data.id, out var partible);
            PartibleTally.Remove(empire.data.id);
            parts.Add("分割继承：" + (partible.vacant == 0 ? "本年无诸侯出缺"
                : $"本年诸侯出缺{partible.vacant}次、掷中{partible.rolled}次" +
                  (partible.blocked != null ? $"，最近没分：{partible.blocked}" : "")));
            parts.Add("诸侯同盟：" + LeagueStatus(empire, state));
            parts.Add("加冕远征：" + RomzugStatus(empire));
            LogService.LogInfo($"[EmpireCraft][神罗诊断] {empire.GetEmpireFullName()}：{string.Join("；", parts)}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗诊断] 失败: {exception.Message}");
        }
    }

    private static string KingOfRomansStatus(Empire empire)
    {
        if (KingOfRomans(empire) is Actor chosen) return $"已选立{chosen.getName()}";
        Actor emperor = empire.Emperor;
        if (emperor == null) return "帝位空缺";
        int living = EmpireCraftKingdomBehCheckHeir.HeirCandidates(empire.CoreKingdom).Count(actor => actor.isAdult());
        PersonalClanIdentity parent = emperor.GetPersonalIdentity();
        int all = parent == null ? 0 : SpecificClanManager.getChildren(parent).Count(item =>
            item.Item2 != null && item.Item2.is_alive && item.Item2.CanHeir() && item.Item2.age >= 18);
        return Math.Max(living, all) == 0 ? "皇帝没有成年可继位的子嗣" : $"成年子嗣{Math.Max(living, all)}人，每年两成可能";
    }

    private static string ReformStatus(Empire empire, HolyRomanState state)
    {
        if (ReformEnacted(empire)) return $"已施行，{CircleCount(empire)}个大区，帝国法院判过{state.court_rulings}次";
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(NodeImperialReform);
        if (node == null) return "制度树里没有这个节点";
        if (InstitutionSystem.CanStartReform(empire, node, out string reason))
            return "条件已齐，待 AI 选中(别的改革在推时要排队)";
        string text = LM.Get(reason);
        if (reason == "institution_reform_missing_tech" &&
            !TechnologySystem.AreRealmInstitutionTechsMet(empire.CoreKingdom, node, out List<string> missing))
            text += "：" + string.Join("、", missing);
        return reason == "institution_reform_low_mandate" ? $"{text}(正统{empire.Legitimacy}，要{node.research.mandate_required})" : text;
    }

    private static string LeagueStatus(Empire empire, HolyRomanState state)
    {
        if (state.sovereign_princes) return $"已订{state.peace_name}，诸侯完全主权";
        if (LeagueWar(empire) != null) return $"{state.league_name}宗教战争进行中";
        if (state.league_leader_id >= 0) return $"{state.league_name}已结成({LeagueMembers(empire).Count}家)，每年一成二可能开战";
        Kingdom core = empire.CoreKingdom;
        if (!core.hasReligion()) return "中央没有国教";
        bool reformation = InstitutionSystem.IsEnacted(empire, PapacySystem.NodeReformation);
        if (ChurchesOf(core.religion).Count == 0)
            return !reformation ? "本文化未施行宗教改革，也没有从皇帝所奉宗教分出的新教会"
                : KeepsOldFaith(empire) ? "待诸侯另立新教会(每年一成五)" : "非选帝制，宗教改革由皇帝主导、整个帝国改宗";
        List<Kingdom> reformed = Members(empire).Where(member => member.hasReligion() && Schismatic(member.religion, core.religion)).ToList();
        int total = Math.Max(1, empire.countCities());
        return $"改奉新教会{reformed.Count}家、占{reformed.Sum(member => member.cities.Count) / (float)total:P0}城(要两家、两成)";
    }

    private static string RomzugStatus(Empire empire)
    {
        if (RomzugUnderway(empire)) return "远征途中";
        if (!PapacySystem.Applies(empire)) return "无教廷可言(非西方、非君主制或所奉宗教没有圣地)";
        if (empire.Emperor == null) return "帝位空缺";
        if (PapacySystem.IsCrowned(empire)) return "已加冕";
        Kingdom papacy = PapacySystem.Papacy(PapacySystem.ReligionOf(empire)), core = empire.CoreKingdom;
        if (papacy == null || PapacySystem.BrokeWithRome(empire)) return "教廷无主或已脱离罗马，自行加冕";
        if (papacy == core) return "教廷就在皇帝自家领内，直接加冕";
        if (PapacySystem.IsExcommunicated(empire)) return "绝罚中";
        if (papacy.isInWarWith(core)) return $"与教廷{papacy.GetKingdomName()}交战";
        return $"未加冕，每年四成可能远征{papacy.GetKingdomName()}";
    }

    #endregion

    private static void Record(Empire empire, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        empire.RecordHistory(directContent: text, actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom?.id ?? -1L);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
}
