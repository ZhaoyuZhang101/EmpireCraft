using System;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using HarmonyLib;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Compatibility;

// WarBox 的外交与战争系统跟本模组的帝国/封建体系对接。
//
// WarBox 只认识"平等的王国"：两个王国之间可以签互不侵犯条约、贸易伙伴、和约，打完仗要赔款。
// 本模组里王国之间还有上下级：同一帝国的成员(含朝贡国)、封建宗主与附庸。对接的原则：
//   · 同一帝国 / 宗主与附庸之间不签 WarBox 条约，旧条约也不拦宣战——讨伐不臣、附庸独立是帝国内政；
//   · 本模组发起的战争(伐不臣、叛乱、独立、索取法理……)由本模组决定何时结束和怎样收场，
//     WarBox 的"目标达成/投降/和谈计划"不去提前结束它们；
//   · 内战不走 WarBox 的赔款与和约(叛军战败赔款给自己的皇帝没有意义)，本模组拒绝结束的战争也不结算战利品；
//   · 夺回本就属于自己法理的城市、帝国内部城市易手，不算"占领"；
//   · 帝国成员的王权合法性以本模组的正统值为基础。
//
// 全部按类型名反射查找，没装 WarBox 就什么都不做。
public static class WarBoxCompatibility
{
    private static bool _applied;

    public static void EnsureApplied(Harmony harmony)
    {
        if (_applied || harmony == null) return;
        _applied = true;
        if (AccessTools.TypeByName("WarBox.Content.DiplomacyRelations") == null) return; // 没装 WarBox

        TryPatch(harmony, "WarBox.Content.DiplomacyRelations", "Set", nameof(BeforeSetRelation));
        // 同一帝国/宗主附庸的军队在彼此境内不算"外国军队越境"
        TryPatch(harmony, "WarBox.Content.DiplomacyEvents", "Record", nameof(BeforeDiplomacyEventRecord));
        TryPatch(harmony, "WarBox.Content.DiplomacyEvents", "Chronicle", nameof(BeforeDiplomacyEventChronicle));
        TryPatch(harmony, "WarBox.Content.Patch_Diplomacy_PactBlocksWar", "Prefix", nameof(BeforePactBlock));
        TryPatch(harmony, "WarMobilization.WarSpoilsSystem", "HasTreaty", null, nameof(AfterHasTreaty));
        TryPatch(harmony, "WarMobilization.WarSpoilsSystem", "OnWarEnded", nameof(BeforeWarSpoils));
        TryPatch(harmony, "WarMobilization.PeacePlanSystem", "RequestPeace", nameof(BeforeRequestPeace));
        TryPatch(harmony, "WarBox.Content.OccupationSystem", "OnConquered", nameof(BeforeOccupation));
        TryPatch(harmony, "WarBox.Content.PoliticalSystem", "Calculate", null, nameof(AfterLegitimacy));
        // 人民革命走本模组的政体机制；抗议的处置影响阶层怨气；帝国成员共用帝国的合法性；日志用完整国号
        TryPatch(harmony, "WarBox.Content.ProtestSystem", "StartRevolution", nameof(BeforeRevolution));
        TryPatch(harmony, "WarBox.Content.ProtestSystem", "RemoveKing", nameof(BeforeRemoveKing));
        TryPatch(harmony, "WarBox.Content.ProtestSystem", "ApplyReaction", null, nameof(AfterProtestReaction));
        TryPatch(harmony, "WarBox.Content.PoliticalSystem", "GetLegitimacy", null, nameof(AfterGetLegitimacy));
        TryPatch(harmony, "WarBox.Content.PoliticalSystem", "UpdateChronicle", nameof(BeforeLegitimacyChronicle));
        TryPatch(harmony, "WarBox.Content.DiplomacyEvents", "ChronicleAt", nameof(BeforeChronicleAt));
        TryPatch(harmony, "WarBox.Content.DiplomacyEvents", "ChronicleRaw", nameof(BeforeChronicleRaw));
    }

