using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.AI.KingdomAI;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.GamePatches;

public class KingdomPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
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
        if (!ModClass.IS_CLEAR)
        {
            pActor.CheckSpecificClan();
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
                if (isActualSuccession)
                {
                    empire?.NewEmperor(pActor);
                    if (empire != null && !RepublicSystem.IsRepublic(empire))
                    {
                        pActor.RecordPersonalHistory(string.Format(LM.Get("personal_history_became_emperor"), empire.GetEmpireName()));
                    }
                }
                else
                {
                    empire?.RepairFoundingEmperorMarker();
                }
                LogService.LogInfo("触发原版选择国王");
                __instance.RemoveHeir();
                // 不论新皇帝是怎么即位的，都在这里处理即位分封(按法理把土地封给新君的兄弟)
                if (isActualSuccession && empire != null) EnfeoffmentHelper.OnEmperorSucceeded(empire);
                return;
            }
            __instance.RemoveHeir();
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
        CultureService.ApplyFounderCulture(__instance, pActor);
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
        var ed = __instance.GetOrCreate();
        if (ed is { last_cached_timestamp: > 0 })
        {
            __result = ed.cached_population;
            return false;
        }
        return true;
    }
}
