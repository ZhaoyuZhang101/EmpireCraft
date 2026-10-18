using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 西方(Roma 制度线)的教廷与世俗帝国：
//   · 教廷：控制本宗教圣地(Religion.CityID)的国家；它的君主就是教宗。圣地所在国不是教皇国、而是世俗强国(帝国成员或核心)时，
//     教廷落在世俗强国手里(教廷之囚)；
//   · 教权(每个宗教一个值 0~100)：每年向目标回归——基准 55，教廷是独立的教皇国 +10，各西方帝国施行教会叙任 +2、
//     君主叙任 −2、脱离罗马 −4(合计最多 ±20)，教廷之囚 −15；加冕、悔罪、圣战胜负另有加减；
//   · 加冕：西方帝国的新君每年有四成可能赴圣地请教宗加冕(献礼为年度经常收入的一成)，加冕后正统 +8；
//     登基三年仍未加冕正统 −5；脱离罗马或世俗化之后自行加冕 +3；帝国自己握有圣地的直接加冕；
//   · 叙任权：教会叙任 / 政教协约 / 君主叙任，按施行的制度定(叙任权之争打赢也会改为君主叙任)；
//   · 什一税：每年把年度经常收入的 6% 按叙任权档位(全额 / 一半 / 不缴)上缴教廷。
public static partial class PapacySystem
{
    public const string Line = "Roma";
    public const string NodeClerical = "western_clerical_supremacy";
    public const string NodeSecular = "western_secular_estates";
    public const string NodeConcordat = "western_concordat";
    public const string NodeReformation = "western_reformation";
    public const string NodeSecularization = "western_secularization";

    public const int InvestitureChurch = 0, InvestitureConcordat = 1, InvestitureCrown = 2;

    private const float BaseAuthority = 55f, PapalStateBonus = 10f, CaptivityPenalty = 15f, AuthorityDrift = 0.15f;
    private const float CoronationChance = 0.4f;
    private const float CoronationGift = 0.1f;
    private const int UncrownedGraceYears = 3;
    private const float TitheRate = 0.06f;
    public const int CrownedLegitimacy = 8, SelfCrownedLegitimacy = 3, UncrownedLegitimacy = -5;

    #region 查询

    public static PapalRelationState State(Empire empire)
    {
        if (empire?.data == null) return null;
        return empire.data.papal ??= new PapalRelationState();
    }

    public static Religion ReligionOf(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        return core != null && core.hasReligion() ? core.religion : null;
    }

    public static bool IsWestern(Empire empire) =>
        empire?.data != null && InstitutionSystem.GetCultureLine(InstitutionSystem.GetPrimaryCulture(empire)) == Line;

    // 有教廷可言：西方文化、君主制、奉一个有圣地的宗教
    public static bool Applies(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
        !empire.CoreKingdom.isRekt() && IsWestern(empire) && !RepublicSystem.IsRepublic(empire) &&
        RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) && HolyCity(ReligionOf(empire)) != null;

    public static City HolyCity(Religion religion)
    {
        City city = religion?.GetCity();
        return city?.data != null && !city.isRekt() && city.kingdom != null && !city.kingdom.wild ? city : null;
    }

    // 教廷：控制圣地的国家(须奉本宗教)
    public static Kingdom Papacy(Religion religion)
    {
        Kingdom holder = HolyCity(religion)?.kingdom;
        return holder?.data != null && !holder.isRekt() && holder.religion == religion ? holder : null;
    }

    public static Actor Pope(Religion religion)
    {
        Actor king = Papacy(religion)?.king;
        return king?.data != null && !king.isRekt() && king.isAlive() ? king : null;
    }

    // 教廷落在世俗强国手里：圣地所在国不是教皇国，而且是帝国的一部分
    public static bool IsCaptive(Religion religion)
    {
        Kingdom holder = Papacy(religion);
        return holder != null && holder.GetKingdomType() != KingdomType.Feudalism_papal_state && holder.GetEmpire() != null;
    }

