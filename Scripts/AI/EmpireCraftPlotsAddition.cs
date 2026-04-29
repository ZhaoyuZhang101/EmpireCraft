using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NCMS.Extensions;
using NeoModLoader.General;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using UnityEngine;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;
using static System.Collections.Specialized.BitVector32;
using Random = System.Random;

namespace EmpireCraft.Scripts.AI
{
    public static class EmpireCraftPlotsAddition
    {
        private static readonly HashSet<string> s_guardedPlotIds = new HashSet<string>();

        private static void GuardAllPlotAssets()
        {
            foreach (var asset in AssetManager.plots_library.getList())
            {
                var plotAsset = (PlotAsset)asset;
                GuardPlotAsset(plotAsset);
            }

            GuardPlotAsset(PlotsLibrary.alliance_create);
        }

        private static void GuardPlotAsset(PlotAsset plotAsset)
        {
            if (plotAsset == null || string.IsNullOrWhiteSpace(plotAsset.id))
            {
                return;
            }

            if (!s_guardedPlotIds.Add(plotAsset.id))
            {
                return;
            }

            string plotId = plotAsset.id;

            if (plotAsset.check_is_possible != null)
            {
                var original = plotAsset.check_is_possible;
                plotAsset.check_is_possible = pActor => SafePlotBoolCall(plotId, "check_is_possible", pActor, () => original(pActor));
            }

            if (plotAsset.check_should_continue != null)
            {
                var original = plotAsset.check_should_continue;
                plotAsset.check_should_continue = pActor => SafePlotBoolCall(plotId, "check_should_continue", pActor, () => original(pActor));
            }

            if (plotAsset.check_can_be_forced != null)
            {
                var original = plotAsset.check_can_be_forced;
                plotAsset.check_can_be_forced = pActor => SafePlotBoolCall(plotId, "check_can_be_forced", pActor, () => original(pActor));
            }

            if (plotAsset.try_to_start_advanced != null)
            {
                var original = plotAsset.try_to_start_advanced;
                plotAsset.try_to_start_advanced = (pActor, pPlotAsset, pForced) =>
                    SafePlotStartCall(plotId, pActor, pPlotAsset, pForced, () => original(pActor, pPlotAsset, pForced));
            }

            if (plotAsset.action != null)
            {
                var original = plotAsset.action;
                plotAsset.action = pActor => SafePlotBoolCall(plotId, "action", pActor, () => original(pActor));
            }
        }

        private static bool SafePlotBoolCall(string plotId, string stage, Actor actor, Func<bool> callback)
        {
            if (actor == null || callback == null)
            {
                return false;
            }

            try
            {
                return callback();
            }
            catch (Exception ex)
            {
                LogPlotGuardException(plotId, stage, actor, ex);
                return false;
            }
        }

        private static bool SafePlotStartCall(string plotId, Actor actor, PlotAsset plotAsset, bool forced, Func<bool> callback)
        {
            if (actor == null || plotAsset == null || callback == null)
            {
                return false;
            }

            try
            {
                return callback();
            }
            catch (Exception ex)
            {
                LogPlotGuardException(plotId, $"try_to_start_advanced(forced={forced})", actor, ex);
                return false;
            }
        }

        private static void LogPlotGuardException(string plotId, string stage, Actor actor, Exception ex)
        {
            string actorName = actor?.data?.name ?? "unknown_actor";
            Debug.LogWarning($"[EmpireCraftPlotsAddition] plot '{plotId}' {stage} failed for actor '{actorName}': {ex}");
        }

        private static List<Kingdom> GetKingdomsRuledByActor(Actor actor)
        {
            List<Kingdom> result = new List<Kingdom>();
            if (actor == null || World.world?.kingdoms == null)
            {
                return result;
            }

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom != null && kingdom.king == actor)
                {
                    result.Add(kingdom);
                }
            }