    private static void TryPatch(Harmony harmony, string typeName, string methodName, string prefix,
        string postfix = null)
    {
        try
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null
                ? null
                : AccessTools.GetDeclaredMethods(type).FirstOrDefault(m => m.Name == methodName);
            if (method == null)
            {
                LogService.LogWarning($"[EmpireCraft] WarBox 兼容：找不到 {typeName}.{methodName}，跳过");
                return;
            }
            harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(WarBoxCompatibility), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(WarBoxCompatibility), postfix));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 兼容补丁失败 {typeName}.{methodName}: {exception.Message}");
        }
    }

    #region 关系判断

    // 同一帝国(含朝贡国) / 封建宗主链上的上下级
    public static bool AreBound(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b || a.isRekt() || b.isRekt()) return false;
        try
        {
            return a.IsInSameEmpire(b) || FeudalVassalService.IsInChain(a, b) || FeudalVassalService.IsInChain(b, a);
        }
        catch
        {
            return false;
        }
    }

    // 帝国内政性质的战争：不结算 WarBox 的赔款与和约
    private static bool IsInternalWar(EmpireWarType type) => type is EmpireWarType.伐不臣 or EmpireWarType.地方独立
        or EmpireWarType.派系叛乱 or EmpireWarType.地方叛乱 or EmpireWarType.民族叛乱 or EmpireWarType.宗教叛乱
        or EmpireWarType.藩王索取皇位 or EmpireWarType.帝国正统 or EmpireWarType.去帝号 or EmpireWarType.清君侧
        or EmpireWarType.不奉诏 or EmpireWarType.附庸独立 or EmpireWarType.获取帝国 or EmpireWarType.统一;

    private static EmpireWarType TypeOf(War war)
    {
        try
        {
            return war == null ? EmpireWarType.None : war.GetEmpireWarType();
        }
        catch
        {
            return EmpireWarType.None;
        }
    }

    #endregion

    #region 补丁

    // 上下级之间不签 WarBox 条约(互不侵犯/贸易/敌对)
    public static bool BeforeSetRelation(Kingdom a, Kingdom b, int type) => type == 0 || !AreBound(a, b);

    // 上下级之间即使留着旧条约，也不拦宣战
    public static bool BeforePactBlock(Kingdom pAttacker, Kingdom pDefender, ref bool __result)
    {
        if (!AreBound(pAttacker, pDefender)) return true;
        __result = true;
        return false;
    }

    // WarBox 的越境检测把同一帝国成员、宗主与附庸的驻军也当成外国军队，拦下这类记忆和史书
    private const string BorderEvent = "border";

    public static bool BeforeDiplomacyEventRecord(Kingdom from, Kingdom to, string ev) =>
        ev != BorderEvent || !AreBound(from, to);

    public static bool BeforeDiplomacyEventChronicle(Kingdom a, Kingdom b, string ev) =>
        ev != BorderEvent || !AreBound(a, b);

    public static void AfterHasTreaty(Kingdom a, Kingdom b, ref bool __result)
    {
        if (__result && AreBound(a, b)) __result = false;
    }

    public static bool BeforeWarSpoils(War war)
    {
        if (war == null) return true;
        // 本模组的 endWar 补丁拒绝结束(比如索取法理还没拿全)时战争其实还在，不能先把赔款和约签了
        if (war.isAlive() && !war.hasEnded()) return false;
        if (IsInternalWar(TypeOf(war))) return false;
        return !AreBound(war.getMainAttacker(), war.getMainDefender());
    }

    // 本模组发起的战争由本模组决定何时结束
    public static bool BeforeRequestPeace(War war, ref bool __result)
    {
        if (TypeOf(war) == EmpireWarType.None) return true;
        __result = false;
        return false;
    }

    public static bool BeforeOccupation(City city, Kingdom oldKingdom, Kingdom newKingdom)
    {
        if (city == null || oldKingdom == null || newKingdom == null) return true;
        if (AreBound(oldKingdom, newKingdom)) return false;
        try
        {
            KingdomTitle title = city.GetTitle();
            if (title != null && (title.main_kingdom == newKingdom ||
                                  title.owner != null && title.owner == newKingdom.king))
                return false; // 收复法理领地
        }
        catch
        {
            // 取不到法理信息就按 WarBox 原逻辑处理
        }
        return true;
    }

    public static void AfterLegitimacy(Kingdom kingdom, object __result)
    {
        if (__result == null || kingdom == null) return;
        try
        {
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.isRekt()) return;
            // WarBox 的基础值是 25；帝国成员改用正统值：正统 50 对应 25，满正统 50，正统见底 0
            int basis = Mathf.Clamp(Mathf.RoundToInt(empire.Legitimacy * 0.5f), 0, 50);
            Traverse.Create(__result).Field("Base").SetValue(basis);
        }
        catch
        {
            // 字段改名了就保持 WarBox 原值
        }
    }

    #endregion

    #region 人民革命

    // WarBox 的人民革命原本只是把国王撤掉(本模组的继承随即补上一位新君，等于什么都没变)。
    // 帝国里改走本模组的政体机制：
    //   · 君主国：民意所向(没有就选最大的非保守政党)的政党推动建立共和(够票和平退位，不够打革命战争)；
    //     还没有政党政治的，王朝正统大损；
    //   · 共和国：一党制垮台、重开党禁；多党制则提前大选；
    //   · 帝国的成员国：就地起兵，地方叛乱。
    // WarBox 自己的收尾(城市动荡复位、撤换城主、结束示威)照常执行，只拦下撤国王。
    public static bool BeforeRevolution(Kingdom kingdom)
    {
        try
        {
            Empire empire = kingdom?.GetEmpire();
            if (empire != null && !empire.isRekt() && !empire.IsArchived()) HandleEmpireRevolution(empire, kingdom);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 人民革命对接失败: {exception.Message}");
        }
        return true;
    }

    public static bool BeforeRemoveKing(Kingdom kingdom, ref bool __result)
    {
        if (kingdom?.GetEmpire() == null) return true;
        __result = false;
        return false;
    }

    private static void HandleEmpireRevolution(Empire empire, Kingdom kingdom)
    {
        if (kingdom != empire.CoreKingdom)
        {
            StartMemberRebellion(empire, kingdom);
            return;
        }
        if (!RepublicSystem.IsRepublic(empire))
        {
            FixedFaction party = PublicOpinionSystem.TryGetPreferred(empire, out PartyIdeology preferred)
                ? PartySystem.GetParties(empire).FirstOrDefault(candidate => candidate.Ideology == preferred)
                : null;
            party ??= PartySystem.GetParties(empire).Where(candidate => candidate.Ideology != PartyIdeology.Conservatism)
                .OrderByDescending(candidate => candidate.CentralRatio).FirstOrDefault();
            if (party != null && party.Ideology != PartyIdeology.Conservatism)
            {
                RepublicSystem.PushRepublic(empire, party);
                if (RepublicSystem.IsRepublic(empire) || RepublicSystem.HasActiveRevolutionWar(empire)) return;
            }
            empire.AddMandate(-20);
            EventRecorder.Record(empire, string.Format(LM.Get("warbox_empire_revolution_monarchy"), empire.GetEmpireFullName()));
            return;
        }
        if (RepublicSystem.IsOneParty(empire))
        {
            PartyBanSystem.Open(empire, "party_ban_reopened_revolution_history");
            return;
        }
        ConstitutionalEconomyState state = empire.data?.constitutional_economy;
        if (state != null) state.last_parliament_election = -1d;
        EventRecorder.Record(empire, string.Format(LM.Get("warbox_empire_revolution_election"), empire.GetEmpireFullName()));
    }

    private static void StartMemberRebellion(Empire empire, Kingdom kingdom)
    {
        if (kingdom.IsLocalRebelling() || kingdom.IsFactionRebelling() || kingdom.getWars().Any() ||
            empire.CoreKingdom == null || !ModernStability.PassRebellionGate(empire.CoreKingdom)) return;
        if (!kingdom.StartLocalRebelling(EmpireWarType.地方叛乱)) return;
        War war = World.world.diplomacy.startWar(kingdom, empire.CoreKingdom, WarTypeLibrary.rebellion);
        if (war == null)
        {
            kingdom.EndLocalRebelling();
            return;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        RebellionStartupService.RaiseUprisingMilitia(kingdom, 0.6f);
        EventRecorder.Record(empire, string.Format(LM.Get("warbox_empire_revolution_member"), kingdom.GetKingdomFullName(),
            empire.GetEmpireFullName()));
    }

    #endregion

    #region 抗议 → 阶层怨气

    // 政府对示威的处置影响本模组的阶层怨气：置之不理、驱散会加重，谈判、让步、宣传会缓和。
    // 受影响的阶层看示威的诉求。一次示威只是一座城的事，数值比阶层起义本身的累积小得多。
    public static void AfterProtestReaction(object demo, bool __result)
    {
        if (!__result || demo == null) return;
        try
        {
            Traverse traverse = Traverse.Create(demo);
            Kingdom kingdom = traverse.Field("Kingdom").GetValue<Kingdom>();
            Empire empire = kingdom?.GetEmpire();
            InstitutionEmpireState institutions = empire?.data?.institution_state;
            if (institutions == null) return;
            string reaction = traverse.Field("Reaction").GetValue()?.ToString() ?? "";
            float delta = reaction switch
            {
                "Ignore" => 2f,
                "Disperse" => 4f,
                "Negotiate" => -1.5f,
                "Concessions" => -4f,
                "Propaganda" => -1f,
                _ => 0f
            };
            if (delta == 0f) return;
            string demand = traverse.Field("DemandKey").GetValue<string>() ?? "";
            SocialClass[] classes = demand switch
            {
                "warbox_protest_demand_taxes" => new[] { SocialClass.Peasant, SocialClass.Labour, SocialClass.Merchant },
                "warbox_protest_demand_welfare" => new[] { SocialClass.Peasant, SocialClass.Labour, SocialClass.Citizen },
                "warbox_protest_demand_peace" => new[] { SocialClass.Peasant, SocialClass.Labour, SocialClass.Citizen },
                "warbox_protest_demand_corruption" => new[] { SocialClass.Merchant, SocialClass.Citizen },
                _ => new[] { SocialClass.Citizen, SocialClass.Merchant, SocialClass.Labour }
            };
            institutions.class_grievances ??= new global::System.Collections.Generic.Dictionary<SocialClass, float>();
            foreach (SocialClass socialClass in classes)
            {
                institutions.class_grievances.TryGetValue(socialClass, out float current);
                institutions.class_grievances[socialClass] = Mathf.Clamp(current + delta, 0f, 100f);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 抗议对接失败: {exception.Message}");
        }
    }

    #endregion

    #region 金融危机 → 经济趋势

    private static MethodInfo _financeSnapshot;
    private static FieldInfo _crisisYearsLeft;
    private static FieldInfo _defaultYearsLeft;
    private static FieldInfo _unemploymentRate;
    private static bool _financeLookupDone;

    // WarBox 的金融状况：本国是否处于金融危机/国家违约，及失业率(没装 WarBox 时返回 false)
    public static bool TryGetFinance(Kingdom kingdom, out bool crisis, out float unemployment)
    {
        crisis = false;
        unemployment = 0f;
        if (kingdom == null || kingdom.isRekt()) return false;
        if (!_financeLookupDone)
        {
            _financeLookupDone = true;
            Type type = AccessTools.TypeByName("WarBox.Content.EconomyFinanceSystem");
            _financeSnapshot = type == null ? null : AccessTools.Method(type, "GetSnapshot", new[] { typeof(Kingdom) });
            Type snapshot = _financeSnapshot?.ReturnType;
            _crisisYearsLeft = snapshot == null ? null : AccessTools.Field(snapshot, "CrisisYearsLeft");
            _defaultYearsLeft = snapshot == null ? null : AccessTools.Field(snapshot, "DefaultYearsLeft");
            _unemploymentRate = snapshot == null ? null : AccessTools.Field(snapshot, "UnemploymentRate");
        }
        if (_financeSnapshot == null || _crisisYearsLeft == null) return false;
        try
        {
            object result = _financeSnapshot.Invoke(null, new object[] { kingdom });
            if (result == null) return false;
            crisis = (int)_crisisYearsLeft.GetValue(result) > 0 ||
                     _defaultYearsLeft != null && (int)_defaultYearsLeft.GetValue(result) > 0;
            if (_unemploymentRate != null) unemployment = (float)_unemploymentRate.GetValue(result);
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 金融状况读取失败: {exception.Message}");
            _financeSnapshot = null;
            return false;
        }
    }

    #endregion

    #region 示威与罢工 → 民意

    private static MethodInfo _isStrike;
    private static MethodInfo _isDemonstrating;
    private static bool _protestLookupDone;

    // 帝国境内正在示威、已转为罢工的城市数(没装 WarBox 时都为 0)。给 PublicOpinionSystem 计入民意
    public static void CountProtests(Empire empire, out int demonstrating, out int striking)
    {
        demonstrating = 0;
        striking = 0;
        if (empire == null) return;
        if (!_protestLookupDone)
        {
            _protestLookupDone = true;
            Type type = AccessTools.TypeByName("WarBox.Content.ProtestSystem");
            _isStrike = type == null ? null : AccessTools.Method(type, "IsStrike", new[] { typeof(City) });
            _isDemonstrating = type == null ? null : AccessTools.Method(type, "IsDemonstrating", new[] { typeof(City) });
        }
        if (_isStrike == null && _isDemonstrating == null) return;
        try
        {
            foreach (Kingdom kingdom in empire.kingdoms_list)
            {
                if (kingdom?.cities == null || kingdom.isRekt()) continue;
                foreach (City city in kingdom.cities)
                {
                    if (city == null || city.isRekt()) continue;
                    if (_isStrike != null && (bool)_isStrike.Invoke(null, new object[] { city })) striking++;
                    else if (_isDemonstrating != null && (bool)_isDemonstrating.Invoke(null, new object[] { city }))
                        demonstrating++;
                }
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] WarBox 罢工统计失败: {exception.Message}");
            _isStrike = _isDemonstrating = null;
        }
    }

    #endregion

    #region 合法性：帝国成员共用帝国的

    private static bool IsEmpireMember(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire?.CoreKingdom != null && empire.CoreKingdom != kingdom && !empire.isRekt();
    }

    // 成员国读帝国核心王国的合法性，危机只在帝国层面发生一次，不再每个成员国各报一次
    public static void AfterGetLegitimacy(Kingdom kingdom, ref int __result)
    {
        if (!IsEmpireMember(kingdom)) return;
        Kingdom core = kingdom.GetEmpire().CoreKingdom;
        if (core?.data == null) return;
        core.data.get("wb_political_legitimacy", out float value, -1f);
        if (value >= 0f) __result = Mathf.Clamp(Mathf.RoundToInt(value), 0, 100);
    }

    public static bool BeforeLegitimacyChronicle(Kingdom kingdom) => !IsEmpireMember(kingdom);

    #endregion

    #region 日志国号

    // WarBox 的日志直接用王国的原始名字(如"大理朝")，换成本模组的完整国号
    public static void BeforeChronicleAt(Kingdom kingdom, ref string text) => text = UseFullName(kingdom, text);

    public static void BeforeChronicleRaw(Kingdom k, ref string text) => text = UseFullName(k, text);

    private static string UseFullName(Kingdom kingdom, string text)
    {
        try
        {
            string raw = kingdom?.data?.name;
            if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrEmpty(text) || !text.Contains(raw)) return text;
            string full = kingdom.IsEmpire() ? kingdom.GetEmpire()?.GetEmpireFullName() : kingdom.GetKingdomFullName();
            return string.IsNullOrWhiteSpace(full) || full == raw ? text : text.Replace(raw, full);
        }
        catch
        {
            return text;
        }
    }

    #endregion

}