    public static float Authority(Religion religion) => religion?.data == null ? 0f : religion.GetOrCreate().PapalAuthority;

    public static void AddAuthority(Religion religion, float delta)
    {
        if (religion?.data == null) return;
        var data = religion.GetOrCreate();
        data.PapalAuthority = Mathf.Clamp(data.PapalAuthority + delta, 0f, 100f);
    }

    // 脱离罗马(宗教改革)或世俗化
    public static bool BrokeWithRome(Empire empire) =>
        InstitutionSystem.IsEnacted(empire, NodeReformation) || InstitutionSystem.IsEnacted(empire, NodeSecularization);

    public static int Investiture(Empire empire)
    {
        PapalRelationState state = State(empire);
        if (state == null) return InvestitureChurch;
        if (BrokeWithRome(empire) || state.investiture_won) return InvestitureCrown;
        if (InstitutionSystem.IsEnacted(empire, NodeConcordat)) return InvestitureConcordat;
        return InvestitureChurch;
    }

    public static bool IsCrowned(Empire empire)
    {
        PapalRelationState state = State(empire);
        Actor emperor = empire?.Emperor;
        return state != null && emperor != null && state.crowned_emperor_id == emperor.id;
    }

    public static bool IsExcommunicated(Empire empire) => State(empire)?.excommunicated_at >= 0d;

    // 正统里的教廷一项(MonarchyLegitimacy.Breakdown 调用)
    public static IEnumerable<(string key, int value)> LegitimacyItems(Empire empire)
    {
        bool caliphal = !Applies(empire) && AppliesCaliphal(empire);
        if (!Applies(empire) && !caliphal) yield break;
        PapalRelationState state = State(empire);
        // 阿拉伯一线：受册封、未受册封(见 PapacySystem.Caliphate)
        if (caliphal)
        {
            if (IsCrowned(empire)) yield return ("mandate_caliphal_investiture", CrownedLegitimacy);
            else if (empire.data.newEmperor_timestamp >= 0d && World.world != null &&
                     World.world.getCurWorldTime() >= empire.data.newEmperor_timestamp &&
                     Date.getYearsSince(empire.data.newEmperor_timestamp) >= UncrownedGraceYears)
                yield return ("mandate_uncrowned_caliphal", UncrownedLegitimacy);
            yield break;
        }
        // 圣座帝国：教宗即皇帝，正统按教权计
        if (IsHolySee(empire))
        {
            int holy = Mathf.RoundToInt((Authority(ReligionOf(empire)) - 50f) / 2f);
            if (holy != 0) yield return ("mandate_holy_see", holy);
            yield break;
        }
        if (IsExcommunicated(empire))
        {
            yield return ("mandate_excommunicated", ExcommunicatedLegitimacy);
            yield break;
        }
        if (IsCrowned(empire))
        {
            yield return state.self_crowned
                ? ("mandate_self_coronation", SelfCrownedLegitimacy)
                : ("mandate_papal_coronation", CrownedLegitimacy);
            yield break;
        }
        double since = empire.data.newEmperor_timestamp;
        if (since >= 0d && World.world != null && World.world.getCurWorldTime() >= since &&
            Date.getYearsSince(since) >= UncrownedGraceYears)
            yield return ("mandate_uncrowned", UncrownedLegitimacy);
    }

    public const int ExcommunicatedLegitimacy = -20;

    #endregion

    #region 教会与世俗两条路

    private static readonly HashSet<string> ClericalNodes = new() { NodeClerical, "western_theocratic_state" };
    private static readonly HashSet<string> SecularNodes = new()
        { NodeSecular, "western_centralized_monarchy", NodeConcordat, NodeReformation, NodeSecularization };

    // AI 选改革的加分：教会类节点 (教权-50)×0.6，世俗类节点反过来
    public static float ReformBias(Empire empire, InstitutionNodeConfig node)
    {
        if (node == null || !IsWestern(empire)) return 0f;
        Religion religion = ReligionOf(empire);
        if (religion == null) return 0f;
        float lean = (Authority(religion) - 50f) * 0.6f;
        if (ClericalNodes.Contains(node.id)) return lean;
        if (SecularNodes.Contains(node.id)) return -lean;
        return 0f;
    }