            return result;
        }

        private static Empire FindTakenAllianceEmpireCandidate(Kingdom kingdom, bool requireGoodOpinion)
        {
            if (kingdom == null)
            {
                return null;
            }

            int kingdomWarriors = kingdom.countTotalWarriors();
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                if (empire == null || empire.IsArchived() || !empire.IsNeighbourWith(kingdom))
                {
                    continue;
                }

                if (requireGoodOpinion && !kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom))
                {
                    continue;
                }

                if (kingdomWarriors < empire.countWarriors())
                {
                    return empire;
                }
            }

            return null;
        }

        public static void init()
        {
            s_guardedPlotIds.Clear();
            AssetManager.plot_category_library.add(new PlotCategoryAsset()
            {
                id = "empirecraft_diplomacy",
                name = "plot_group_empirecraft_diplomacy",
                color = "#5EFFFF",
                show_counter = true,
                plot_retry_action = new PlotRetryAction(PlotCategoryLibrary.diplomacyRetryAction)
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "become_empire",
                path_icon = "ChineseCrown.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = pActor => false,
                check_can_be_forced = (Actor pActor) => true,
                try_to_start_advanced = delegate(Actor pActor, PlotAsset pPlotAsset, bool pForced)
                {
                    foreach (Plot plot3 in World.world.plots)
                    {
                        if (plot3.isActive() && plot3.isSameType(pPlotAsset))
                        {
                            pActor.setPlot(plot3);
                            return true;
                        }
                    }
                    World.world.plots.newPlot(pActor, pPlotAsset, pForced);
                    return true;
                },
                check_should_continue = (Actor pActor) => true,
                action = BecomeEmpireAndStartEnfeoff
            });  
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "combine_kingdom",
                path_icon = "ChineseCrown.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = delegate(Actor pActor)
                {
                    if (!pActor.isKing()) return false;
                    var allKingdoms = GetKingdomsRuledByActor(pActor);
                    if (!pActor.HasTitle()) return false;
                    if (ModClass.KINGDOM_TITLE_MANAGER.get(pActor.GetOwnedTitle()[0])?.title_capital?.kingdom?.king !=
                        pActor) return false;
                    return allKingdoms.Count > 1;
                },
                check_should_continue = delegate(Actor pActor)
                {
                    if (!pActor.isKing()) return false;
                    var allKingdoms = GetKingdomsRuledByActor(pActor);
                    if (!pActor.HasTitle()) return false;
                    return allKingdoms.Count > 1;
                },
                action = delegate(Actor pActor)
                {
                    if (!pActor.isKing()) return false;
                    
                    var allKingdoms = GetKingdomsRuledByActor(pActor);
                    if (allKingdoms.Count < 2) return false;

                    Kingdom mainKingdom = null;
                    
                    // 1. Prioritize Empire Core
                    var empireCore = allKingdoms.Find(k => k.IsInEmpire() && k.GetEmpire()?.CoreKingdom == k);
                    if (empireCore != null)
                    {
                        mainKingdom = empireCore;
                    }
                    else
                    {
                        // 2. Fallback to Primary Title
                        if (pActor.HasTitle())
                        {
                            var title = ModClass.KINGDOM_TITLE_MANAGER.get(pActor.GetOwnedTitle()[0]);
                            if (title?.title_capital?.kingdom?.king == pActor)
                            {
                                mainKingdom = title.title_capital.kingdom;
                            }
                        }
                        
                        // 3. Fallback to current kingdom
                        if (mainKingdom == null)
                        {
                            mainKingdom = pActor.kingdom;
                        }
                    }

                    if (mainKingdom == null) return false;

                    foreach (var kingdom in allKingdoms)
                    {
                        if (kingdom == mainKingdom) continue;
                        List<City> citiesSnapshot = new List<City>(kingdom.cities);
                        for (int i = 0; i < citiesSnapshot.Count; i++)
                        {
                            citiesSnapshot[i]?.joinAnotherKingdom(mainKingdom);
                        }
                    }
                    TranslateHelper.LogCombineKingdom(pActor);
                    return true;
                }
            });            
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "empire_plots",
                path_icon = "MinisterAcquireTitle.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = pActor => false,
                check_can_be_forced = (Actor pActor) => true,
                try_to_start_advanced = delegate(Actor pActor, PlotAsset pPlotAsset, bool pForced)
                {
                    var kingdom = pActor.kingdom;
                    var regime = kingdom?.GetRegime();
                    if (regime == null) return false;
                    var run = regime.GetDominateFaction()?.GetAnyTFactionRuns();
                    if (run == null) return false;
                    foreach (Plot plot3 in World.world.plots)
                    {
                        if (plot3.isActive() && plot3.isSameType(pPlotAsset))
                        {
                            pActor.setPlot(plot3);
                            plot3._plot_asset.progress_needed = run.progressMax - run.acceleration;
                            plot3.setName(run.type.ToString());
                            return true;
                        }
                    }
                    var newPlot = World.world.plots.newPlot(pActor, pPlotAsset, pForced);
                    newPlot._plot_asset.progress_needed = run.progressMax - run.acceleration;
                    newPlot.setName(run.type.ToString());
                    return true;
                },
                check_should_continue = delegate(Actor pActor)
                {
                    var kingdom = pActor.kingdom;
                    var regime = kingdom?.GetRegime();
                    if (regime == null) return false;
                    var run = regime.GetDominateFaction()?.GetAnyTFactionRuns();
                    if (run == null) return false;
                    if (!run.CheckContinue()) return false;
                    if (!run.IsStarted()) return false;
                    if (!run.CheckTarget()) return false;
                    return true;
                },
                action = delegate(Actor pActor)
                {
                    var kingdom = pActor.kingdom;
                    var regime = kingdom?.GetRegime();
                    var run = regime?.GetDominateFaction()?.GetAnyTFactionRuns();
                    if (run == null) return false;
                    run.Execute();
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
		    {
			    id = "force_stop_war",
			    is_basic_plot = true,
			    path_icon = "plots/icons/plot_stop_war",
			    group_id = "empirecraft_diplomacy",
			    min_level = 4,
			    money_cost = 0,
			    min_diplomacy = 8,
                show_for_unlockables_ui = false,
			    can_be_done_by_king = true,
			    check_target_war = true,
			    requires_diplomacy = false,
			    check_is_possible = (Actor pActor) => false,
			    try_to_start_advanced = delegate(Actor pActor, PlotAsset pPlotAsset, bool pForced)
			    {
				    Kingdom kingdom = pActor.kingdom;
				    War war = null;
                    var warsList1 = KingdomExtension.GetWarsCached(kingdom, true);
				    for (int wi = 0; wi < warsList1.Count; wi++)
				    {
                        var war3 = warsList1[wi];
					    if (war3.isAttacker(kingdom)&&war3.getDuration()>=ModClass.WAR_END_YEAR)
					    {
							war = war3;
							break;
					    }
				    }
				    if (war == null)
				    {
					    return false;
				    }
				    foreach (Plot plot3 in World.world.plots)
				    {
					    if (plot3.isActive() && plot3.isSameType(pPlotAsset) && plot3.target_war == war)
					    {
						    pActor.setPlot(plot3);
						    return true;
					    }
				    }
				    Plot plot = World.world.plots.newPlot(pActor, pPlotAsset, pForced);
				    plot.target_war = war;
				    if (!plot.checkInitiatorAndTargets())
				    {
					    Debug.Log("tryPlotStopWar is missing start requirements");
					    return true;
				    }
				    return true;
			    },
			    check_can_be_forced = (Actor pActor) => pActor.kingdom.hasEnemies() ? true : false,
			    check_should_continue = delegate(Actor pActor)
			    {
				    Plot plot = pActor.plot;
				    War targetWar = plot.target_war;
				    if (targetWar == null || targetWar.isRekt())
				    {
					    return false;
				    }
				    return !plot.target_war.hasEnded();
			    },
			    action = delegate(Actor pActor)
			    {
				    Plot plot = pActor.plot;
				    if (plot.target_war.hasEnded())
				    {
					    return false;
				    }
				    World.world.wars.endWar(plot.target_war, WarWinner.Peace);
				    return true;
			    }
		    });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "empire_move_back_to_capital",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.IsEmperor()) return false;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (empire.OriginalCapital == null) return false;
                    if (empire.OriginalCapital==kingdom.capital) return false;
                    if (empire.OriginalCapital.kingdom!=kingdom) return false;
                    return true;
                },
                check_should_continue = delegate (Actor actor)
                {
                    Kingdom kingdom = actor.kingdom;
                    if (!actor.IsEmperor()) return false;
                    if (!actor.isKing()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (empire.OriginalCapital == kingdom.capital) return false;
                    if (empire.OriginalCapital.kingdom != kingdom) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
                    kingdom.setCapital(empire.OriginalCapital);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_start_join_taken_alliance",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsInEmpire()) return false;
                    if (kingdom.HasTakenAlliance()) return false;
                    return FindTakenAllianceEmpireCandidate(kingdom, true) != null;
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = FindTakenAllianceEmpireCandidate(kingdom, false);
                    if (empire == null)
                    {
                        return false;
                    }
                    kingdom.JoinTakenAlliance(empire);
                    TranslateHelper.LogJoinTakenAlliance(kingdom, empire);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_start_invite_to_faction",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                min_renown_actor = 300,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (!pActor.HasFaction()) return false;
                    return empire.kingdoms_list.Any(k => ((!k.king?.HasFaction()) ?? false)&&(k.king?.renown??99999)<pActor.renown);
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    var empire = kingdom.GetEmpire();
                    if (!pActor.HasFaction()) return false;
                    var target = empire?.kingdoms_list?.Find(k => ((!k.king?.HasFaction()) ?? false)&&(k.king?.renown??99999)<pActor.renown);
                    if (target?.king == null)
                    {
                        return false;
                    }
                    target.king?.SetFaction(pActor.GetFaction());
                    pActor.data.renown -= target.king?.renown??0;
                    TranslateHelper.LogInviteIntoFaction(kingdom, target, pActor.GetFaction());
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_expose_crime",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 5,
                min_renown_actor = 200,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (!pActor.HasFaction()) return false;
                    return empire.kingdoms_list.Any(k => (k.king?.GetFaction()!=pActor.GetFaction())&&(((k.king?.renown??99999)/2)<pActor.renown)&&k.king.GetViolateValue()>=0&&k!=kingdom);
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    var empire = kingdom.GetEmpire();
                    if (!pActor.HasFaction()) return false;
                    var target = empire?.kingdoms_list?.Find(k => k.king?.GetFaction()!=pActor.GetFaction()&&((k.king?.renown??99999)/2)<pActor.renown&&k.king.GetViolateValue()>=0&&k!=kingdom);
                    if (target == null)
                    {
                        return false;
                    }
                    pActor.data.renown -= (target.king?.renown/2)??0;
                    target.king.AddTyrantValue(30);
                    if (target.king.GetViolateValue() >= 100)
                    {
                        var potentialCrimes = new List<LawType>()
                        {
                            LawType.叛国,
                            LawType.伪造货币,
                            LawType.滥用职权,
                            LawType.私通敌国,
                            LawType.玩忽职守,
                            LawType.走私
                        };
                        var crime = potentialCrimes.FindAll(c=>target.HasLaw(c)).GetRandom();
                        target.king.TryTriggerProbabilisticLaw(crime, 1f, _ => { });
                        double rebellingPossibility = 0.2f;
                        if (!target.isOpinionTowardsKingdomGood(empire.CoreKingdom))
                        {
                            rebellingPossibility = ((double)target.countTotalWarriors() / (double)empire.countWarriors()) * 0.5f + (0.5f*(double)(100.0f-empire.Mandate)/100.0f);
                        }
                        Random rand = new Random();
                        if (rand.NextDouble() < rebellingPossibility)
                        {
                            var war = DiplomacyHelpers.wars.newWar(target, kingdom, WarTypeLibrary.normal);
                            war.SetEmpireWarType(EmpireWarType.地方叛乱, pre: kingdom.name, nanoObject:empire, belongingFaction:target.king?.GetFaction());
                            empire.leave(target, true, true);
                            target.StartLocalRebelling(EmpireWarType.地方叛乱);
                        }
                        else
                        {
                            var context = target.king.TryEnforceLaw(crime, kingdom);
                            //当影响力小于两百, 或者脱罪次数达到2的上限则进行惩罚
                            if (target.king?.renown <= 200&&target.king.EscapeFromPunishment(true))
                            {
                                var punishments = new List<PunishmentLevel>()
                                {
                                    PunishmentLevel.剥夺官职, PunishmentLevel.剥夺爵位, PunishmentLevel.夷三族
                                };
                                context.AppliedPunishments = punishments;
                                foreach (var p in punishments)
                                {
                                    EmpireLawSystem.ApplyPunishment(context, p);
                                }
                                TranslateHelper.LogLawEnforcement(context);
                            }
                            else
                            {
                                target.king?.addRenown(-200);
                                EmpireLawSystem.ApplyPunishment(context, PunishmentLevel.无罪);
                                target.king.AddTyrantValue(-30);
                                context.AppliedPunishments = new List<PunishmentLevel>() {PunishmentLevel.无罪};
                                target.king.EscapeFromPunishment();
                            }
                            TranslateHelper.LogLawEnforcement(context);
                        }
                        TranslateHelper.LogExposeCrime(kingdom, target, crime.ToString());
                        kingdom.SetMainCrime(crime);
                    }
                    else
                    {
                        TranslateHelper.LogExposeCrime(kingdom, target, "");
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_start_religion_war",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (kingdom.GetKingdomType() != KingdomType.Feudalism_papal_state) return false;
                    if (regime.religion_point<1000) return false;
                    if (kingdom.hasEnemies()) return false;
                    var warsList2 = KingdomExtension.GetWarsCached(kingdom, false);
                    for (int wi = 0; wi < warsList2.Count; wi++)
                    {
                        if (warsList2[wi].GetEmpireWarType() == EmpireWarType.神圣) return false;
                    }
                    if (regime.type != RegimeType.Feudalism || regime.GetReligionLevel() != ReligionLevel.High)
                        return false;
                    if (kingdom.hasReligion() && kingdom.religion.GetCity() == kingdom.capital)
                    {
                        Religion religion = kingdom.religion;
                        
                        if (World.world.kingdoms.ToList().Any(k =>
                                !religion.kingdoms.Contains(k)&&religion.kingdoms.Any(k2=>k2.IsNeighbourWith(k)))) return true;
                    }
                    return false;
                },
                action = delegate (Actor pActor)
                {                    
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (regime.type != RegimeType.Feudalism || regime.GetReligionLevel() != ReligionLevel.High)
                        return false;
                    if (kingdom.hasReligion() && kingdom.religion.GetCity() == kingdom.capital)
                    {
                        Religion religion = kingdom.religion;

                        var target = World.world.kingdoms.ToList().Find(k =>
                            !religion.kingdoms.Contains(k)&&religion.kingdoms.Any(k2=>k2.IsNeighbourWith(k)));
                        if (target == null) return false;
                        var war = DiplomacyHelpers.wars.newWar(kingdom, target, WarTypeLibrary.normal);
                        war.SetEmpireWarType(EmpireWarType.神圣, pre:religion.name);
                        regime.religion_point -= 1000;
                        return true;
                    }
                    return false;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "powerful_minister_replace_empire",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 60f,
                can_be_done_by_leader = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if(!pActor.isOfficer()) return false;
                    if (pActor.IsEmperor()) return false;
                    if (empire.Emperor == null) return false;
                    if (!empire.Emperor.isUnitFitToRule()) return false;
                    if (pActor.GetIdentity().officialLevel !=  1) return false;
                    if (!pActor.HasTitle()) return false;
                    if (pActor.renown < empire.Emperor.renown) return false; 
                    return true;
                },
                check_should_continue = delegate (Actor actor)
                {
                    if (!actor.isOfficer()) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = kingdom.GetEmpire();
                    kingdom.setKing(pActor);
                    pActor.setKingdom(kingdom);
                    pActor.setCity(empire.CoreKingdom.capital);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "new_empire_royal",
                path_icon = "ChineseCrown.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 30,
                progress_needed = 60f,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (!pActor.HasSpecificClan()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (kingdom.hasEnemies()) return false;
                    if (!kingdom.GetEmpire().IsRoyalBeenChanged()) return false;
                    if (Date.getYearsSince(kingdom.GetEmpire().data.original_royal_been_changed_timestamp)<=5) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    pActor.CheckSpecificClan();
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = kingdom.GetEmpire();
                    empire.data.original_royal_been_changed = false;
                    empire.data.empire_specific_clan = pActor.GetSpecificClan().id;
                    if (ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out string culture))
                    {
                        if (culture == "Huaxia")
                        {
                            string new_name = pActor.generateName(MetaType.Kingdom, IdGenerator.NextId());
                            LogService.LogInfo($"New Empire Name：{new_name}，Original Empire Name：{empire.GetEmpireName()}");
                            empire.SetEmpireName(new_name.Split('\u200A')[0].Split(' ').Last());
                            LogService.LogInfo($"Empire Name has been changed to：{empire.GetEmpireName()}");
                            empire.data.currentHistory.is_first = true;
                            empire.data.currentHistory.empire_name = empire.GetEmpireName();
                        }
                    }
                    pActor.GetSpecificClan()?.RecordHistoryEmpire(empire, empire.CoreKingdom.capital);
                    empire.data.created_time = World.world.getCurWorldTime();
                    empire.CoreKingdom.setKing(pActor);
                    if (empire.HasYearName())
                    {
                        empire.RecordHistory(EmpireHistoryType.new_empire_history, new Dictionary<string, string>()
                        {
                            ["actor"] = pActor.getName(),
                            ["place"] = empire.CoreKingdom.capital.GetCityName(),
                            ["name"] = empire.GetEmpireName(),
                        });
                    }
                    else
                    {
                        empire.RecordHistory(EmpireHistoryType.new_empire_history_west, new Dictionary<string, string>()
                        {
                            ["actor"] = pActor.getName(),
                            ["place"] = empire.CoreKingdom.capital.GetCityName(),
                            ["name"] = empire.GetEmpireName(),
                        });
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "emperor_year_name",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpire().IsAllowToMakeYearName()) return false;
                    if (kingdom.GetEmpire().HasYearName()) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.GetEmpire().create_year_name();
                    TranslateHelper.LogNewEmperor(pActor, kingdom.capital, kingdom.GetEmpire().data.year_name);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_allow_army",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (empire.Mandate > 60) return false;
                    if (regime == null) return false;
                    if (regime.IsAllowArmy()) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetAllowArmy(true);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_allow_diplomacy",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (empire.Mandate > 60) return false;
                    if (regime == null) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (regime.IsAllowDiplomacy()) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetAllowDiplomacy(true);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_allow_succession",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_allow_self_army",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;
                    if (!regime.IsAllowSupportCenterArmy()) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetAllowSupportCenterArmy(false);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_allow_independent",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (empire.Mandate > 40) return false;
                    if (regime == null) return false;
                    if (regime.type != RegimeType.Feudalism && regime.type != RegimeType.Arabic) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;
                    if (regime.IsAllowSupportCenterArmy()) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.FinishedSelfPlot();
                    Empire empire = kingdom.GetEmpire();
                    if (empire.isRekt()) return false;
                    empire.leave(kingdom);
                    var war = DiplomacyHelpers.wars.newWar(kingdom, empire.CoreKingdom, WarTypeLibrary.normal);
                    war.SetEmpireWarType(EmpireWarType.地方独立);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "emperor_posthumous_name",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpire().IsNeedToSetPosthumous()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    if (!pActor.isKing()) return false;
                    if (!pActor.hasKingdom()) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isAlive()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (kingdom.GetEmpire().Emperor == null) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    foreach (EmpireCraftHistory cHistory in kingdom.GetEmpire().data.history)
                    {
                        Actor actor =  World.world.units.get(cHistory.id);
                        if (!string.IsNullOrEmpty(cHistory.emperor))
                        {
                            if (actor != null)
                            {
                                if (actor.isAlive()) continue;
                            }
                            if (string.IsNullOrEmpty(cHistory.shihao_name))
                            {
                                bool isFirst = false;
                                bool isLast = false;
                                bool isGood = true;
                                {
                                    isFirst = cHistory.is_first;
                                }
                                var names = PosthumousNameGenerator.GenerateBoth(kingdom.GetEmpire(), 1, isFirst, isLast, isGood);
                                cHistory.shihao_name = names.shi;
                                cHistory.miaohao_name = names.miao.pre;
                                cHistory.miaohao_suffix = names.miao.suf;
                                kingdom.GetEmpire().RecordHistory(EmpireHistoryType.give_posthumous_to_previous_emperor_history, new Dictionary<string, string>
                                {
                                    ["actor"] = kingdom.GetEmpire().Emperor.data.name,
                                    ["actor2"] = cHistory.emperor,
                                    ["shihao"] = LM.Get(cHistory.shihao_name),
                                    ["miaohao"] = LM.Get(cHistory.miaohao_name) + LM.Get(cHistory.miaohao_suffix)
                                });
                                TranslateHelper.LogEmpeorNamingPreviousEmperor(pActor, cHistory.emperor);
                            }
                            //追封
                        }                    
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "king_acquire_title",
                path_icon = "TitleAcquire.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!pActor.CanAcquireTitle()) return false;
                    var warsList3 = kingdom.GetWarsCached(false);
                    if (warsList3.Count > 0) return false;
                    Regime regime = kingdom.GetRegime();
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (kingdom.IsEmpire()) return false;
                    return true;
                },
                try_to_start_advanced = delegate(Actor pActor, PlotAsset pPlotAsset, bool pForced)
                {
                    Kingdom kingdom = pActor.kingdom;
                    List<KingdomTitle> titles = pActor.getAcquireTitle();
                    if (!titles.Any()) return false;
                    foreach (KingdomTitle title in titles)
                    {
                        if (title.isRekt()) continue;
                        foreach(City city in title.city_list)
                        {
                            if (!kingdom.cities.Contains(city))
                            {
                                Kingdom targetKingdom = city.kingdom;
                                if (!targetKingdom.hasKing()) continue;
                                if (targetKingdom.isNeutral()) continue;
                                if (!targetKingdom.king.GetOwnedTitle().Contains(title.getID())) continue;
                                if (kingdom.isOpinionTowardsKingdomGood(targetKingdom)) continue;
                                if (kingdom.countTotalWarriors() > targetKingdom.countTotalWarriors())
                                {
                                    foreach (Plot plot3 in World.world.plots)
                                    {
                                        if (plot3.isActive() && plot3.isSameType(pPlotAsset))
                                        {
                                            pActor.setPlot(plot3);
                                            plot3.target_kingdom = targetKingdom;
                                            plot3.setName($"{kingdom.name}试图索取{targetKingdom.name}的{title.name}法理");
                                            return true;
                                        }
                                    }
                                    var nPlot = World.world.plots.newPlot(pActor, pPlotAsset, pForced);
                                    nPlot.target_kingdom = targetKingdom;
                                    nPlot.setName($"{kingdom.name}试图索取{targetKingdom.name}的{title.name}法理");
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
                },
                check_should_continue = delegate (Actor pActor) {
                    if (!pActor.hasPlot()) return false;
                    if (!pActor.hasKingdom()) return false;
                    var plot = pActor.plot;
                    var targetKingdom = plot.target_kingdom;
                    if (targetKingdom.isRekt()) return false;
                    if (!targetKingdom.hasKing()) return false;
                    return true;
                },
                action = delegate(Actor pActor)
                {
                    if (!pActor.hasPlot()) return false;
                    if (!pActor.hasKingdom()) return false;
                    var kingdom = pActor.kingdom;
                    var plot = pActor.plot;
                    var targetKingdom = plot.target_kingdom;
                    List<KingdomTitle> titles = pActor.getAcquireTitle();
                    var needTitles = targetKingdom.king.GetOwnedTitle().Select(tid=>ModClass.KINGDOM_TITLE_MANAGER.get(tid)).Intersect(titles).ToList();
                    if (needTitles.Count <= 0) return false;
                    var finalTitle = needTitles.ToList().Find(t => !t.isRekt());
                    if (finalTitle == null) return false;
                    War war = World.world.diplomacy.startWar(kingdom, targetKingdom, WarTypeLibrary.normal);
                    war.SetEmpireWarType(EmpireWarType.索取法理, nanoObject:finalTitle);
                    TranslateHelper.LogKingdomAcquireTitle(kingdom, targetKingdom, finalTitle);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_destroy_title",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (ModClass.KINGDOM_TITLE_FREEZE) return false;
                    if (!kingdom.HasMainTitle()) return false;
                    if (!pActor.titleCanBeDestroy().Any()) return false;
                    return true;
                },

                check_should_continue = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isAlive()) return false;
                    if (!kingdom.HasMainTitle()) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    List<KingdomTitle> titles = pActor.titleCanBeDestroy();
                    foreach(KingdomTitle title in titles)
                    {

                        ModClass.KINGDOM_TITLE_MANAGER.dissolveTitle(title);
                        pActor.removeTitle(title);
                        if (kingdom.GetMainTitle()==title)
                        {
                            kingdom.RemoveMainTitle();
                        }
                        TranslateHelper.LogDestroyTitle(kingdom, title);
                    }
                    if (kingdom.HasMainTitle())
                    {
                        KingdomTitle title = kingdom.GetMainTitle();
                        foreach(City city in kingdom.cities)
                        {
                            if (city.GetTitle()!=kingdom.GetMainTitle())
                            {
                                title.addCity(city);
                                TranslateHelper.LogCityAddToTitle(city, title);
                            }
                        }
                    }else
                    {
                        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(kingdom.capital);
                        kingdom.SetMainTitle(title);
                        foreach (City city in kingdom.cities)
                        {
                            if (city!=kingdom.capital)
                            {
                                title.addCity(city);
                                TranslateHelper.LogCityAddToTitle(city, title);
                            }
                        }
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_get_title",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    if (pActor == null) return false;
                    if (!pActor.isKing()) return false;
                    if (!pActor.canTakeTitle()) return false;
                    var warsList4 = pActor.kingdom.GetWarsCached(false);
                    if (warsList4.Count > 0) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (kingdom.isRekt()) return false;
                    if (kingdom.IsInEmpire())
                    {
                        var empire = kingdom.GetEmpire();
                        return (empire?.Mandate??0) <= 50;
                    }
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    List<KingdomTitle> titles = pActor.takeTitle();
                    foreach(KingdomTitle title in titles)
                    {
                        if (!title.isRekt())
                        {
                            TranslateHelper.LogKingTakeTitle(kingdom, title);
                        }
                    }
                    return true;
                }
            }); 
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_change_capital_title",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    if (pActor == null) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.HasMainTitle()) return false;
                    if (kingdom.GetMainTitle()==null) return false;
                    if (kingdom.GetMainTitle().title_capital==null) return false;
                    if (kingdom.capital == null) return false;
                    if (pActor.kingdom.GetMainTitle().title_capital==kingdom.capital) return false;
                    if (!kingdom.cities.Contains(pActor.kingdom.GetMainTitle().title_capital)) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.setCapital(kingdom.GetMainTitle().title_capital);
                    TranslateHelper.LogKingdomChangeCapitalToTitle(kingdom, kingdom.GetMainTitle());
                    var emp = kingdom.GetEmpire();
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_join_empire",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    if (pActor == null) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (kingdom == null) return false;
                    if (!pActor.isKing()) return false;
                    if (kingdom.HasMainTitle()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpiresCanBeJoined().Any()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    if (pActor == null) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpiresCanBeJoined().Any()) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.GetEmpiresCanBeJoined().First().join(kingdom);
                    var warsList5 = KingdomExtension.GetWarsCached(kingdom, false);
                    for (int wi = 0; wi < warsList5.Count; wi++)
                    {
                        warsList5[wi].lostWar(kingdom);
                    }
                    TranslateHelper.LogKingdomJoinEmpire(kingdom, kingdom.GetEmpire());
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_create_title",
                path_icon = "EmperorQuest.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (kingdom.capital==null) return false;
                    if (!pActor.isKing()) return false;
                    if (!pActor.hasKingdom()) return false; 
                    if (kingdom.capital.hasTitle()) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(kingdom.capital);
                    TranslateHelper.LogCreateTitle(kingdom, title);
                    title.owner = pActor;
                    pActor.AddOwnedTitle(title);
                    var emp = kingdom.GetEmpire();
                    foreach(City c in kingdom.cities)
                    {
                        if (!c.hasTitle())
                        {
                            title.addCity(c);
                            TranslateHelper.LogCityAddToTitle(c, title);
                            var emp2 = kingdom.GetEmpire();
                        }
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "minister_acquire_empire",
                path_icon = "ministerAcquireEmpire.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                money_cost = 30,
                progress_needed = 30f,
                can_be_done_by_king = true,
                requires_diplomacy = true,
                check_is_possible = delegate (Actor pActor)
                {
                    if (pActor==null) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (!DiplomacyHelpers.isWarNeeded(kingdom)) return false;
                    if (!pActor.isKing()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (!pActor.HasTitle() || (!pActor.HasSpecificClan() || pActor.GetSpecificClan().id != kingdom.GetEmpire().EmpireSpecificClan.id)) return false;
                    LogService.LogInfo("权臣索取帝国错误");
                    if (kingdom.countTotalWarriors()<kingdom.GetEmpire().countWarriors()- kingdom.countTotalWarriors()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isAlive()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (kingdom.GetEmpire().Emperor == null) return false;
                    return true;
                },
                action = minister_acquire_empire
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "minister_acquire_title",
                path_icon = "ministerAcquireTitle.png",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 30f,
                can_be_done_by_leader = true,
                can_be_done_by_king = true,
                requires_diplomacy = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!DiplomacyHelpers.isWarNeeded(kingdom)) return false;
                    if (!pActor.isKing()&&!pActor.isOfficer()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (pActor.HasTitle()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire.Emperor == null) return false; 
                    if (empire.Emperor.GetOwnedTitle().Count()<=1) return false; 
                    if (pActor.GetPeeragesLevel()==PeeragesLevel.peerages_2) return false;
                    if (pActor.GetIdentity() == null) return false;
                    if (pActor.GetIdentity().officialLevel!= 1) return false;

                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isAlive()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (kingdom.GetEmpire().Emperor == null) return false;
                    return true;
                },
                action = delegate (Actor pActor) 
                {
                    Empire empire = pActor.kingdom.GetEmpire();
                    City city = pActor.city;
                    
                    foreach(long title_id in empire.Emperor.GetOwnedTitle())
                    {
                        KingdomTitle kingdomTitle = ModClass.KINGDOM_TITLE_MANAGER.get(title_id);
                        if (empire.CoreKingdom.GetMainTitle()!= kingdomTitle)
                        {
                            pActor.AddOwnedTitle(kingdomTitle);
                            pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_2);
                            TranslateHelper.LogPowerfulMinisterAcquireTitle(pActor, pActor.kingdom.GetEmpire(), kingdomTitle.data.name + LM.Get("King"));
                            return true;
                        }
                    }
                    return false;
                }
            });
            AssetManager.plots_library.list.RemoveAll(a => a.id == "new_war");
            AssetManager.plots_library.basic_plots.RemoveAll(a=>a.id=="new_war");
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "empirecraft_war",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_new_war",
                group_id = "empirecraft_diplomacy",
                min_level = 3,
                min_warfare = 6,
                money_cost = 20,
                min_renown_kingdom = 50,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                requires_diplomacy = true,
                unlocked_with_achievement = false,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Regime regime = kingdom.GetRegime();
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (!DiplomacyHelpers.isWarNeeded(kingdom))
                    {
                        return false;
                    }
                    if (kingdom.IsEmpire()) return false;
                    if (pActor.hasCulture() && pActor.culture.hasTrait("serenity_now"))
                    {
                        return false;
                    }
                    if (pActor.hasTrait("pacifist"))
                    {
                        return false;
                    }
                    if (kingdom.hasAlliance())
                    {
                        foreach (Kingdom item in kingdom.getAlliance().kingdoms_hashset)
                        {
                            if (item != kingdom && item.hasKing())
                            {
                                Actor king = item.king;
                                if (king.hasPlot() && king.plot.isSameType(PlotsLibrary.new_war))
                                {
                                    return false;
                                }
                            }
                        }
                    }
                    if (kingdom.IsInEmpire()&&!kingdom.IsEmpire())
                    {
                        if (!kingdom?.GetEmpire()?.IsAllowToMakeWar()??false)
                        {
                            return false;
                        }
                    }
                    if (kingdom.GetMoney() < 200) return false;
                    return true;
                },
                try_to_start_advanced = delegate (Actor pActor, PlotAsset pPlotAsset, bool pForced)
                {
  
                    Kingdom kingdom = pActor.kingdom;
                    var warTarget = getWarTarget(kingdom);
                    if (warTarget == null)
                    {
                        return false;
                    }
                    if (warTarget.IsInSameEmpire(pActor.kingdom))
                    {
                        Empire empire = warTarget.GetEmpire();
                        if (!empire.IsAllowToMakeWar()&&warTarget==empire.CoreKingdom)
                        {
                            return false;
                        }
                    }

                    if (kingdom.IsInEmpire())
                    {
                        if (warTarget.GetGivenAllianceEmpire() == kingdom.GetEmpire())
                        {
                            return false;
                        }

                        if (warTarget.GetTakenAllianceEmpire() == kingdom.GetEmpire())
                        {
                            return false;
                        }
                    }
                    if (kingdom.HasGivenAlliance())
                    {
                        if (warTarget == kingdom.GetGivenAllianceEmpire()?.CoreKingdom)
                        {
                            if (!kingdom.NeedToRemoveGivenAlliance()) return false;
                        }
                    }
                    if (!kingdom.IsNeighbourWith(warTarget))
                    {
                        return false;
                    }
                    Plot plot = World.world.plots.newPlot(pActor, pPlotAsset, pForced);
                    plot.target_kingdom = warTarget;
                    return plot.checkInitiatorAndTargets();
                },
                check_should_continue = delegate (Actor pActor)
                {
                    Plot plot = pActor.plot;
                    if (!plot.target_kingdom.isAlive())
                    {
                        return false;
                    }
                    if (pActor.kingdom.hasAlliance() && pActor.kingdom.getAlliance() == plot.target_kingdom.getAlliance())
                    {
                        return false;
                    }
                    return DiplomacyHelpers.isWarNeeded(pActor.kingdom);
                },
                action = delegate (Actor pActor)
                {
                    World.world.diplomacy.startWar(pActor.kingdom, pActor.plot.target_kingdom, WarTypeLibrary.normal);
                    pActor.kingdom.SubMoney(200);
                    return true;
                }
            });

            PlotsLibrary.alliance_create.check_is_possible = delegate (Actor pActor)
            {
                Kingdom kingdom = pActor.kingdom;
                if (kingdom.hasAlliance())
                {
                    return false;
                }
                if (kingdom.IsInEmpire())
                {
                    return false;
                }
                if (kingdom.hasEnemies())
                {
                    return false;
                }
                if (kingdom.isSupreme())
                {
                    return false;
                }
                if (Date.getYearsSince(kingdom.data.timestamp_alliance) < SimGlobals.m.alliance_timeout)
                {
                    return false;
                }
                return !World.world.plots.isPlotTypeAlreadyRunning(pActor, PlotsLibrary.alliance_create);
            };
            AssetManager.plots_library.list.RemoveAll(a => a.id == "alliance_join");
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "alliance_join",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_alliance_create",
                group_id = "empirecraft_diplomacy",
                min_level = 2,
                money_cost = 5,
                min_diplomacy = 5,
                min_renown_kingdom = 50,
                can_be_done_by_king = true,
                check_target_alliance = true,
                requires_diplomacy = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (kingdom.isSupreme())
                    {
                        return false;
                    }
                    if (kingdom.hasAlliance())
                    {
                        return false;
                    }
                    if (kingdom.IsInEmpire())
                    {
                        return false;
                    }
                    if (kingdom.hasEnemies())
                    {
                        return false;
                    }
                    if (Date.getYearsSince(kingdom.data.timestamp_alliance) < SimGlobals.m.alliance_timeout)
                    {
                        return false;
                    }
                    return World.world.alliances.anyAlliances() ? true : false;
                },
                try_to_start_advanced = delegate (Actor pActor, PlotAsset pPlotAsset, bool pForced)
                {
                    Kingdom kingdom = pActor.kingdom;
                    _ = kingdom.power;
                    Alliance alliance = null;
                    foreach (var item3 in World.world.alliances.list.LoopRandom())
                    {
                        if (!item3.hasWars())
                        {
                            if (pForced)
                            {
                                alliance = item3;
                                break;
                            }

                            if (!item3.canJoin(kingdom) || item3.hasSupremeKingdom()) continue;
                            _ = item3.power;
                            var flag = kingdom.cities.Count <= 2 && !kingdom.hasNearbyKingdoms();
                            if (!flag && item3.hasSharedBordersWithKingdom(kingdom))
                            {
                                flag = true;
                            }
                            if (flag)
                            {
                                alliance = item3;
                            }
                        }
                    }
                    if (alliance == null)
                    {
                        return false;
                    }
                    Plot plot = World.world.plots.newPlot(pActor, pPlotAsset, pForced);
                    plot.target_alliance = alliance;
                    if (!plot.checkInitiatorAndTargets())
                    {
                        Debug.Log("tryPlotJoinAlliance is missing start requirements");
                        return true;
                    }
                    pActor.setPlot(plot);
                    return true;
                },
                check_other_plots = PlotsLibrary.alliance_create.check_other_plots,
                check_can_be_forced = (Actor pActor) => !pActor.kingdom.hasAlliance(),
                check_should_continue = delegate(Actor pActor)
                {
                    Plot plot = pActor.plot;
                    if (pActor.kingdom.hasAlliance())
                    {
                        return false;
                    }
                    if (!plot.target_alliance.isAlive())
                    {
                        return false;
                    }
                    if (!plot.target_alliance.canJoin(pActor.kingdom))
                    {
                        return false;
                    }
                    if (pActor.kingdom.hasEnemies())
                    {
                        return false;
                    }
                    return !plot.target_alliance.hasWars();
                },
                action = delegate(Actor pActor)
                {
                    Plot plot = pActor.plot;
                    if (pActor.kingdom.hasAlliance())
                    {
                        return false;
                    }
                    if (!plot.target_alliance.isAlive())
                    {
                        return false;
                    }
                    plot.target_alliance.join(pActor.kingdom);
                    if (pActor.kingdom.hasKing())
                    {
                        pActor.kingdom.king.leavePlot();
                    }
                    return true;
                }
            });
            GuardAllPlotAssets();
            LogService.LogInfo($"Currently loaded{AssetManager.plots_library.getList().Count().ToString()} plots");
            AssetManager.plots_library.linkAssets();
        }

        private static bool minister_acquire_empire(Actor pActor)
        {
            if (pActor == null)
            {
                return false;
            }

            Kingdom kingdom = pActor.kingdom;
            Empire empire = kingdom?.GetEmpire();
            Kingdom coreKingdom = empire?.CoreKingdom;
            if (kingdom == null || empire == null || coreKingdom == null || !kingdom.isAlive() || !coreKingdom.isAlive())
            {
                return false;
            }

            new WorldLogMessage(EmpireCraftWorldLogLibrary.minister_try_aqcuire_empire_log, pActor.GetTitle() ?? "", pActor.data?.name ?? "", empire.data?.name ?? "")
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = coreKingdom.getColor()._color_text
            }.add();

            int ownedCities = kingdom.countCities();
            int empireCities = empire.countCities();
            int otherCities = empireCities - ownedCities;
            if (otherCities <= 0 || (float)ownedCities / (float)otherCities >= 4f)
            {
                empire.ReplaceEmpire(kingdom);
            } 
            else
            {
                War war = World.world?.diplomacy?.startWar(kingdom, coreKingdom, WarTypeLibrary.normal);
                if (war != null)
                {
                    war.SetEmpireWarType(EmpireWarType.获取帝国);
                }
            }
            return true;
        }
        public static bool BecomeEmpireAndStartEnfeoff(Actor pActor)
        {
            Kingdom kingdom = pActor?.kingdom;
            if (kingdom == null || !kingdom.isAlive())
            {
                return false;
            }

            Empire empire = ModClass.EMPIRE_MANAGER?.NewEmpire(kingdom);
            if (empire == null)
            {
                return false;
            }

            if (kingdom.hasAlliance())
            {
                Alliance alliance = kingdom.getAlliance();
                if (alliance?.kingdoms_hashset == null)
                {
                    return true;
                }

                foreach (Kingdom kingdom1 in alliance.kingdoms_hashset.ToList()) 
                {
                    if (kingdom1 == null || !kingdom1.isAlive())
                    {
                        continue;
                    }

                    kingdom1.SetIndependentValue(50);
                    empire.join(kingdom1);
                }
            }
            return true;
        }

        public static Kingdom getWarTarget(Kingdom pInitiatorKingdom)
        {
            if (pInitiatorKingdom == null || !pInitiatorKingdom.isAlive() || World.world?.kingdoms == null)
            {
                return null;
            }

            Kingdom result = null;
            Empire empire = pInitiatorKingdom.GetEmpire();
            int initiatorWarriors = pInitiatorKingdom.countTotalWarriors();
            foreach (Kingdom tKingdom in World.world.kingdoms)
            {
                if (tKingdom == null || tKingdom == pInitiatorKingdom || !tKingdom.isAlive())
                {
                    continue;
                }

                int targetWarriors = tKingdom.countTotalWarriors();
                bool isNeighbour = pInitiatorKingdom.IsInEmpire() ? empire != null && empire.IsNeighbourWith(tKingdom) : pInitiatorKingdom.IsNeighbourWith(tKingdom);
                if (!tKingdom.IsInSameEmpire(pInitiatorKingdom) && !pInitiatorKingdom.isOpinionTowardsKingdomGood(tKingdom) && initiatorWarriors > targetWarriors && isNeighbour)
                {
                    result = tKingdom;
                    break;
                }
            }
            if (result == null)
            {
                Kingdom target = pInitiatorKingdom.FindClosestKingdom();
                if (target != null && target.isAlive() && UnityEngine.Vector3.Distance(pInitiatorKingdom.location, target.location) < 300f)
                {
                    if (initiatorWarriors > target.countTotalWarriors())
                    {
                        result = target;
                    }
                }
            }
            return result;
        }

    }
}
