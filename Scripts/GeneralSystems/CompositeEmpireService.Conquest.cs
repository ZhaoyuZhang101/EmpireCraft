using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 征服王朝的反复(用户 2026-10-19 问答："游牧南下建立汉化政权有没有更好的方案"，本俗反扑、北归都选"要")：
//   · 本俗反扑(国语骑射，见 TempFac_国语骑射)：统治族群游牧、尚未融完的征服王朝，融合度到 50 以上或已走上从俗之路时，
//     攘夷、同化派可请重申国语骑射——融合度 −10，部族正统 +10、属地正统 −10，二十年内不从俗(从俗的退回兼用)；三十年一次；
//   · 旧部起兵：融合完成(含易俗改制一步到位)时，以统治族群旧地为主的成员各有一半机会不服改俗，据地起兵独立(至多两国)；
//   · 北归：尚未融完、不受天命、属地法理只控制着不到四分之一、治下还有本族旧地的城——退归旧地：都城迁回本族旧地最大的城，
//     朝廷复行旧制(军事传统的政体)，国号加方位(新都在旧都哪边就称哪边)，以属地制度治理的成员各自脱离，复合帝国就此作罢；
//     日后再打下属地、条件够了还可以再采纳。采纳后十年内不北归。
//     离草(见 ShuiTuSystem)到第二档以上的，属地还没丢也每年有 RetreatHomesickChance 的机会北归，只回旱地上的本族旧地，离草从头计。
public static partial class CompositeEmpireService
{
    private const int NativistYears = 20, NativistIntegration = 50, NativistLoss = 10, NativistLegitimacy = 10;
    private const float OldGuardChance = 0.5f;
    private const int OldGuardMax = 2;
    private const float RetreatControlShare = 0.25f, RetreatHomesickChance = 0.3f;
    private const int RetreatGraceYears = 10;

    #region 本俗反扑

    public static bool NativistActive(Empire empire) =>
        empire?.data != null && empire.data.nativist_revival_at >= 0d && World.world != null &&
        empire.data.nativist_revival_at <= World.world.getCurWorldTime() &&
        Date.getYearsSince(empire.data.nativist_revival_at) < NativistYears;

    // 统治族群游牧、尚未融完的征服王朝：攘夷、同化派挂着国语骑射这项诉求
    public static bool NativistClaimApplies(Empire empire)
    {
        if (!IsComposite(empire)) return false;
        string ruling = GetRulingCulture(empire);
        return IsNomadicCulture(ruling) && !string.Equals(ruling, GetInstitutionalCulture(empire), StringComparison.Ordinal);
    }

    public static bool CanProclaimNativism(Empire empire) =>
        NativistClaimApplies(empire) && !NativistActive(empire) &&
        (empire.data.composite_integration >= NativistIntegration || GetRoute(empire) == RouteAdopt);

    // 紧迫度：融得越深越急，已在从俗更急
    public static float NativismUrgency(Empire empire) => !CanProclaimNativism(empire) ? 0f
        : Math.Max(0f, (empire.data.composite_integration - 40) * 0.6f) + (GetRoute(empire) == RouteAdopt ? 10f : 0f);

    public static string ProclaimNativism(Empire empire)
    {
        if (!CanProclaimNativism(empire) || World.world == null) return null;
        string ruling = GetRulingCulture(empire), institutional = GetInstitutionalCulture(empire);
        CompositeEmpireIntegrationStage oldStage = empire.data.composite_integration_stage;
        empire.data.composite_integration = Math.Max(1, empire.data.composite_integration - NativistLoss);
        empire.data.composite_integration_stage = CompositeEmpireRules.GetStage(empire.data.composite_integration);
        empire.data.ruling_tradition_legitimacy = Clamp100(empire.data.ruling_tradition_legitimacy + NativistLegitimacy);
        empire.data.central_plains_legitimacy = Clamp100(empire.data.central_plains_legitimacy - NativistLegitimacy);
        empire.data.nativist_revival_at = World.world.getCurWorldTime();
        if (empire.data.composite_route == RouteAdopt)
        {
            empire.data.composite_route = RouteJoint;
            empire.data.composite_route_since = empire.data.nativist_revival_at;
        }
        if (oldStage != empire.data.composite_integration_stage)
            TranslateHelper.LogCompositeEmpireStageChanged(empire, oldStage, empire.data.composite_integration_stage);
        LogService.LogInfo($"[EmpireCraft][征服王朝] {empire.GetEmpireName()} 重申国语骑射，融合度 {empire.data.composite_integration}");
        return string.Format(LM.Get("claim_outcome_国语骑射_done"), ruling.GetCultureTranslate(), institutional.GetCultureTranslate());
    }

