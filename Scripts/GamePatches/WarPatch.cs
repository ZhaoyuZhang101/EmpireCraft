using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.System;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using static UnityEngine.UI.CanvasScaler;

namespace EmpireCraft.Scripts.GamePatches;
public class WarPatch: GamePatch
{
    public ModDeclare declare { get; set; }

    // 同一 Kingdom 在同一场 War 里只排队一次开战动员。
    private static readonly Dictionary<War, HashSet<long>> _warMobilizedKingdoms = new();
    private static readonly HashSet<War> _expandingFeudalWars = new();
    public void Initialize()
    {
        // 战争窗口显示开战目标与战况(见 WarGoalSystem)
        new Harmony(nameof(AfterWarStats)).Patch(AccessTools.Method(typeof(WarWindow), "showStatsRows"),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(AfterWarStats)));
        new Harmony(nameof(end_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.endWar)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(end_war))
        );
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(War), nameof(War.Dispose)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(removeData))
        );
        new Harmony(nameof(update)).Patch(
            AccessTools.Method(typeof(War), nameof(War.update)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(DropBrokenWar)) { priority = Priority.First },
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(update))
        );

        // V3：把分帧征兵、分帧出征、和平/战争都可运行的 Warrior 生育
        // 统一挂在 ArmyManager.update，而不是 War.update。
        new Harmony(nameof(army_update_population)).Patch(
            AccessTools.Method(typeof(ArmyManager), nameof(ArmyManager.update)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(army_update_population))
        );
        new Harmony(nameof(new_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.newWar)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(new_war))
        );
        new Harmony(nameof(join_war_side)).Patch(
            AccessTools.Method(typeof(War), nameof(War.joinAttackers), new[] { typeof(Kingdom) }),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(BeforeJoinAttackers)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(join_war_side))
        );
        new Harmony(nameof(join_war_side) + "_defenders").Patch(
            AccessTools.Method(typeof(War), nameof(War.joinDefenders), new[] { typeof(Kingdom) }),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(BeforeJoinDefenders)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(join_war_side))
        );
        LogService.LogInfo("战争补丁加载成功");
    }
    
    private static readonly HashSet<long> _brokenWarsLogged = new();

    // 原版 War.update 默认主攻方、主守方都在：任何一方的王国查不到(比如开战时守方传了 null，
    // 或者王国在同一帧被革命/叛乱抹掉)就会在 main_defender.isAlive() 上崩，而且战争永远结束不了，每帧报一次。
    // 这种战争直接判和结束。
    public static bool DropBrokenWar(War __instance)
    {
        if (__instance?.data == null || __instance.hasEnded()) return true;
        if (__instance.getMainAttacker() != null &&
            (__instance.isTotalWar() || __instance.getMainDefender() != null &&
             __instance.getMainAttacker() != __instance.getMainDefender())) return true;
        try
        {
            World.world.wars.endWar(__instance, WarWinner.Nobody);
        }
        catch (Exception exception)
        {
            if (_brokenWarsLogged.Add(__instance.data.id))
                LogService.LogWarning($"[EmpireCraft] 结束主攻/主守方缺失或相同的战争 {__instance.data.name} 失败: {exception.Message}");
            return false;
        }
        if (_brokenWarsLogged.Add(__instance.data.id))
            LogService.LogInfo($"[EmpireCraft] 战争 {__instance.data.name} 主攻/主守方缺失或相同，已自动结束");
        return false;
    }

    public static void update(War __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance.getMainAttacker() == null) return; // 坏战争，前置已经处理

        EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.Ensure(__instance);
        RecordWarDeclared(__instance);
        if (!EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.ContainedWar(__instance)) WorldWarSystem.TryEscalate(__instance);
        if (!__instance.hasEnded() && __instance.GetEmpireWarType() == EmpireWarType.索取法理)
        {
            KingdomTitle targetTitle = __instance.GetTitleTarget();
            if (__instance.IsMainDefenderEliminated() ||
                __instance.AreAllDeJureTitleZonesControlledByAttackers(targetTitle))
            {
                World.world.wars.endWar(__instance, WarWinner.Attackers);
                return;
            }
        }
        if (!__instance.hasEnded() && __instance.GetEmpireWarType() == EmpireWarType.不奉诏)
        {
            VassalInvestitureService.CheckDefianceWar(__instance);
            if (__instance.hasEnded()) return;
        }
        if (!__instance.hasEnded() && __instance.GetEmpireWarType() == EmpireWarType.去帝号 &&
            (__instance.IsMainDefenderEliminated() || __instance.HasLegitimacyOccupationThreshold()))
        {
            World.world.wars.endWar(__instance, WarWinner.Attackers);
            return;
        }
        // 同一法统内部的统一战争打到底，不受战争年限强制停战(见 WarlordEraSystem 统一进程)；
        // 开国雄主统一本帝国核心的战争同样打到底
        if (__instance.getDuration() > ModClass.WAR_END_YEAR && !WarlordEraSystem.IsCivilWar(__instance) &&
            !RulerTraitSystem.IsFounderUnificationWar(__instance))
        {
            var attacker = __instance.getMainAttacker()?.king;
            if (attacker != null)
            {
                var plot = AssetManager.plots_library.basic_plots.Find(p => p.id == "force_stop_war");
                if (!attacker.plot?.isSameType(plot) ?? true)
                {
                    
                    plot?.try_to_start_advanced(attacker, plot, true);
                } 
            }
            var defender = __instance.getMainDefender()?.king;
            if (defender != null)
            {
                var plot = AssetManager.plots_library.basic_plots.Find(p => p.id == "force_stop_war");
                if (!defender.plot?.isSameType(plot) ?? true)
                {
                    
                    plot?.try_to_start_advanced(defender, plot, true);
                } 
            }
        }
    }
    public static void army_update_population(
        ArmyManager __instance,
        float pElapsed)
    {
        // 这三个函数内部都有限额/错峰，不会在同一帧处理全部人口。
        EmpireCraftActorCheckWarrior.ProcessArmyPopulationMaintenance();
        EmpireCraftActorCheckWarriorMove.ProcessQueuedMovementJobs();
    }

    public static void removeData(War __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;

        _warMobilizedKingdoms.Remove(__instance);
        _expandingFeudalWars.Remove(__instance);
        __instance.RemoveExtraData<War, WarExtraData>();
    }

    // 战争结束：一方的城市里清掉另一方(这场战争的对手)留下的占领；若城主与该占领者还有别的战争在打，保留
    private static void ClearOccupationAfterWar(War endedWar, List<Kingdom> owners, List<Kingdom> opponents)
    {
        foreach (Kingdom owner in owners)
        {
            if (owner?.cities == null || owner.isRekt()) continue;
            var cleared = new HashSet<Kingdom>(opponents.Where(opponent => opponent != null &&
                !StillAtWar(owner, opponent, endedWar)));
            if (cleared.Count == 0) continue;
            foreach (City city in owner.cities.ToList())
                city?.ClearOccupationBy(cleared.Contains);
        }
    }

    private static bool StillAtWar(Kingdom a, Kingdom b, War except) =>
        a.getWars().Any(war => war != null && war != except && !war.hasEnded() &&
                               (war.isAttacker(a) && war.isDefender(b) || war.isDefender(a) && war.isAttacker(b)));

    public static bool end_war(WarManager __instance, War pWar, WarWinner pWinner = WarWinner.Nobody)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pWar)) return true;
        SmoothBorders.RefreshWar(pWar);
        if (pWinner == WarWinner.Attackers && pWar.GetEmpireWarType() == EmpireWarType.索取法理 &&
            !pWar.IsMainDefenderEliminated() &&
            !pWar.AreAllDeJureTitleZonesControlledByAttackers(pWar.GetTitleTarget()))
        {
            return false;
        }
        if (pWinner == WarWinner.Attackers && pWar.GetEmpireWarType() == EmpireWarType.去帝号 &&
            !pWar.IsMainDefenderEliminated() && !pWar.HasLegitimacyOccupationThreshold())
        {
            return false;
        }
        if (pWar.isAlive() && !pWar.hasEnded())
        {
            RecordWarDeclared(pWar);
            CaptureWarRoyalHouses(pWar);
            RememberDefeatedWarHouses(pWar, pWinner);
            RecordWarEnded(pWar, pWinner);
            // 按开战目标结算和约(赔款、割地、吞并，见 WarGoalSystem)
            EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.Settle(pWar, pWinner);
            // 圣战：圣地交接、教权与正统
            EmpireCraft.Scripts.GeneralSystems.PapacySystem.OnWarEnded(pWar, pWinner);
            // 帝国战争、执行禁令、宗教战争的结局
            EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.OnWarEnded(pWar, pWinner);
            // 劫掠之战收场：联盟散伙、盟主威望(见 NomadRaidSystem)
            EmpireCraft.Scripts.GeneralSystems.NomadRaidSystem.OnWarEnded(pWar, pWinner);
            // 游牧争草场收场：割争城或赔款(见 SteppeOrderSystem)
            EmpireCraft.Scripts.GeneralSystems.SteppeOrderSystem.OnWarEnded(pWar, pWinner);
            ReignRecordSystem.OnWarEnded(pWar, pWinner);
            WorldWarSystem.Resolve(pWar, pWinner);
            World.world.game_stats.data.peacesMade++;
            World.world.map_stats.peacesMade++;
            pWar.setWinner(pWinner);
            __instance.warStateChanged();
            pWar.endForSides(pWinner);
            pWar.data.died_time = World.world.getCurWorldTime();
            FeudalVassalService.ResolveIndependenceWar(pWar, pWinner);

            // 不在 endWar 同一帧直接复员。
            // 延后由 ArmyManager.update 检查，避免战争关系尚未完全刷新。
            EmpireCraftActorCheckWarrior.RequestDemobilizationCheck();
            Kingdom aKingdom = null;
            Kingdom dKingdom = null;
            aKingdom = pWar.getMainAttacker();
            dKingdom = pWar.getMainDefender();
            // 正在筹备称帝的国家打了败仗，称帝即失败
            if (pWinner == WarWinner.Attackers)
                foreach (Kingdom loser in pWar._list_defenders.ToList()) EmpireFormationService.OnWarLost(loser);
            else if (pWinner == WarWinner.Defenders)
                foreach (Kingdom loser in pWar._list_attackers.ToList()) EmpireFormationService.OnWarLost(loser);
            // 只清掉这场战争的对手留下的占领；与城主还有别的仗在打的占领者保留进度
            ClearOccupationAfterWar(pWar, pWar._list_attackers.ToList(), pWar._list_defenders.ToList());
            ClearOccupationAfterWar(pWar, pWar._list_defenders.ToList(), pWar._list_attackers.ToList());
            if (pWinner == WarWinner.Attackers)
            {
                if (aKingdom.IsEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(30);
                    }
                    empire.AddMandate(10);
                    empire.AddRenown(100);
                }
                if (dKingdom.IsEmpire())
                {
                    Empire empire = dKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(-50);
                    }
                    empire.AddRenown(-50);
                }
            } else if (pWinner == WarWinner.Defenders)
            {
                if (dKingdom.IsEmpire())
                {
                    Empire empire = dKingdom.GetEmpire();
                    if (empire.Emperor!=null)
                    {
                        empire.Emperor.editRenown(30);

                    }
                    empire.AddRenown(30);
                }
                if (aKingdom.IsEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(-50);
                    }
                    empire.AddRenown(-50);
                }
            }

            switch (pWar.GetEmpireWarType())
            {
                case EmpireWarType.获取帝国:
                    if (pWinner == WarWinner.Attackers)
                    {
                        Kingdom kingdom = pWar.getMainAttacker();
                        if (kingdom != null)
                        {
                            kingdom.GetEmpire().ReplaceEmpire(kingdom);
                            TranslateHelper.LogministerAqcuireEmpire(kingdom.king, kingdom.GetEmpire());
                        }
                        return false;
                    }
                    break;
                case EmpireWarType.派系叛乱:
                    Kingdom attacker = pWar.getMainAttacker();
                    // 在胜方替换帝国核心之前结算改革叛乱，否则原帝国和派系上下文会丢失。
                    InstitutionSystem.ResolveReformRebellion(pWar, pWinner);
                    InstitutionSystem.ResolveSocialRebellion(pWar, pWinner);
                    LandEconomySystem.ResolvePeasantLandRebellion(pWar, pWinner);
                    if (RepublicSystem.IsRevolutionWar(pWar))
                        RepublicSystem.ResolveRevolutionWar(pWar, pWinner);
                    else if (pWinner == WarWinner.Attackers)
                    {
                        attacker.GetEmpire().ReplaceEmpire(attacker);
                    }
                    // 联省兵谏的响应者也有叛乱状态与临时国名，停战时一并清理。
                    foreach (Kingdom participant in pWar._list_attackers.ToList()) participant?.EndFactionRebelling();
                    break;
                case EmpireWarType.清君侧:
                    // 一派独大引发的清君侧(见 FactionPurgeSystem)；皇位空缺时的清君侧没有派系记录，照旧
                    FactionPurgeSystem.Resolve(pWar, pWinner);
                    break;
                case EmpireWarType.地方叛乱:
                case EmpireWarType.地方独立:
                    Kingdom attacker1 = pWar.getMainAttacker();
                    InstitutionSystem.ResolveSocialRebellion(pWar, pWinner);
                    LandEconomySystem.ResolvePeasantLandRebellion(pWar, pWinner);
                    RepublicSystem.ResolveRevolutionWar(pWar, pWinner);
                    attacker1.EndLocalRebelling();
                    break;
                case EmpireWarType.索取法理:
                    KingdomTitle title = pWar.GetTitleTarget();
                    if (pWinner == WarWinner.Attackers)
                    {
                        Kingdom kingdom = pWar.getMainAttacker();
                        Kingdom defeatedKingdom = pWar.getMainDefender();
                        if (kingdom != null && !kingdom.HasTakenAlliance() && pWar.IsMainDefenderEliminated())
                        {
                            List<long> defeatedTitleIds = pWar.GetOrCreate().defender_realm_title_ids ??
                                                          new List<long>();
                            if (defeatedTitleIds.Count == 0 && defeatedKingdom != null)
                                defeatedTitleIds = defeatedKingdom.GetRealmTitleIds().ToList();
                            // 西方封建制一法理一国：能另立的法理由获胜君主兼领，其余照旧并入
                            List<KingdomTitle> inherited = PersonalUnionService.InheritTitlesAsUnion(kingdom,
                                defeatedKingdom, defeatedTitleIds, out List<long> mergeTitleIds);
                            inherited.AddRange(kingdom.InheritRealmTitles(defeatedKingdom, mergeTitleIds));
                            if (inherited.Count > 0)
                            {
                                string notice = string.Format(LM.Get("title_all_inherited_notice"),
                                    kingdom.GetKingdomName(), pWar.GetOrCreate().defender_kingdom_name);
                                EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(notice, kingdom);
                                kingdom.GetEmpire()?.RecordHistory(directContent: notice,
                                    actorId: kingdom.king?.id ?? -1L, kingdomId: kingdom.id);
                                kingdom.king?.RecordPersonalHistory(notice);
                            }
                        }
                        else if (title != null)
                        {
                            if (kingdom != null && !kingdom.HasTakenAlliance())
                            {
                                List<KingdomTitle> transferred = PersonalUnionService.InheritTitlesAsUnion(kingdom,
                                    defeatedKingdom, new[] { title.id }, out List<long> mergeTitleIds);
                                transferred.AddRange(kingdom.InheritRealmTitles(defeatedKingdom, mergeTitleIds));
                                if (transferred.Contains(title))
                                    TranslateHelper.LogKingTakeTitle(kingdom, title);
                            }
                        }
                        return false;
                    }
                    break;
                case EmpireWarType.去帝号:
                    ImperialLegitimacyChallengeService.ResolveWar(pWar, pWinner);
                    break;
                case EmpireWarType.不奉诏:
                    VassalInvestitureService.ResolveDefianceWar(pWar, pWinner);
                    break;
            }
            WorldLog.logWarEnded(pWar);
        }
        return false;
    }
    public static void AfterWarStats(WarWindow __instance)
    {
        try
        {
            War war = SelectedMetas.selected_war;
            string goal = EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.Describe(war);
            if (goal != null)
            {
                bool switchable = EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.Switchable(war);
                string hint = switchable ? NeoModLoader.General.LM.Get("war_goal_switch_hint") : "";
                KeyValueField row = __instance.showStatRow("war_goal", goal + hint, "#F3C34A", pIconPath: "iconWar");
                // 点一下换一个目标(行从对象池复用，原版每次显示都会清掉 on_click_value)
                if (switchable && row != null)
                    row.on_click_value = () =>
                    {
                        EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.Next(war);
                        row.value.text = EmpireCraft.Scripts.GeneralSystems.WarGoalSystem.Describe(war) +
                                         NeoModLoader.General.LM.Get("war_goal_switch_hint");
                    };
            }
            // 双方战时统帅(见 WarCommandSystem)
            foreach ((string id, Kingdom side) in new[] { ("war_commander_attackers", war?.main_attacker),
                         ("war_commander_defenders", war?.main_defender) })
            {
                Actor commander = EmpireCraft.Scripts.GeneralSystems.WarCommandSystem.Commander(side);
                if (commander != null && war != null && !war.hasEnded())
                    __instance.showStatRow(id, $"{EmpireCraft.Scripts.GeneralSystems.WarCommandSystem.TitleOf(side)} {commander.getName()}",
                        "#D9B36A", pIconPath: "iconWar");
            }
            // 双方粮草官与军粮(见 LogisticsSystem)
            foreach ((string qmId, string foodId, Kingdom side) in new[]
                     {
                         ("war_quartermaster_attackers", "war_rations_attackers", war?.main_attacker),
                         ("war_quartermaster_defenders", "war_rations_defenders", war?.main_defender)
                     })
            {
                if (war == null || war.hasEnded() || side?.data == null) continue;
                Actor quartermaster = EmpireCraft.Scripts.GeneralSystems.WarCommandSystem.Quartermaster(side);
                if (quartermaster != null)
                    __instance.showStatRow(qmId,
                        $"{EmpireCraft.Scripts.GeneralSystems.WarCommandSystem.QuartermasterTitleOf(side)} {quartermaster.getName()}",
                        "#D9B36A", pIconPath: "iconWar");
                var sideData = side.GetOrCreate();
                if (sideData.war_food_need > 0f)
                    __instance.showStatRow(foodId, string.Format(NeoModLoader.General.LM.Get("war_rations_format"),
                        sideData.war_food_need.ToString("0.#"), sideData.war_food_short.ToString("0.#")),
                        sideData.war_food_short > 0f ? "#E8584F" : "#9BD27A", pIconPath: "iconWar");
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][开战目标] 战争窗口显示失败: {exception.Message}");
        }
    }

    public static void new_war(War __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__result)) return;
        if (__result == null) return;

        WarSituation.RecordParticipant(__result, __result.main_attacker);
        WarSituation.RecordParticipant(__result, __result.main_defender);
        try
        {
            // 诸侯私战只许两家对打：不拉兄弟之国、姻亲(见 HolyRomanSystem.ContainedWar)
            bool contained = EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.ContainedWar(__result);
            if (!contained) EmpirePartitionSystem.OnWarStarted(__result);
            // 施行祭祀的攻方战前先卜(见 RitesSystem)
            RitesSystem.OnWarDeclared(__result);
            if (!contained) RoyalMarriageSystem.OnWarStarted(__result);
            // 保护约：原帝国攻打被保护者时保护人加入守方(见 HolyRomanSystem.Decline)
            if (!contained) EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.OnWarStarted(__result);
            SmoothBorders.RefreshWar(__result);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] 兄弟之国、姻亲之国援助失败: {exception.Message}");
        }
        // 原版按模板用 kingdom.name 生成战争名，改用地图铭牌上的国名
        if (__result.data != null)
            __result.data.name = WorldLogNamePatch.UseDisplayNames(__result.data.name,
                __result.main_attacker, __result.main_defender);
        ExpandFeudalWar(__result);
        RecordWarDeclared(__result);
        DetachHostileTributaries(__result);
        if (!EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.ContainedWar(__result))
            WorldWarSystem.TryEscalate(__result, force: true);

        // 只“排队”，这里不再同步扫描全国人口/军队。
        QueueWarSideMobilization(__result, __result._list_attackers);
        QueueWarSideMobilization(__result, __result._list_defenders);

        QueueKingdomMobilizationOnce(__result, __result.getMainAttacker());
        QueueKingdomMobilizationOnce(__result, __result.getMainDefender());

        Kingdom aKingdom = __result.getMainAttacker();
        Kingdom dKingdom = __result.getMainDefender();

        if (aKingdom != null && aKingdom.IsEmpire())
        {
            Empire empire = aKingdom.GetEmpire();
        }

        if (dKingdom != null && dKingdom.IsEmpire())
        {
            Empire empire = dKingdom.GetEmpire();
        }
    }

    public static bool BeforeJoinAttackers(War __instance, Kingdom pKingdom, ref bool __state)
    {
        return __state = CanJoinWarSide(__instance, pKingdom, attackers: true);
    }

    public static bool BeforeJoinDefenders(War __instance, Kingdom pKingdom, ref bool __state)
    {
        return __state = CanJoinWarSide(__instance, pKingdom, attackers: false);
    }

    public static bool CanJoinWarSide(War war, Kingdom kingdom, bool attackers)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(war)) return true;
        if (war?.data == null || kingdom?.data == null || kingdom.isRekt()) return false;
        // During creation the main defender is known before its first native join runs.
        if (attackers && !war.isTotalWar() && kingdom.id == war.data.main_defender ||
            !attackers && kingdom.id == war.data.main_attacker) return false;
        // Native joins only check the destination list; alliance auto-joining can otherwise
        // put one polity on both sides, even when feudal expansion itself filters correctly.
        var opposite = attackers ? war.data.list_defenders : war.data.list_attackers;
        return opposite?.Contains(kingdom.id) != true;
    }

    // 同一个 Postfix 兼容 joinAttackers(Kingdom) / joinDefenders(Kingdom)。
    public static void join_war_side(War __instance, Kingdom pKingdom, bool __state)
    {
        if (!__state) return;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;

        WarSituation.RecordParticipant(__instance, pKingdom);
        ExpandFeudalWar(__instance);
        CaptureWarRoyalHouses(__instance);
        DetachHostileTributaries(__instance);
        if (!EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.ContainedWar(__instance))
            WorldWarSystem.TryEscalate(__instance, force: true);

        QueueKingdomMobilizationOnce(__instance, pKingdom);
    }

    // 诸侯私战不扩封建附庸、共主领国(见 HolyRomanSystem.ContainedWar)
    private static void ExpandFeudalWar(War war)
    {
        if (war?.getAsset() != WarTypeLibrary.normal || EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.ContainedWar(war) ||
            !_expandingFeudalWars.Add(war)) return;
        try { FeudalVassalService.ExpandWar(war); }
        finally { _expandingFeudalWars.Remove(war); }
    }

    private static void QueueWarSideMobilization(
        War war,
        IEnumerable<Kingdom> side)
    {
        if (war == null || side == null)
            return;

        foreach (Kingdom kingdom in side)
        {
            QueueKingdomMobilizationOnce(war, kingdom);
        }
    }

    private static void QueueKingdomMobilizationOnce(
        War war,
        Kingdom kingdom)
    {
        if (war == null ||
            kingdom == null ||
            kingdom.isRekt())
        {
            return;
        }

        if (!_warMobilizedKingdoms.TryGetValue(
                war,
                out HashSet<long> mobilized))
        {
            mobilized = new HashSet<long>();
            _warMobilizedKingdoms[war] = mobilized;
        }

        if (!mobilized.Add(kingdom.id))
            return;

        // 1. 原有 Warrior 马上进入“分帧出征队列”。
        EmpireCraftActorCheckWarriorMove
            .QueueKingdomMobilizationOnWarStart(kingdom);

        // 2. 缺少的 Warrior 进入“分帧征兵队列”。
        // 每征出一个新兵，它会自动追加到上面的移动队列。
        EmpireCraftActorCheckWarrior
            .QueueWarStartFill(kingdom);
    }

    private static void CaptureWarRoyalHouses(War war)
    {
        if (war == null || war.getMainAttacker() == null || war.getMainDefender() == null) return;
        WarExtraData extra = war.GetOrCreate();
        extra.attacker_empire_houses ??= new List<DefeatedEmpireHouse>();
        extra.defender_empire_houses ??= new List<DefeatedEmpireHouse>();
        CaptureSideRoyalHouses(war._list_attackers, extra.attacker_empire_houses);
        CaptureSideRoyalHouses(war._list_defenders, extra.defender_empire_houses);
        extra.royal_houses_recorded = true;
    }

    private static void CaptureSideRoyalHouses(IEnumerable<Kingdom> side, List<DefeatedEmpireHouse> houses)
    {
        if (side == null) return;
        foreach (Kingdom kingdom in side)
        {
            Empire empire = kingdom?.GetEmpire();
            // A provincial war alone is not proof that its entire empire was defeated.
            if (empire == null || empire.CoreKingdom != kingdom || empire.IsArchived()) continue;
            long emperorId = empire.Emperor?.id ?? -1L;
            long clanId = empire.EmpireSpecificClan?.id ?? -1L;
            if (houses.Any(h => h != null && h.empire_id == empire.id &&
                h.emperor_id == emperorId && h.royal_clan_id == clanId)) continue;
            houses.Add(new DefeatedEmpireHouse
            {
                empire_id = empire.id, emperor_id = emperorId, royal_clan_id = clanId
            });
        }
    }

    private static void RememberDefeatedWarHouses(War war, WarWinner winner)
    {
        if (winner != WarWinner.Attackers && winner != WarWinner.Defenders) return;
        WarExtraData extra = war.GetOrCreate();
        var winners = winner == WarWinner.Attackers ? extra.attacker_empire_houses : extra.defender_empire_houses;
        var defeated = winner == WarWinner.Attackers ? extra.defender_empire_houses : extra.attacker_empire_houses;
        var winningSide = winner == WarWinner.Attackers ? war._list_attackers : war._list_defenders;
        var winningIds = new HashSet<long>(winners.Where(h => h != null).Select(h => h.empire_id));
        foreach (long empireId in winningIds)
        {
            Empire empire = ModClass.EMPIRE_MANAGER.get(empireId);
            if (empire == null || empire.IsArchived() || empire.isRekt() ||
                !winningSide.Contains(empire.CoreKingdom)) continue;
            foreach (DefeatedEmpireHouse house in defeated)
            {
                if (house != null && !winningIds.Contains(house.empire_id))
                    empire.RememberDefeatedEmpireHouse(house);
            }
        }
    }

    private static void DetachHostileTributaries(War war)
    {
        if (war == null || war.hasEnded()) return;
        DetachTributariesOnSide(war._list_attackers, war._list_defenders);
        DetachTributariesOnSide(war._list_defenders, war._list_attackers);
    }

    private static void DetachTributariesOnSide(IEnumerable<Kingdom> tributarySide, IEnumerable<Kingdom> enemySide)
    {
        if (tributarySide == null || enemySide == null) return;
        foreach (Kingdom tributary in tributarySide)
        {
            if (tributary == null || tributary.isRekt() || !tributary.HasTakenAlliance()) continue;
            Empire overlord = tributary.GetTakenAllianceEmpire();
            if (overlord == null || overlord.IsArchived())
            {
                tributary.RemoveTakenAlliance();
                continue;
            }

            foreach (Kingdom enemy in enemySide)
            {
                if (enemy == null || enemy.isRekt()) continue;
                if (enemy == overlord.CoreKingdom || enemy.GetEmpire() == overlord)
                {
                    tributary.RemoveTakenAlliance();
                    break;
                }
            }
        }
    }

    private static void RecordWarDeclared(War war)
    {
        if (war == null || war.hasEnded()) return;
        WarExtraData extraData = war.GetOrCreate();
        if (!extraData.royal_houses_recorded) CaptureWarRoyalHouses(war);
        if (extraData.history_declaration_recorded) return;
        if (war.getMainAttacker() == null || war.getMainDefender() == null) return;

        extraData.history_declaration_recorded = true;
        RecordWarHistory(war, EmpireHistoryType.war_declared_history);
    }

    private static void RecordWarEnded(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtraData extraData = war.GetOrCreate();
        if (extraData.history_end_recorded) return;

        extraData.history_end_recorded = true;
        EmpireHistoryType historyType = winner switch
        {
            WarWinner.Attackers => EmpireHistoryType.war_ended_attacker_victory_history,
            WarWinner.Defenders => EmpireHistoryType.war_ended_defender_victory_history,
            _ => EmpireHistoryType.war_ended_peace_history
        };
        RecordWarHistory(war, historyType);
    }

    private static void RecordWarHistory(War war, EmpireHistoryType historyType)
    {
        Kingdom mainAttacker = war.getMainAttacker();
        Kingdom mainDefender = war.getMainDefender();
        if (mainAttacker == null || mainDefender == null) return;

        var recordInfo = new Dictionary<string, string>
        {
            ["attacker"] = GetPolityHistoryName(mainAttacker),
            ["defender"] = GetPolityHistoryName(mainDefender),
            ["type"] = GetWarHistoryType(war)
        };
        Actor attackerLeader = GetSideLeader(war._list_attackers, mainAttacker);
        Actor defenderLeader = GetSideLeader(war._list_defenders, mainDefender);
        var recordedEmpires = new HashSet<long>();
        RecordWarHistoryForSide(war._list_attackers, recordInfo, defenderLeader, mainDefender, historyType,
            recordedEmpires);
        RecordWarHistoryForSide(war._list_defenders, recordInfo, attackerLeader, mainAttacker, historyType,
            recordedEmpires);
    }

    private static void RecordWarHistoryForSide(IEnumerable<Kingdom> side, Dictionary<string, string> recordInfo,
        Actor enemyLeader, Kingdom enemyKingdom, EmpireHistoryType historyType, HashSet<long> recordedEmpires)
    {
        if (side == null) return;
        foreach (Kingdom kingdom in side)
        {
            if (kingdom == null || kingdom.isRekt()) continue;
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.IsArchived() || empire.isRekt() || !recordedEmpires.Add(empire.id)) continue;
            empire.RecordHistory(historyType, recordInfo, actorId: enemyLeader?.id ?? -1L,
                kingdomId: enemyKingdom?.id ?? -1L);
        }
    }

    private static Actor GetSideLeader(IEnumerable<Kingdom> side, Kingdom preferredKingdom)
    {
        Actor preferredLeader = GetKingdomLeader(preferredKingdom);
        if (preferredLeader != null) return preferredLeader;
        if (side == null) return null;
        foreach (Kingdom kingdom in side)
        {
            Actor leader = GetKingdomLeader(kingdom);
            if (leader != null) return leader;
        }
        return null;
    }

    private static Actor GetKingdomLeader(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return null;
        if (kingdom.king != null) return kingdom.king;
        if (kingdom.capital != null && kingdom.capital.hasLeader()) return kingdom.capital.leader;
        if (kingdom.cities == null) return null;
        foreach (City city in kingdom.cities)
        {
            if (city != null && city.hasLeader()) return city.leader;
        }
        return null;
    }

    private static string GetPolityHistoryName(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        string name = empire?.GetEmpireFullName();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        name = kingdom?.GetKingdomFullName();
        return string.IsNullOrWhiteSpace(name) ? kingdom?.name ?? "" : name;
    }

    private static string GetWarHistoryType(War war)
    {
        EmpireWarType type = war.GetEmpireWarType();
        if (type != EmpireWarType.None)
        {
            string localizedType = LocalizedTextManager.getText(type.ToString());
            if (!string.IsNullOrWhiteSpace(localizedType) && localizedType != type.ToString()) return localizedType;
            return type.ToString();
        }

        return string.IsNullOrWhiteSpace(war.data?.name) ? "战争" : war.data.name;
    }
}
