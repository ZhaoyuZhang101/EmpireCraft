using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.AI.KingdomAI;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;

namespace EmpireCraft.Scripts.GamePatches;

public class KingdomPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(RefreshCapitalCityNames)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.setCapital), new[] { typeof(City) }),
            prefix: new HarmonyMethod(GetType(), nameof(RememberCapitalCity)),
            postfix: new HarmonyMethod(GetType(), nameof(RefreshCapitalCityNames))
        );
        new Harmony(nameof(RemovePatchData)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(RemovePatchData))
        );         
        new Harmony(nameof(NewCivKingdom)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.newCivKingdom)),
            postfix: new HarmonyMethod(GetType(), nameof(NewCivKingdom))
        );
        // 王国资产不存档，读档时按建国者物种重算——非文明物种建的王国每次读档都会变回 null，
        // 随后加载单位时 City.isNeutral() 空引用，整个存档读不进来。读完王国数据立刻补上
        new Harmony(nameof(RepairAfterLoad)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.loadData), new[] { typeof(KingdomData) }),
            postfix: new HarmonyMethod(GetType(), nameof(RepairAfterLoad))
        );
        new Harmony(nameof(RepairBeforeRemoval)).Patch(
            AccessTools.Method(typeof(KingdomManager), nameof(KingdomManager.removeObject)),
            prefix: new HarmonyMethod(GetType(), nameof(RepairBeforeRemoval))
        );
        new Harmony(nameof(new_emperor)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.setKing)),
            prefix: new HarmonyMethod(GetType(), nameof(before_new_emperor)),
            postfix: new HarmonyMethod(GetType(), nameof(new_emperor))
        );           
        new Harmony(nameof(emperor_left)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.removeKing)),
            prefix: new HarmonyMethod(GetType(), nameof(emperor_left))
        );             
        new Harmony(nameof(GetMaxCities)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.getMaxCities)),
            prefix: new HarmonyMethod(GetType(), nameof(GetMaxCities))
        );               
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(removeData))
        );
        new Harmony(nameof(countTotalWarriors)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.countTotalWarriors)),
            prefix: new HarmonyMethod(GetType(), nameof(countTotalWarriors)));
        new Harmony(nameof(getPopulationPeople)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.getPopulationPeople)),
            prefix: new HarmonyMethod(GetType(), nameof(getPopulationPeople)));
    }

    public static void RememberCapitalCity(Kingdom __instance, out City __state)
    {
        __state = __instance.capital;
        ReportRestoredCapitalTakeover(__instance);
    }

    // 还于旧都诊断：核心王国已坐镇记录旧都时还有人改它的首都——打出调用栈点名抢夺者。
    // (限频：每 10 秒最多一条，抓到元凶后随修复一起移除)
    private static float _capitalTakeoverLogAt;
    private static void ReportRestoredCapitalTakeover(Kingdom kingdom)
    {
        try
        {
            if (UnityEngine.Time.unscaledTime < _capitalTakeoverLogAt || kingdom?.capital == null) return;
            if (!EmpireCraft.Scripts.GeneralSystems.CoreCapitalRestorationSystem.IsAnchored(kingdom)) return;
            _capitalTakeoverLogAt = UnityEngine.Time.unscaledTime + 10f;
            NeoModLoader.services.LogService.LogWarning(
                $"[EmpireCraft][还于旧都诊断] {kingdom.name} 已坐镇记录旧都 {kingdom.capital.name}，仍被调用 setCapital。调用栈:\n" +
                new global::System.Diagnostics.StackTrace(2, false));
        }
        catch { }
    }

    public static void RefreshCapitalCityNames(Kingdom __instance, City __state)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance) ||
            __instance.GetRegime() == null) return;
        if (__state != null && !__state.isRekt()) __state.RefreshCityDisplayName();
        if (__instance.capital != null && __instance.capital != __state && !__instance.capital.isRekt())
            __instance.capital.RefreshCityDisplayName();
    }

    public static bool GetMaxCities(Kingdom __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        int maxCities = __instance.getActorAsset().civ_base_cities;
        if (__instance.hasKing())
            maxCities += (int) __instance.king.stats["cities"];
        if (maxCities < 1)
            maxCities = 1;
        // A ruler's legal domain is administered through its title and does not consume
        // personal city capacity. Losing the title removes this allowance immediately.
        maxCities += __instance.CountDeJureCapacityExemptCities();
        __result = maxCities;
        return false;
    }

    public static void removeData(Kingdom __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance == null)
        {
            return;
        }
        KingdomExtraData extraData = __instance.GetOrCreate();
        foreach (Kingdom child in FeudalVassalService.GetDirectVassals(__instance).ToList())
            FeudalVassalService.Break(child);
        KingdomTitle mainTitle = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.MainTitle);
        if (mainTitle != null)
        {
            mainTitle.EndJurisdiction(__instance, KingdomTitle.JurisdictionHolder);
            if (mainTitle.main_kingdom == __instance) mainTitle.main_kingdom = null;
        }
        KingdomTitle administrativeTitle = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.AdministrativeTitle);
        administrativeTitle?.EndJurisdiction(__instance, KingdomTitle.JurisdictionAdministration);
        if (__instance.HasGivenAlliance())
        {
            __instance.RemoveGivenAlliance();
        }

        if (__instance.HasTakenAlliance())
        {
            __instance.RemoveTakenAlliance(recordHistory: false);
        }
        __instance.RemoveExtraData<Kingdom, KingdomExtraData>();
    }

    public static void new_emperor(Kingdom __instance, Actor pActor, bool pFromLoad, Actor __state)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (pActor == null || pActor.IsWarMachine()) return; // 被 before_new_emperor 拒绝的船只
        if (__instance.king != pActor) return; // 被宪制拒绝的即位不能继续转移宗族和法理头衔
        // 真实年龄：新君即位时已到或将到天年的，按即位时的年纪另定余寿(见 RealisticAgeSystem.OnCrowned)
        if (!pFromLoad && !ModClass.IS_CLEAR && (__state == null || __state.id != pActor.id)) RealisticAgeSystem.OnCrowned(pActor);
        if (!ModClass.IS_CLEAR)
        {
            // 即位时姓本族的姓(华夏宗族)，世系、年号诏书上不会写成随错的姓
            pActor.CheckSpecificClan(false);
            SurnameInheritanceSystem.EnforceClanSurname(pActor.GetPersonalIdentity());
        }
        if (!pFromLoad && !ModClass.IS_CLEAR && __instance.IsEmpire()) LogEmperorChange(__instance, pActor, __state);
        if (!ModClass.IS_CLEAR)
        {
            pActor.CheckSpecificClan();
            // 新君的原版氏族跟着宗族走(人物档案上"氏族""宗族"一致)
            SpecificClanManager.SyncVanillaClan(pActor);
            __instance.SetSpecificClan(pActor.GetSpecificClan());
            __instance.TransferRealmTitlesToRuler(pActor);
            (__instance.GetEmpire() ?? __instance.GetTakenAllianceEmpire())?
                .SynchronizeLandedLegalTitles(__instance);
            foreach (var kt in ModClass.KINGDOM_TITLE_MANAGER)
            {
                if (kt.main_kingdom == __instance)
                {
                    if (kt.owner != pActor) pActor.AddOwnedTitle(kt);
                }
            }

            Regime regime = __instance.GetRegime();
            if (regime?.type == RegimeType.Feudalism)
            {
                KingdomType kingdomType = EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(__instance);
                if (WesternPeerageRules.TryGetRulerLevel(kingdomType, out PeeragesLevel level))
                {
                    pActor.SetPeeragesLevel(level);
                }
            }
            else if (__instance.HasMainTitle())
            {
                if (__instance.IsInEmpire() && !__instance.IsEmpire())
                {
                    Empire localEmpire = __instance.GetEmpire();
                    bool isImperialClan = pActor.GetSpecificClan() != null &&
                                           SpecificClanManager.SameLineage(pActor.GetSpecificClan(), localEmpire?.EmpireSpecificClan);
                    pActor.SetPeeragesLevel(isImperialClan
                        ? Enums.PeeragesLevel.peerages_2
                        : Enums.PeeragesLevel.peerages_3);
                    localEmpire?.SynchronizeLandedLegalTitles(__instance);

                } else if (!__instance.IsInEmpire())
                {
                    pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_1);
                }
            }
            // 推恩令下一人不得兼领两国：他原来统治的封国从宗室另立，或收归郡县
            if (!pFromLoad) GraceEdictService.OnKingCrowned(__instance, pActor);
            // 共主联盟：同一君主名下不在帝国里的王国自动结盟，以盟主国的名字命名
            if (!pFromLoad) PersonalUnionService.OnKingCrowned(__instance, pActor);
            if (__instance.IsEmpire())
            {
                Empire empire = __instance.GetEmpire();
                bool isActualSuccession = !pFromLoad && (__state == null || __state.id != pActor.id);
                // 即位后续(登记、重立储君、汗位/哈里发更替之乱、水土、即位分封)：与中央改成继承人的国、月度补登记共用，
                // 同一次换君只走一遍(见 Empire.CompleteSuccession)
                if (isActualSuccession) empire?.CompleteSuccession(pActor);
                else empire?.RepairFoundingEmperorMarker();
                LogService.LogInfo("触发原版选择国王");
                __instance.RemoveHeir();
                return;
            }
            // 不是帝国的宗主换了君主：改从游牧的附庸可能起兵独立(见 ShuiTuSystem)
            if (!pFromLoad && (__state == null || __state.id != pActor.id)) ShuiTuSystem.OnOverlordSucceeded(__instance);
            __instance.RemoveHeir();
        }
    }

    // 诊断：帝位每次更替记一行——先帝与新帝的姓名、宗族、年龄，是谁(哪段代码)把新帝扶上去的，
    // 用来查"皇帝一直换姓、年纪都很小"
    private static void LogEmperorChange(Kingdom kingdom, Actor emperor, Actor previous)
    {
        try
        {
            PersonalClanIdentity before = previous?.GetPersonalIdentity() ??
                                          SpecificClanManager.getPerson(kingdom.GetOrCreate().last_ruler_identity_id);
            string Describe(string name, string clan, int age) => $"{name}(宗族={clan ?? "无"},{age}岁)";
            string oldText = before == null ? "无"
                : Describe(before.name ?? before._actor?.getName(), before._specificClan?.name, before.age);
            emperor.CheckSpecificClan(false);
            string newText = Describe(emperor.getName(), emperor.GetSpecificClan()?.name, emperor.getAge());
            bool sameClan = before?._specificClan != null && before._specificClan == emperor.GetSpecificClan();
            bool branch = !sameClan && SpecificClanManager.SameLineage(before?._specificClan, emperor.GetSpecificClan());
            var frames = new global::System.Diagnostics.StackTrace(1, false).GetFrames() ?? Array.Empty<global::System.Diagnostics.StackFrame>();
            string source = string.Join(" ← ", frames.Select(frame => frame.GetMethod())
                .Where(method => method?.DeclaringType?.Namespace?.StartsWith("EmpireCraft") == true &&
                                 method.Name != nameof(LogEmperorChange) && method.Name != nameof(new_emperor))
                .Select(method => method.DeclaringType.Name + "." + method.Name).Distinct().Take(5));
            LogService.LogInfo($"[EmpireCraft][帝位更替] {kingdom.GetKingdomName()}：{oldText} → {newText} " +
                               $"{(sameClan ? "同宗" : branch ? "同宗支系" : "换宗族")} 来源={source}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][帝位更替] 记录失败: {exception.Message}");
        }
    }

    public static bool before_new_emperor(Kingdom __instance, Actor pActor, out Actor __state)
    {
        __state = __instance?.king;
        // 船只、战争机器不是人，不能当君主(不论哪条路径选出来的；读档时存档里已是船的也拒绝，由原版另选)
        if (pActor != null && pActor.IsWarMachine()) return false;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (ModClass.IS_CLEAR || __instance == null) return true;
        Empire empire = __instance.IsEmpire() ? __instance.GetEmpire() : null;
        if (ConstitutionalSuccessionSystem.IsProtected(empire) &&
            !ConstitutionalSuccessionSystem.CanInherit(empire, pActor)) return false;
        __instance.SyncRealmTitlesFromRuler(__instance.king);
        return true;
    }

    public static void emperor_left(Kingdom __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (ModClass.IS_CLEAR) return;
        Actor king = __instance.king;
        if (king != null)
        {
            __instance.GetOrCreate().last_ruler_identity_id = king.GetPersonalIdentity()?.id ?? -1L;
            EmpireCraftKingdomBehCheckHeir.PrepareForSuccession(__instance, king.GetPersonalIdentity());
        }
        __instance.SyncRealmTitlesFromRuler(king);
        if (__instance.HasMainCrime()) __instance.RemoveMainCrime();
        if (king != null && king.HasOfficeIdentity())
        {
            var officeIdentity = king.GetIdentity();
            officeIdentity?.RemoveOffice();
        }
        if (__instance.IsEmpire())
        {
            __instance.GetEmpire()?.EmperorLeft();
        }
    }

    // 原版按建国者物种的 kingdom_id_civilization 取王国资产；非文明物种(动物、战争机器、别的模组单位)
    // 取到的是 null，这个王国之后每次被销毁都在 getColor 抛空引用、永远删不掉，日志每帧刷屏。
    // 建国时和销毁前都补一个文明资产
    private static KingdomAsset FallbackCivAsset(Kingdom kingdom)
    {
        string founder = kingdom?.data?.original_actor_asset;
        ActorAsset species = string.IsNullOrEmpty(founder) ? null : AssetManager.actor_library.get(founder);
        string id = species?.kingdom_id_civilization;
        if (!string.IsNullOrEmpty(id) && AssetManager.kingdoms.dict.TryGetValue(id, out KingdomAsset asset) &&
            asset.civ) return asset;
        return AssetManager.kingdoms.dict.TryGetValue("human", out asset) ? asset
            : AssetManager.kingdoms.list.FirstOrDefault(candidate => candidate.civ);
    }

    public static void RepairKingdomAsset(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.asset != null) return;
        kingdom.asset = FallbackCivAsset(kingdom);
        LogService.LogWarning($"[EmpireCraft] 王国 {kingdom.id} 缺少王国资产，已补为 {kingdom.asset?.id}");
    }

    public static void RepairBeforeRemoval(Kingdom pKingdom) => RepairKingdomAsset(pKingdom);

    public static void RepairAfterLoad(Kingdom __instance) => RepairKingdomAsset(__instance);

    public static void NewCivKingdom(Kingdom __instance, Actor pActor)
    {
        RepairKingdomAsset(__instance);
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        __instance.RememberInitialRandomKingdomName();
        __instance.SetLevel(4);
        __instance.SetEmpireID(-1L);
        CultureService.ApplyFounderCulture(__instance, pActor, preferDeJure: true);
        Regime regime = __instance.GetRegime();
        if (regime == null)
        {
            __instance.SetRegimeType(RegimeType.Feudalism);
            __instance.LoadRegime();
            regime = __instance.GetRegime();
        }
        if (regime == null) return;
        regime.SetAllowDiplomacy(true);
        regime.SetAllowArmy(true);
    }
    public static void RemovePatchData(Kingdom __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        Empire empire = __instance.GetEmpire();
        if (empire == null) return;
        if (__instance.IsEmpire())
        {
            empire.CheckDissolve(__instance);
        }
        else
        {
            empire.leave(__instance);
        }
    }
    public static bool countTotalWarriors(Kingdom __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        var ed = __instance.GetOrCreate();
        if (ed is { last_cached_timestamp: > 0 })
        {
            __result = ed.cached_warriors;
            return false;
        }
        return true;
    }
    public static bool getPopulationPeople(Kingdom __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (CityPopulationSystem.VanillaScale && CityPopulationSystem.AbstractPopulationEnabled)
        {
            // 原版判断要不要开战时按古代口径折户(见 NoCommonersPatch.WarNeedPopulation)
            __result = NoCommonersPatch.InWarNeedScope
                ? NoCommonersPatch.WarNeedPopulation(__instance)
                : CityPopulationSystem.Households(__instance);
            return false;
        }
        var ed = __instance.GetOrCreate();
        if (ed is { last_cached_timestamp: > 0 })
        {
            __result = ed.cached_population;
            return false;
        }
        // 无小人模式：原版只数实体单位，补上各城的背景人口
        if (!CityPopulationSystem.AbstractPopulationEnabled) return true;
        int people = 0;
        if (__instance.units != null)
            foreach (Actor unit in __instance.units)
                if (unit != null && unit.isAlive() && unit.asset?.is_boat != true && unit.kingdom == __instance) people++;
        if (__instance.cities != null)
            foreach (City city in __instance.cities)
                people += CityPopulationSystem.BackgroundCount(city);
        __result = people;
        return false;
    }
}
