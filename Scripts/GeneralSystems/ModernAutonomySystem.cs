using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 近代国家成员的自主度(用户 2026-11-26 "军阀和地方政权地方武装和政府的判定怎么样才会更加的合理""先做自主度分档")。
// 以前近代国家的成员只有两档：在国家里(随征、缴税)或者独立。名义上奉中央、实际自己管兵管钱、调兵时可以抗命、
// 会通电反中央或者投敌——民国地方实力派的常态——表达不出来。现在每个成员记一档：
//   · 直辖(0)：没有自己的兵，赋税照近代国家的分成缴中央；
//   · 地方实力派(1)：自有军队、首领由军中推举，只缴中央两成，照旧听调随征；
//   · 割据(2)：不奉号令、不缴税、不随征，中央也不替它付饷(复用分裂进程的割据效果，见 SchismSystem.IsBreakaway)；
//   · 再往上就离开国家——通电反中央、举兵独立，或者中央正和外国打仗时投向那个外国、成为它的傀儡。
// 每年看一次离心(0~100)，连着两年过线才挪一档，一次只挪一档：
//   离心 = 兵权(本省兵力占本省与中央合计的份额 × 40) + 中央正统不足(正统 50 以下每差 1 点 +0.4，至多 20)
//        + 中央欠饷亏空 15 + 中央在对外的仗里丢了城 15 + 鞭长莫及((1 − 政令) × 15) + 异文化 10 + 理念与中央政府相去甚远 10
//        − 外敌当前而中央守住了(同仇敌忾) 10；
//   往上挪：直辖 → 实力派要 ≥ 50 且本省至少 3 城(拥兵坐大)；实力派 → 割据要 ≥ 28；割据 → 离开要 ≥ 60，且没在开国宽限里、没关自立；
//   ≤ 20 往下挪：割据满三年 → 实力派(复奉号令)；实力派 → 直辖是整编：外敌没打上门、兵力至少是本省两倍、国库出得起整编费
//        (本省每城 50)，六成收编成功(本省的军团交给中央)，四成激起兵变、径直割据。
// 只管近代国家(核心是现代政体)的成员；傀儡、属地、流亡政府不是成员，不在此列。分裂进程进行中的帝国不动(两套割据不叠)。
// 旧档：头一回结算按现状定档(剧本的地方实力派、有兵的州算实力派，其余算直辖)
public static class ModernAutonomySystem
{
    public const int Direct = 0, Strongman = 1, Defiant = 2;
    // 实力派缴中央的成数(近代国家照常六成，联邦制三成五，见 TreasurySystem.TaxShares)
    public const int StrongmanCentralPercent = 20;

    // 门槛(用户 2026-11-27 "把门槛调低一点，让割据和整编常见些"：以前坐大、割据都是 55，离开 70，整编线 25，
    // 1937 剧本里民国各派离心 15~37，三十年一档没挪)
    private const float RiseAt = 50f, DefyAt = 28f, LeaveAt = 60f, DownAt = 20f;
    // 割据了至少这么多年才复奉号令(兵变之后第二年就复奉，等于没闹)
    private const int DefyMinYears = 3;
    private const int ConfirmYears = 2;
    private const int RiseMinCities = 3;
    private const float ReorgArmyRatio = 2f, ReorgSuccess = 0.6f;
    // 够了条件的实力派每年只有这一成会被拿来整编：不然仗一停，各派同一年一齐被整编、一齐兵变
    private const float ReorgChance = 0.35f;
    private const int ReorgCostPerCity = 50;

    // ───────── 查询 ─────────

    private static bool InModernState(Kingdom kingdom, out Empire empire)
    {
        empire = kingdom?.data == null || kingdom.isRekt() ? null : kingdom.GetEmpire();
        return empire != null && kingdom != empire.CoreKingdom && ModernTitleCentralization.IsModernState(empire);
    }

    // 这一成员眼下的档(不在近代国家里的、还没定档的按直辖)
    public static int Tier(Kingdom kingdom)
    {
        if (!InModernState(kingdom, out _)) return Direct;
        return Mathf.Clamp(kingdom.GetOrCreate().autonomy_tier, Direct, Defiant);
    }

    // 割据(见 SchismSystem.IsBreakaway：不缴税、不随征、不奉诏都经那里)
    public static bool IsDefiant(Kingdom kingdom) => Tier(kingdom) == Defiant;

    public static bool IsStrongman(Kingdom kingdom) => Tier(kingdom) == Strongman;