    // 文化演进挑节点的次序：教权 55 以上先教会后世俗，以下反过来；文化没有教廷时按文化名随机定一边
    public static int DriftRank(string culture, InstitutionNodeConfig node)
    {
        if (node == null || (!ClericalNodes.Contains(node.id) && !SecularNodes.Contains(node.id))) return 0;
        Religion religion = CultureReligion(culture);
        bool church = religion != null
            ? Authority(religion) >= 55f
            : ((culture?.GetHashCode() ?? 0) & 1) == 0;
        return ClericalNodes.Contains(node.id) == church ? 0 : 1;
    }

    private static Religion CultureReligion(string culture)
    {
        if (string.IsNullOrEmpty(culture) || World.world?.kingdoms == null) return null;
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom?.data != null && !kingdom.isRekt() && kingdom.hasReligion() &&
                string.Equals(CultureService.GetRealmCulture(kingdom), culture, StringComparison.Ordinal) &&
                HolyCity(kingdom.religion) != null)
                return kingdom.religion;
        return null;
    }

    #endregion

    #region 年度结算

    // 每年(ConstitutionalEconomySystem 年度结算)逐个帝国调用
    public static void Yearly(Empire empire)
    {
        try
        {
            Religion religion = ReligionOf(empire);
            if (religion != null) UpdateReligion(religion);
            TryBreakWithRome(empire);
            religion = ReligionOf(empire);
            if (!Applies(empire))
            {
                // 阿拉伯一线：册封与天课(见 PapacySystem.Caliphate)
                if (AppliesCaliphal(empire)) CaliphalYear(empire, religion);
                return;
            }
            PapalRelationState state = State(empire);
            NameHolySee(empire, state);
            state.investiture = Investiture(empire);
            UpdateParties(empire);
            Conflict(empire, state, religion);
            Coronation(empire, state, religion);
            Tithe(empire, state, religion);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][教廷] 年度结算失败: {exception.Message}");
        }
    }

    // 教权每年向目标回归一次(同一宗教一年只算一次)
    private static void UpdateReligion(Religion religion)
    {
        var data = religion.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (data.PapalUpdatedAt >= 0d && now >= data.PapalUpdatedAt && Date.getYearsSince(data.PapalUpdatedAt) < 1) return;
        data.PapalUpdatedAt = now;
        UpdateCaptivity(religion);
        Kingdom papacy = Papacy(religion);
        if (papacy == null)
        {
            Kingdom strongest = Strongest(religion);
            // 圣战：罗马一线、阿拉伯一线(法教传统的除外，见 WagesHolyWar)
            if (strongest != null && WagesHolyWar(CultureService.GetRealmCulture(strongest))) TryCrusade(religion);
            UpdateOrders(religion);
            return;
        }
        float target = BaseAuthority;
        if (papacy.GetKingdomType() == KingdomType.Feudalism_papal_state) target += PapalStateBonus;
        float stance = 0f;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (empire?.data == null || empire.IsArchived() || ReligionOf(empire) != religion || !IsWestern(empire)) continue;
            stance += BrokeWithRome(empire) ? -4f : Investiture(empire) switch
            {
                InvestitureChurch => 2f,
                InvestitureCrown => -2f,
                _ => 0f
            };
        }
        target += Mathf.Clamp(stance, -20f, 20f);
        if (IsCaptive(religion)) target -= CaptivityPenalty;
        if (data.Schism) target -= SchismPenalty;
        data.PapalAuthority = Mathf.Clamp(data.PapalAuthority + (target - data.PapalAuthority) * AuthorityDrift, 0f, 100f);
        // 圣战：西方(与阿拉伯的吉哈德)才有，法教传统的不打(见 WagesHolyWar)
        if (WagesHolyWar(CultureService.GetRealmCulture(papacy))) TryCrusade(religion);
        UpdateOrders(religion);
    }

    // 加冕：教宗加冕(献礼)、帝国自己握有圣地直接加冕、脱离罗马后自行加冕
    private static void Coronation(Empire empire, PapalRelationState state, Religion religion)
    {
        Actor emperor = empire.Emperor;
        if (emperor == null || !emperor.isAlive() || IsCrowned(empire) || IsExcommunicated(empire)) return;
        Kingdom core = empire.CoreKingdom;
        Kingdom papacy = Papacy(religion);
        if (BrokeWithRome(empire) || papacy == null)
        {
            Crown(empire, state, emperor, self: true, null);
            return;
        }
        if (papacy == core || papacy.GetEmpire() == empire)
        {
            Crown(empire, state, emperor, self: false, papacy);
            return;
        }
        // 封建制帝国：皇帝要远征教廷，一年后到达才加冕(见 HolyRomanSystem.Romzug)；到了却与教廷开战就作罢
        int romzug = HolyRomanSystem.Romzug(empire);
        if (romzug == HolyRomanSystem.RomzugTravelling) return;
        if (romzug != HolyRomanSystem.RomzugArrived)
        {
            if (UnityEngine.Random.value >= CoronationChance) return;
            if (papacy.isInWarWith(core)) return;
            if (HolyRomanSystem.StartRomzug(empire, papacy)) return;
        }
        else if (papacy.isInWarWith(core)) return;
        int gift = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * CoronationGift));
        if (gift > 0)
        {
            core.SubMoney(gift, TreasuryCategory.Tithe);
            papacy.AddMoney(gift, TreasuryCategory.Tithe);
        }
        Crown(empire, state, emperor, self: false, papacy);
    }

    private static void Crown(Empire empire, PapalRelationState state, Actor emperor, bool self, Kingdom papacy)
    {
        state.crowned_emperor_id = emperor.id;
        state.self_crowned = self;
        state.crowned_at = World.world.getCurWorldTime();
        Religion religion = ReligionOf(empire);
        if (!self) AddAuthority(religion, 2f);
        City holy = HolyCity(religion);
        Actor pope = Pope(religion);
        string text = self
            ? string.Format(LM.Get("papal_self_coronation_history"), emperor.getName(), empire.GetEmpireFullName())
            : string.Format(LM.Get(IsCaliphal(empire) ? "caliphal_investiture_history" : "papal_coronation_history"),
                emperor.getName(), holy?.GetCityName() ?? "", pope?.getName() ?? LM.Get("label_none"), empire.GetEmpireFullName());
        empire.RecordHistory(directContent: text, actorId: emperor.id, kingdomId: empire.CoreKingdom.id);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        emperor.RecordPersonalHistory(text);
        MonarchyLegitimacy.Invalidate(empire);
    }

    // 什一税：年度经常收入的 6%，按叙任权档位上缴教廷(教廷就是本帝国的不缴)
    private static void Tithe(Empire empire, PapalRelationState state, Religion religion)
    {
        double now = World.world.getCurWorldTime();
        if (state.last_tithe_at >= 0d && now >= state.last_tithe_at && Date.getYearsSince(state.last_tithe_at) < 1) return;
        state.last_tithe_at = now;
        state.tithe_paid = 0;
        Kingdom core = empire.CoreKingdom, papacy = Papacy(religion);
        if (papacy == null || papacy == core || papacy.GetEmpire() == empire || papacy.isInWarWith(core)) return;
        float share = Investiture(empire) switch
        {
            InvestitureChurch => 1f,
            InvestitureConcordat => 0.5f,
            _ => 0f
        };
        if (share <= 0f || IsExcommunicated(empire)) return;
        long amount = (long)(StateDebtSystem.AnnualStableIncome(core) * TitheRate * share);
        amount = Math.Min(amount, Math.Max(0L, (long)core.GetMoney()));
        if (amount <= 0) return;
        core.SubMoney((int)amount, TreasuryCategory.Tithe);
        papacy.AddMoney((int)amount, TreasuryCategory.Tithe);
        state.tithe_paid = amount;
    }

    #endregion
}
