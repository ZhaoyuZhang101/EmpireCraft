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
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using UnityEngine;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;
using static System.Collections.Specialized.BitVector32;

namespace EmpireCraft.Scripts.AI
{
    public static class EmpireCraftPlotsAddition
    {
        public static void init()
        {
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
                    if (!run.IsStarted()) return false;
                    if (!run.CheckTarget()) return false;
                    return true;
                },
                action = delegate(Actor pActor)
                {
                    var kingdom = pActor.kingdom;
                    var regime = kingdom?.GetRegime();
                    if (regime == null) return false;
                    var run = regime.GetDominateFaction()?.GetAnyTFactionRuns();
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
				    foreach (War war3 in kingdom.getWars(pRandom: true))
				    {
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
                    return ModClass.EMPIRE_MANAGER
                        .Where(empire => empire.IsNeighbourWith(kingdom)&&kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom))
                        .Any(empire => kingdom.countTotalWarriors() < empire.countWarriors());
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = ModClass.EMPIRE_MANAGER.Where(empire => empire.IsNeighbourWith(kingdom)).ToList()
                        .Find(empire => kingdom.countTotalWarriors() < empire.countWarriors());
                    kingdom.JoinTakenAlliance(empire);
                    TranslateHelper.LogJoinTakenAlliance(kingdom, empire);
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
                    if (kingdom.getWars().Any(w => w.GetEmpireWarType() == EmpireWarType.神圣)) return false;
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
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!pActor.CanAcquireTitle()) return false;
                    if (kingdom.getWars().Any()) return false;
                    Regime regime = kingdom.GetRegime();
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (kingdom.IsEmpire()) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    List<KingdomTitle> titles = pActor.getAcquireTitle();
                    if (!titles.Any()) return false;
                    foreach (KingdomTitle title in titles)
                    {
                        foreach(City city in title.city_list)
                        {
                            if (!kingdom.cities.Contains(city))
                            {
                                Kingdom targetKingdom = city.kingdom;
                                if (kingdom.countTotalWarriors() > targetKingdom.countTotalWarriors())
                                {
                                    War war = World.world.diplomacy.startWar(kingdom, targetKingdom, WarTypeLibrary.normal);
                                    TranslateHelper.LogKingdomAcquireTitle(kingdom, targetKingdom, title);
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
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
                    if (pActor.kingdom.getWars().Any()) return false;
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
                    kingdom.getWars().ForEach(war => war.lostWar(kingdom));
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
                    foreach(City c in kingdom.cities)
                    {
                        if (!c.hasTitle())
                        {
                            title.addCity(c);
                            TranslateHelper.LogCityAddToTitle(c, title);
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
            LogService.LogInfo($"Currently loaded{AssetManager.plots_library.getList().Count().ToString()} plots");
            AssetManager.plots_library.linkAssets();
        }

        private static bool minister_acquire_empire(Actor pActor)
        {

            new WorldLogMessage(EmpireCraftWorldLogLibrary.minister_try_aqcuire_empire_log, pActor.GetTitle(), pActor.data.name, pActor.kingdom.GetEmpire().data.name)
            {
                color_special1 = pActor.kingdom.getColor()._color_text,
                color_special2 = pActor.kingdom.GetEmpire().CoreKingdom.getColor()._color_text
            }.add();

            if ((float)pActor.kingdom.countCities() / (float)(pActor.kingdom.GetEmpire().countCities()- pActor.kingdom.countCities())>=4)
            {
                pActor.kingdom.GetEmpire().ReplaceEmpire(pActor.kingdom);
            } 
            else
            {
                War war = World.world.diplomacy.startWar(pActor.kingdom, pActor.kingdom.GetEmpire().CoreKingdom, WarTypeLibrary.normal);
                if (war != null)
                {
                    war.SetEmpireWarType(EmpireWarType.获取帝国);
                }
            }
            return true;
        }
        public static bool BecomeEmpireAndStartEnfeoff(Actor pActor)
        {
            Kingdom kingdom = pActor.kingdom;
            Empire empire = ModClass.EMPIRE_MANAGER.NewEmpire(kingdom);
            if (kingdom.hasAlliance())
            {
                foreach (Kingdom kingdom1 in kingdom.getAlliance().kingdoms_hashset) 
                {
                    kingdom1.SetIndependentValue(50);
                    empire.join(kingdom1);
                }
            }
            return true;
        }

        public static Kingdom getWarTarget(Kingdom pInitiatorKingdom)
        {
            if (pInitiatorKingdom == null) { return null; }
            Kingdom result = null;
            Empire empire = pInitiatorKingdom.GetEmpire();
            float num = float.MaxValue;
            int num2 = pInitiatorKingdom.countTotalWarriors();
            foreach(Kingdom tKingdom in World.world.kingdoms)
            {
                if (tKingdom == null) continue;
                if (!tKingdom.isAlive()) continue;
                num = tKingdom.countTotalWarriors();
                bool flag = pInitiatorKingdom.IsInEmpire() ? pInitiatorKingdom.GetEmpire().IsNeighbourWith(tKingdom) : pInitiatorKingdom.IsNeighbourWith(tKingdom);
                if (!tKingdom.IsInSameEmpire(pInitiatorKingdom)&&!pInitiatorKingdom.isOpinionTowardsKingdomGood(tKingdom)&&num2>num&&flag)
                {
                    result = tKingdom;
                    break;
                }
            }
            if (result==null)
            {
                Kingdom target = pInitiatorKingdom.FindClosestKingdom();
                if (target != null)
                {
                    if (num2 > target.countTotalWarriors())
                    {
                        result = target;
                    }
                }
            }
            return result;
        }

    }
}