    #endregion

    #region 旧部起兵

    // 融合完成：以旧统治族群文化为主的成员各有一半机会据地起兵(至多两国)
    private static void OldGuardRevolt(Empire empire, string oldRuling, string adopted)
    {
        if (!IsNomadicCulture(oldRuling) || empire.CoreKingdom == null) return;
        Kingdom core = empire.CoreKingdom;
        int risen = 0;
        foreach (Kingdom member in (empire.kingdoms_list ?? new List<Kingdom>()).ToList())
        {
            if (risen >= OldGuardMax) break;
            if (member?.data == null || member.isRekt() || member == core || !member.hasKing() ||
                member.cities == null || member.cities.Count == 0 ||
                !string.Equals(ResolveRegionalInstitutionCulture(member), oldRuling, StringComparison.Ordinal) ||
                UnityEngine.Random.value >= OldGuardChance) continue;
            string name = member.GetKingdomFullName();
            empire.leave(member);
            War war = DiplomacyHelpers.wars.newWar(member, core, WarTypeLibrary.normal);
            war?.SetEmpireWarType(EmpireCraft.Scripts.Enums.EmpireWarType.地方独立);
            EventRecorder.Record(empire, actor: member.king, logKingdom: member, text: string.Format(
                LM.Get("composite_old_guard_history"), oldRuling.GetCultureTranslate(), empire.GetEmpireName(),
                adopted.GetCultureTranslate(), name));
            risen++;
        }
    }

    #endregion

    #region 北归