    // 派系军阀：近代国家里有自己兵马的成员(实力派、割据；还没定档的看剧本的地方实力派与有兵的州)
    public static bool IsClique(Kingdom kingdom)
    {
        if (!InModernState(kingdom, out _)) return false;
        KingdomExtension.KingdomExtraData extra = kingdom.GetOrCreate();
        return extra.autonomy_tier >= Strongman ||
               extra.autonomy_tier < 0 && (extra.scenario_autonomy || kingdom.GetKingdomType() == KingdomType.Modern_state);
    }

    // 派系军阀的接班人(见 TryHandleVacancy)：军阀是个人和家族的派系，不是谁兵多谁上——
    // 先看本家(子嗣、兄弟、族人，见 VirtualGenealogySystem.RealizeHeir)，没有成年合格的再由统兵最多的部将接手，再没有就认一位族中远支；
    // 都没有返回 null，照常推举(以前一换人就从当地百姓里随便挑一位，青马、新疆的首领换成了外族人，派系就此没了)
    public static Actor CliqueSuccessor(Kingdom kingdom, OfficeObject office)
    {
        if (!IsClique(kingdom)) return null;
        bool Fit(Actor actor) => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && actor.isUnitFitToRule() &&
                                 !actor.isKing() && actor.CanServeOffice(kingdom) && office.Accepts(actor);
        Actor heir = VirtualGenealogySystem.RealizeHeir(kingdom);
        if (Fit(heir) && heir.kingdom == kingdom) return heir;
        foreach (CorpsData corps in CorpsSystem.Of(kingdom).OrderByDescending(corps => corps?.strength ?? 0f))
        {
            Actor general = corps == null || corps.general_id < 0 ? null : World.world.units.get(corps.general_id);
            if (Fit(general) && general.kingdom == kingdom) return general;
        }
        // 本家没有成年的、部将里也没人：从都城人口里认一位族中远支，改用本家的姓(见 VirtualGenealogySystem.SpawnCadet)，派系不就此易姓
        SpecificClan clan = SpecificClanManager.getPerson(kingdom.GetOrCreate().last_ruler_identity_id)?._specificClan;
        if (clan != null && CityPopulationSystem.AbstractPopulationEnabled)
        {
            Actor cadet = VirtualGenealogySystem.SpawnCadet(kingdom, clan);
            if (Fit(cadet)) return cadet;
        }
        return null;
    }