    private static bool TryNorthernRetreat(Empire empire, string ruling, string institutional, float controlledShare)
    {
        Kingdom core = empire.CoreKingdom;
        if (core?.capital == null || World.world == null) return false;
        // 属地丢得差不多了；或离草到第二档以上(部众思归，每年三成机会)
        bool lost = controlledShare < RetreatControlShare;
        bool homesick = !lost && ShuiTuSystem.OffGrassTier(core) >= ShuiTuSystem.RetreatTier &&
                        UnityEngine.Random.value < RetreatHomesickChance;
        if ((!lost && !homesick) ||
            TianxiaSystem.MandateOf(core) != null || !IsNomadicCulture(ruling) || !CultureService.IsValidCulture(institutional) ||
            empire.data.composite_identity_adopted_timestamp >= 0d &&
            Date.getYearsSince(empire.data.composite_identity_adopted_timestamp) < RetreatGraceYears ||
            !Enum.TryParse(empire.data.military_tradition, true, out RegimeType tradition) ||
            RegimeManager.regimes == null || !RegimeManager.regimes.ContainsKey(tradition)) return false;
        City seat = HomelandSeat(empire, ruling, homesick);
        if (seat == null) return false;

        string before = empire.GetEmpireFullName();
        City oldSeat = EmpireCoreManager.EmpireCores.Values
            .FirstOrDefault(other => other != null && string.Equals(other.default_culture, institutional, StringComparison.Ordinal))
            ?.GetCoreCapital() ?? core.capital;
        if (seat.kingdom != core) seat.joinAnotherKingdom(core);
        if (seat.kingdom != core) return false;
        core.setCapital(seat);

        // 以属地制度治理的成员各自脱离
        int left = 0;
        foreach (Kingdom member in (empire.kingdoms_list ?? new List<Kingdom>()).ToList())
        {
            if (member?.data == null || member.isRekt() || member == core ||
                !CultureService.SameKin(ResolveRegionalInstitutionCulture(member), institutional)) continue;
            empire.leave(member);
            left++;
        }

        // 朝廷复行旧制，复合帝国作罢
        if (core.GetRegime()?.type != tradition && !RepublicSystem.IsRegimeLocked(core))
        {
            core.SetRegimeType(tradition);
            core.LoadRegime();
            core.SystemChange();
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(core);
        }
        empire.data.composite_integration_stage = CompositeEmpireIntegrationStage.None;
        empire.data.composite_integration = 0;
        empire.data.composite_route = 0;
        empire.data.institutional_culture = "";
        empire.data.external_identity_culture = ruling;
        empire.data.military_tradition = "";
        empire.data.nativist_revival_at = -1d;
        empire.data.empire_type_key = "";
        if (oldSeat != null && !oldSeat.isRekt() && oldSeat != seat && !empire.data.capital_loss_prefix_applied &&
            !core.HasCustomCountryNaming())
            empire.data.directPre = Direction(oldSeat, seat);
        InstitutionSystem.ResyncCulture(empire);
        SynchronizeMilitaryTraditionClaims(empire);
        empire.data.centerOffice ??= new CenterOffice();
        empire.data.centerOffice.Init(core);

        string text = string.Format(LM.Get(homesick ? "composite_northern_retreat_shuitu_history" : "composite_northern_retreat_history"),
            before, institutional.GetCultureTranslate(), seat.GetCityName(), empire.GetEmpireFullName(), left);
        EventRecorder.Record(empire, text);
        // 离草从头计(见 ShuiTuSystem)
        ShuiTuSystem.OnNorthernRetreat(core);
        LogService.LogInfo($"[EmpireCraft][征服王朝] {before} 北归 {seat.GetCityName()}{(homesick ? "(离草思归)" : "")}，{left} 个成员脱离");
        return true;
    }

    // 复合帝国作罢(一键易俗把整个帝国改成一个文化时，见 RealmCultureSystem)：不改政体，只清掉双重身份
    public static void Dissolve(Empire empire)
    {
        if (empire?.data == null) return;
        empire.data.composite_integration_stage = CompositeEmpireIntegrationStage.None;
        empire.data.composite_integration = 0;
        empire.data.composite_route = 0;
        empire.data.institutional_culture = "";
        empire.data.external_identity_culture = "";
        empire.data.military_tradition = "";
        empire.data.nativist_revival_at = -1d;
        SynchronizeMilitaryTraditionClaims(empire);
    }

    // 本族旧地最大的城：先找朝廷自己的，再找成员的(成员的都城不拿)；离草思归的只回旱地上的旧地(见 ShuiTuSystem.IsDryCity)
    private static City HomelandSeat(Empire empire, string ruling, bool dryOnly = false)
    {
        bool Home(City city) => city?.data != null && !city.isRekt() &&
                                string.Equals(CultureService.GetMainCulture(city, initialize: false), ruling, StringComparison.Ordinal) &&
                                (!dryOnly || ShuiTuSystem.IsDryCity(city));
        City own = (empire.CoreKingdom.cities ?? new List<City>()).Where(Home)
            .OrderByDescending(city => city.CountLivingPopulation()).FirstOrDefault();
        if (own != null) return own;
        return (empire.kingdoms_list ?? new List<Kingdom>())
            .Where(member => member?.cities != null && !member.isRekt() && member != empire.CoreKingdom)
            .SelectMany(member => member.cities)
            .Where(city => Home(city) && city.kingdom?.capital != city)
            .OrderByDescending(city => city.CountLivingPopulation()).FirstOrDefault();
    }

    private static string Direction(City from, City to)
    {
        float dx = to.city_center.x - from.city_center.x, dy = to.city_center.y - from.city_center.y;
        if (Math.Abs(dx) > Math.Abs(dy)) return dx > 0f ? "Eastern" : "Western";
        return dy > 0f ? "Northern" : "Southern";
    }

    #endregion
}