    // 国君出缺(见 EmpireCraftKingdomBehCheckKing)：派系军阀由本家或部将接班；接上了返回 true
    public static bool TryHandleVacancy(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.hasKing() || !IsClique(kingdom)) return false;
        OfficeObject office = kingdom.GetOffice();
        Actor successor = office == null ? null : CliqueSuccessor(kingdom, office);
        if (successor == null) return false;
        office.is_local = true;
        office.meta_object = kingdom;
        office.SetActor(successor);
        return kingdom.hasKing();
    }

    // 提示框里的一行：实力派、割据才显示
    public static string StatusText(Kingdom kingdom)
    {
        int tier = Tier(kingdom);
        if (tier == Direct) return null;
        return string.Format(LM.Get("modern_autonomy_status_line"), LM.Get("modern_autonomy_tier_" + tier),
            Mathf.RoundToInt(kingdom.GetOrCreate().autonomy_score));
    }

    // ───────── 年结(见 ConstitutionalEconomySystem.Update) ─────────

    public static void Yearly(Empire empire)
    {
        if (!ModernTitleCentralization.IsModernState(empire) || World.world == null) return;
        Kingdom core = empire.CoreKingdom;
        if (core?.data == null || core.isRekt()) return;
        // 分裂进程里另有割据(见 SchismSystem)，两套不叠
        if (SchismSystem.Active(empire)) return;
        List<Kingdom> members = empire.kingdoms_list.Where(member => member?.data != null && !member.isRekt() && member != core &&
                                                                   member.hasKing() && !member.IsFactionRebelling() &&
                                                                   !member.IsLocalRebelling()).ToList();
        if (members.Count == 0) return;
        Context context = Read(empire, core);
        var lines = new List<string>();
        foreach (Kingdom member in members)
        {
            try
            {
                Step(empire, core, member, context);
                int tier = Tier(member);
                if (tier != Direct || member.GetOrCreate().autonomy_score >= RiseAt)
                    lines.Add($"{member.GetKingdomName()} {tier}档 离心{Mathf.RoundToInt(member.GetOrCreate().autonomy_score)}");
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][自主度] {member.GetKingdomName()} 结算失败: {exception.Message}");
            }
        }
        if (lines.Count > 0) LogService.LogInfo($"[EmpireCraft][自主度] {empire.GetEmpireName()}：" + string.Join("；", lines));
    }

    // 中央这一年的处境(各成员共用)
    private sealed class Context
    {
        public float army;
        public int legitimacy;
        public bool bankrupt, lostForeign, defendingForeign, foreignWar, invaded;
        public string culture;
        public PartyIdeology ideology;
    }

    private static float Army(Kingdom kingdom) =>
        CorpsSystem.Of(kingdom)?.Sum(corps => Mathf.Max(0f, corps?.strength ?? 0f)) ?? 0f;

    private static IEnumerable<War> ForeignWars(Empire empire, Kingdom core) => core.getWars().Where(war =>
        war?.data != null && !war.hasEnded() && war.getAsset() != WarTypeLibrary.rebellion &&
        war.GetEmpireWarType() != EmpireWarType.地方独立 &&
        (war.getMainAttacker()?.GetEmpire() != empire || war.getMainDefender()?.GetEmpire() != empire));

    private static Context Read(Empire empire, Kingdom core)
    {
        var context = new Context
        {
            army = Army(core), legitimacy = empire.Legitimacy, bankrupt = EmpireBankruptcySystem.IsBankrupt(empire),
            culture = InstitutionSystem.GetPrimaryCulture(empire), ideology = WarlordEraSystem.GovernmentIdeology(empire)
        };
        foreach (War war in ForeignWars(empire, core))
        {
            context.foreignWar = true;
            // 外敌打上门(中央是守方)；丢了城另记(剧本开局就在打的仗没记开战时的城数，丢没丢看不出来)
            bool defending = war.getMainDefender() == core;
            if (defending) context.defendingForeign = true;
            if (WarSituation.CitiesLost(war, core) > 0)
            {
                context.lostForeign = true;
                if (defending) context.invaded = true;
            }
        }
        return context;
    }

    private static float Score(Empire empire, Kingdom member, Context context)
    {
        float own = Army(member);
        float score = 40f * own / Mathf.Max(1f, own + context.army);
        score += Mathf.Clamp((50f - context.legitimacy) * 0.4f, 0f, 20f);
        if (context.bankrupt) score += 15f;
        if (context.lostForeign) score += 15f;
        if (member.capital != null) score += (1f - Mathf.Clamp01(AdministrativeControlSystem.Get(member.capital))) * 15f;
        string culture = CultureService.GetRealmCulture(member);
        if (!string.IsNullOrEmpty(culture) && !string.IsNullOrEmpty(context.culture) && culture != context.culture) score += 10f;
        if (PartySystem.IdeologyDistance(IdeologyFamilies.StateIdeology(member), context.ideology) >= 60f) score += 10f;
        if (context.defendingForeign && !context.lostForeign) score -= 10f;
        return Mathf.Clamp(score, 0f, 100f);
    }

    private static void Step(Empire empire, Kingdom core, Kingdom member, Context context)
    {
        KingdomExtension.KingdomExtraData extra = member.GetOrCreate();
        // 旧档、新成员：按现状定档
        if (extra.autonomy_tier < 0)
        {
            extra.autonomy_tier = extra.scenario_autonomy || member.GetKingdomType() == KingdomType.Modern_state ? Strongman : Direct;
            extra.autonomy_years = 0;
        }
        float score = Score(empire, member, context);
        extra.autonomy_score = score;
        int tier = Mathf.Clamp(extra.autonomy_tier, Direct, Defiant);
        float upAt = tier == Direct ? RiseAt : tier == Strongman ? DefyAt : LeaveAt;
        bool up = score >= upAt && (tier != Direct || (member.cities?.Count ?? 0) >= RiseMinCities);
        bool down = tier > Direct && score <= DownAt &&
                    (tier != Defiant || extra.autonomy_since < 0d || Date.getYearsSince(extra.autonomy_since) >= DefyMinYears);
        // 连着两年过线才挪一档：往上记正数、往下记负数，中间回落就清零
        extra.autonomy_years = up ? Math.Max(0, extra.autonomy_years) + 1 : down ? Math.Min(0, extra.autonomy_years) - 1 : 0;
        if (extra.autonomy_years >= ConfirmYears)
        {
            extra.autonomy_years = 0;
            if (tier == Direct) Rise(empire, member);
            else if (tier == Strongman) Defy(empire, member);
            else Leave(empire, core, member, context);
        }
        else if (extra.autonomy_years <= -ConfirmYears)
        {
            extra.autonomy_years = 0;
            if (tier == Defiant) Obey(empire, member);
            else Reorganize(empire, core, member, context);
        }
    }

    // ───────── 挪档 ─────────

    private static void Set(Kingdom member, int tier)
    {
        KingdomExtension.KingdomExtraData extra = member.GetOrCreate();
        extra.autonomy_tier = tier;
        extra.autonomy_since = World.world.getCurWorldTime();
    }

    // 直辖 → 实力派：拥兵坐大(同剧本的地方实力派，见 ScenarioModernPolitics.MakeWarlordMember)
    private static void Rise(Empire empire, Kingdom member)
    {
        Regime regime = member.GetRegime();
        if (regime == null) return;
        regime.SetAllowArmy(true);
        regime.SetLeaderSelectMethod(LeaderSelectMethod.Army);
        TreasurySystem.EnsureCollectsTax(regime);
        member.GetOrCreate().scenario_autonomy = true;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
        Set(member, Strongman);
        Record(empire, member, "modern_autonomy_rise_history");
    }

    // 实力派 → 割据
    private static void Defy(Empire empire, Kingdom member)
    {
        Set(member, Defiant);
        Record(empire, member, "modern_autonomy_defy_history");
    }

    // 割据 → 实力派
    private static void Obey(Empire empire, Kingdom member)
    {
        Set(member, Strongman);
        Record(empire, member, "modern_autonomy_obey_history");
    }

    // 实力派 → 直辖：整编。外敌打上门的时候不整编，本省自己挑头打着仗的也不动；
    // 兵力不到本省两倍、国库出不起的不动手(以前打着任何外战都不整编，中央随征的仗几乎年年有，三十年一次也没整编过)
    private static void Reorganize(Empire empire, Kingdom core, Kingdom member, Context context)
    {
        if (context.defendingForeign || UnityEngine.Random.value >= ReorgChance) return;
        if (member.getWars().Any(war => war.getMainAttacker() == member || war.getMainDefender() == member)) return;
        float own = Army(member);
        if (context.army < own * ReorgArmyRatio) return;
        int cost = ReorgCostPerCity * Math.Max(1, member.cities?.Count ?? 1);
        // 整编费直接从国库出，不看可支配余款：可支配余款要先留安全储备，民国国库两万多照样一文不剩，六十回整编一回也没出得起钱
        if (!TreasurySystem.TrySpend(core, cost, TreasuryCategory.Military, discretionary: false)) return;
        if (UnityEngine.Random.value >= ReorgSuccess)
        {
            Set(member, Defiant);
            Record(empire, member, "modern_autonomy_mutiny_history");
            return;
        }
        foreach (CorpsData corps in CorpsSystem.Of(member).ToList()) CorpsSystem.Transfer(corps, member, core, member.capital ?? core.capital);
        Regime regime = member.GetRegime();
        if (regime != null)
        {
            regime.SetAllowArmy(false);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Default);
            regime.SetAllowSupportCenterArmy(true);
        }
        member.GetOrCreate().scenario_autonomy = false;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
        Set(member, Direct);
        Record(empire, member, "modern_autonomy_reorg_history");
    }

    // 割据 → 离开：中央正和外国打仗、那个外国比中央强又挨着本省的，投向它当傀儡；不然通电反中央、举兵独立
    private static void Leave(Empire empire, Kingdom core, Kingdom member, Context context)
    {
        if (UnificationSystem.InGrace(core) || SecessionGate.Forbids("地方独立", empire.GetEmpireName())) return;
        Kingdom patron = null;
        double ownPower = empire.GetNationalPower();
        foreach (War war in ForeignWars(empire, core))
        {
            Kingdom enemy = war.getMainAttacker() == core || war.getMainAttacker()?.GetEmpire() == empire
                ? war.getMainDefender() : war.getMainAttacker();
            if (enemy?.data == null || enemy.isRekt() || enemy.GetEmpire() == empire || PuppetStateSystem.IsPuppet(enemy)) continue;
            double enemyPower = enemy.GetEmpire()?.GetNationalPower() ?? enemy.GetNationalPower();
            if (enemyPower <= ownPower || !member.IsNeighbourWith(enemy)) continue;
            patron = enemy;
            break;
        }
        string name = member.GetKingdomFullName();
        empire.leave(member);
        member.GetOrCreate().autonomy_tier = -1;
        if (patron != null && PuppetStateSystem.Establish(patron, member, record: false) == null)
        {
            EventRecorder.Record(empire, actor: member.king, logKingdom: member, text: string.Format(LM.Get("modern_autonomy_defect_history"),
                name, empire.GetEmpireFullName(), patron.GetKingdomFullName()));
            return;
        }
        War revolt = DiplomacyHelpers.wars.newWar(member, core, WarTypeLibrary.normal);
        revolt?.SetEmpireWarType(EmpireWarType.地方独立);
        EventRecorder.Record(empire, actor: member.king, logKingdom: member, text: string.Format(LM.Get("modern_autonomy_revolt_history"),
            name, empire.GetEmpireFullName()));
    }

    private static void Record(Empire empire, Kingdom member, string key) =>
        EventRecorder.Record(empire, actor: member.king, logKingdom: member,
            text: string.Format(LM.Get(key), member.GetKingdomFullName(), empire.GetEmpireFullName()));
}
