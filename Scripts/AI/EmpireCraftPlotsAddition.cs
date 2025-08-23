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
using UnityEngine;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;
using static System.Collections.Specialized.BitVector32;

namespace EmpireCraft.Scripts.AI
{
    public static class EmpireCraftPlotsAddition
    {
        public static void init()
        {
            // AssetManager.plots_library.add(new PlotAsset
            // {
            //     id = "become_empire",
            //     path_icon = "ChineseCrown.png",
            //     group_id = "diplomacy",
            //     is_basic_plot = true,
            //     min_level = 1,
            //     money_cost = 30,
            //     priority = 999,
            //     progress_needed = 60f,
            //     can_be_done_by_king = true,
            //     check_is_possible = delegate (Actor pActor)
            //     {
            //         Kingdom kingdom = pActor.kingdom;
            //         if (!pActor.isKing()) return false;
            //         if (kingdom.isEmpire()) return false;
            //         if (kingdom.isInEmpire()) return false;
            //         if (!kingdom.HasMainTitle()) return false; //if a kingdom has main title then it could become an empire
            //         ModClass.EMPIRE_MANAGER.update(-1L);
            //         if (!kingdom.canBecomeEmpire()) return false;
            //         return true;
            //     },
            //     action = BecomeEmpireAndStartEnfeoff
            // });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "province_change_to_kingdom",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_leader = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isOfficer()) return false;
                    if (pActor.isKing()) return false;
                    Province province = ModClass.PROVINCE_MANAGER.get(pActor.GetProvinceID());
                    if (province == null) return false;
                    if (!province.needToBecomeKingdom()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor actor)
                {
                    if (!actor.isOfficer()) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    if (!pActor.isOfficer()) return false;
                    Province province = ModClass.PROVINCE_MANAGER.get(pActor.GetProvinceID());
                    province.becomeKingdom();
                    province.occupied_cities.Clear();
                    if (pActor.data.renown>=20)
                    {
                        pActor.data.renown -= 20;
                    }
                    
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "empire_move_back_to_capital",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.IsEmperor()) return false;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.isEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
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
                id = "emperor_take_armed_province",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if(!pActor.IsEmperor()) return false;
                    if (!pActor.isKing()) return false;
                    Empire empire = pActor.kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (!empire.CanTakeArmedProvince()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor actor)
                {
                    if (!actor.IsEmperor()) return false;
                    if (!actor.isKing()) return false;
                    Empire empire = actor.kingdom.GetEmpire();
                    if (!empire.CanTakeArmedProvince()) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null ) return false;
                    ModClass.EMPIRE_MANAGER.update(-1L);
                    foreach(Kingdom kingdom2 in empire.kingdoms_hashset)
                    {
                        if (kingdom.isRekt()) continue;
                        if (!kingdom2.isBorder()&& empire.Emperor.renown >= kingdom2.king.renown*2 && kingdom.countTotalWarriors() >= empire.countWarriors() / 5)
                        {
                            kingdom2.ChangeToProvince(empire);
                            empire.Emperor.addRenown(-kingdom2.king.renown);
                        }
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "powerful_minister_controlled_empire",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 30f,
                can_be_done_by_leader = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if(!pActor.isOfficer()) return false;
                    if (pActor.IsEmperor()) return false;
                    if (empire.Emperor == null) return false;
                    if (!empire.Emperor.isUnitFitToRule()) return false;
                    if (pActor.GetIdentity(empire).officialLevel != OfficialLevel.officiallevel_2 && pActor.GetIdentity(empire).officialLevel != OfficialLevel.officiallevel_3) return false;
                    if (empire.data.centerOffice.Minister.actor_id != -1L && empire.data.centerOffice.General.actor_id != -1L) return false;
                    if (empire.data.centerOffice.Minister.actor_id == empire.data.centerOffice.General.actor_id) return false;
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
                    empire.SetOfficer(empire.data.centerOffice.General, pActor);
                    empire.SetOfficer(empire.data.centerOffice.Minister, pActor);
                    OfficeIdentity identity = pActor.GetIdentity(empire);
                    identity.peerageType = PeerageType.Both;
                    identity.meritLevel = 1;
                    identity.honoraryOfficial = 1;
                    pActor.SetIdentity(identity, false);
                    TranslateHelper.LogcontrolledEmpire(pActor, empire);
                    pActor.editRenown(300);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "powerful_minister_replace_empire",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 60f,
                can_be_done_by_leader = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if(!pActor.isOfficer()) return false;
                    if (pActor.IsEmperor()) return false;
                    if (empire.Emperor == null) return false;
                    if (!empire.Emperor.isUnitFitToRule()) return false;
                    if (pActor.GetIdentity(empire).officialLevel != OfficialLevel.officiallevel_1) return false;
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
                id = "powerful_minister_take_city",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if(!pActor.isOfficer()) return false;
                    if (!pActor.isKing()) return false;
                    if (pActor.renown < empire.CoreKingdom.getRenown()/10) return false;
                    if (!pActor.CanTakeCity()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor actor)
                {
                    if (!actor.isOfficer()) return false;
                    if(!actor.isKing()) return false;
                    if(actor.IsEmperor()) return false;
                    if (!actor.CanTakeCity()) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = kingdom.GetEmpire();
                    foreach(City city in kingdom.cities)
                    {
                        if (city.neighbours_cities.Count>0)
                        {
                            foreach(City city2 in city.neighbours_cities)
                            {
                                if(city2.kingdom.isInEmpire()&&city2.kingdom!=kingdom&&city2.kingdom.isEmpire()&&!city2.isCapitalCity())
                                {
                                    city2.joinAnotherKingdom(kingdom);
                                    pActor.editRenown(-(empire.CoreKingdom.getRenown() / 10));
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
                }
            });
            //AssetManager.plots_library.add(new PlotAsset
            //{
            //    id = "officer_grab_alliance",
            //    path_icon = "EmperorQuest.png",
            //    group_id = "diplomacy",
            //    is_basic_plot = true,
            //    min_level = 5,
            //    progress_needed = 15f,
            //    can_be_done_by_leader = true,
            //    check_is_possible = delegate (Actor pActor)
            //    {
            //        Kingdom kingdom = pActor.kingdom;
            //        if (!kingdom.isInEmpire()) return false;
            //        Empire empire = kingdom.GetEmpire();
            //        if(!pActor.isOfficer()) return false;
            //        if (pActor.data.renown <= empire.emperor.data.renown) return false; 
            //        return true;
            //    },
            //    check_should_continue = delegate (Actor actor)
            //    {
            //        if (!actor.isOfficer()) return false;
            //        return true;
            //    },
            //    action = delegate (Actor pActor)
            //    {
            //        Kingdom kingdom = pActor.kingdom;
            //        Empire empire = kingdom.GetEmpire();
            //        foreach (Kingdom kingdom2 in empire.kingdoms_hashset)
            //        {

            //        }
                    
            //        return true;
            //    }
            //});
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "emperor_set_armed_province",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_leader = true,
                check_is_possible = delegate (Actor pActor)
                {
                    if (!pActor.IsEmperor()) return false;
                    Kingdom kingdom = pActor.kingdom;
                    Empire empire = pActor.kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (!empire.IsNeedToSetArmedProvince()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor actor)
                {
                    if (!actor.IsEmperor()) return false;
                    Kingdom kingdom = actor.kingdom;
                    Empire empire = actor.kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (!empire.IsNeedToSetArmedProvince()) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    foreach (Province province in empire.ProvinceList)
                    {
                        if (province.isBorderProvince()&&!province.data.is_set_to_country)
                        {
                            province.becomeKingdom();
                        }
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "new_empire_royal",
                path_icon = "ChineseCrown.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 30,
                progress_needed = 60f,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.isInEmpire()) return false;
                    if (!pActor.HasSpecificClan()) return false;
                    if (!kingdom.isEmpire()) return false;
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
                    pActor.clan.RecordHistoryEmpire(empire);
                    empire.data.created_time = World.world.getCurWorldTime();
                    empire.CoreKingdom.setKing(pActor);
                    empire.RecordHistory(EmpireHistoryType.new_empire_history, new Dictionary<string, string>()
                    {
                        ["actor"] = pActor.getName(),
                        ["place"] = empire.CoreKingdom.capital.GetCityName(),
                        ["name"] = empire.GetEmpireName(),
                    });
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "empire_enfeoff",
                path_icon = "ChineseCrown.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.IsEmperor()) return false;
                    if (!kingdom.isEmpire()) return false;
                    if (kingdom.hasEnemies()) return false;
                    if (!kingdom.GetEmpire().IsNeedToSetProvince()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.IsEmperor()) return false;
                    if (kingdom == null) return false;
                    if (!kingdom.isAlive()) return false;
                    if (!kingdom.isEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (!empire.isAlive()) return false;
                    if (kingdom.hasEnemies()) return false;
                    if (!empire.IsNeedToSetProvince()) return false;
                    return true;
                },
                action = StartEnfeoff
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "emperor_year_name",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
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
                id = "join_original_empire",
                path_icon = "plots/icons/plot_cause_rebellion",
                group_id = "diplomacy",
                is_basic_plot = true,
                can_be_done_by_leader = true,
                progress_needed = 70f,
                min_level = 1,
                check_is_possible = delegate (Actor pActor)
                {
                    if (pActor == null) return false;
                    if (!pActor.isCityLeader()) return false;
                    if (!pActor.hasCity())return false;
                    City city = pActor.city;
                    if (city == null) return false;
                    if (!city.hasProvince()) return false;
                    Province province = city.GetProvince();
                    if (province == null) return false;
                    if (!province.IsTotalVassaled()) return false;
                    if (province.TotolLoyalty()>100) return false;
                    return true;
                },
                action = delegate (Actor pActor)
                {
                    City city = pActor.city;
                    if (city == null) return false;
                    Province province = city.GetProvince();
                    if (province == null) return false;
                    province.becomeKingdom(leader:pActor);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "emperor_posthumous_name",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
                    if (!kingdom.GetEmpire().IsNeedToSetPosthumous()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    if (!pActor.isKing()) return false;
                    if (!pActor.hasKingdom()) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isAlive()) return false;
                    if (!kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
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
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!pActor.canAcquireTitle()) return false;
                    if (kingdom.getWars().Any()) return false;
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
                id = "emperor_get_back_province",
                path_icon = "TitleAcquire.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = CheckEmperorCanReclaim,
                check_should_continue = CheckEmperorCanReclaim,
                action = delegate(Actor pActor) 
                {
                    try
                    {
                        Kingdom kingdom = pActor.kingdom;
                        if (kingdom == null) return false;

                        Empire empire = kingdom.GetEmpire();
                        empire.ProvinceList.ForEach(p => 
                        {
                            p.updateOccupied();
                        });
                        var citiesToReclaim = empire.ProvinceList
                            .Where(p => p.occupied_cities.Count > 0)
                            .SelectMany(p => p.occupied_cities)
                            .Where(c => c.Key.kingdom != null && c.Key.kingdom != kingdom)
                            .ToList();

                        if (!citiesToReclaim.Any())
                        {
                            LogService.LogInfo("No cities to reclaim");
                            return false;
                        }

                        var kingdomsToWar = citiesToReclaim
                            .Select(c=> (kingdom: c.Key.kingdom, province: c.Key.GetProvince()))
                            .Distinct();

                        foreach (var target in kingdomsToWar)
                        {
                            if (kingdom.countTotalWarriors()> target.kingdom.countTotalWarriors())
                            {
                                if (kingdom == target.kingdom) continue;
                                War war = World.world.diplomacy.startWar(kingdom, target.kingdom, WarTypeLibrary.normal);
                                war.data.name = LM.Get("emperor_get_back_province");
                                TranslateHelper.LogEmpireGetBackLand(kingdom, target.province);
                                break;
                            }
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"Error reclaiming provinces: {ex}");
                        return false;
                    }
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_destroy_title",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
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
                        if (kingdom.GetOwnedTitle().Contains(title.data.id))
                        {
                            pActor.GetOwnedTitle().Remove(title.data.id);
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
                group_id = "diplomacy",
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
                        TranslateHelper.LogKingTakeTitle(kingdom, title);
                    }
                    return true;
                }
            }); 
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_change_capital_title",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    if (pActor == null) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
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
                group_id = "diplomacy",
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
                    if (kingdom.HasTitle()) return false;
                    if (kingdom.isEmpire()) return false;
                    if (kingdom.isInEmpire()) return false;
                    if (!kingdom.GetEmpiresCanbeJoined().Any()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    if (pActor == null) return false;
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.GetEmpiresCanbeJoined().Any()) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    kingdom.SetCountryLevel(countryLevel.countrylevel_4);
                    kingdom.GetEmpiresCanbeJoined().First().join(kingdom);
                    kingdom.getWars().ForEach(war => war.endForSides(WarWinner.Nobody));
                    TranslateHelper.LogKingdomJoinEmpire(kingdom, kingdom.GetEmpire());
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "kingdom_create_title",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
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
                id = "kingdom_change_name",
                path_icon = "EmperorQuest.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!pActor.HasTitle()) return false;
                    if (!pActor.kingdom.needToBecomeKingdom()) return false;
                    return true;
                },
                action = delegate(Actor pActor) 
                {
                    Kingdom kingdom = pActor.kingdom;
                    string name = kingdom.becomeKingdom(true);
                    TranslateHelper.LogBecomeKingdom(kingdom, name);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "minister_acquire_empire",
                path_icon = "ministerAcquireEmpire.png",
                group_id = "diplomacy",
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
                    if (kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
                    if (!pActor.HasTitle() || (!pActor.HasSpecificClan() || pActor.GetSpecificClan().id != kingdom.GetEmpire().EmpireSpecificClan.id)) return false;
                    LogService.LogInfo("权臣索取帝国错误");
                    return false;

                    if (kingdom.countTotalWarriors()<kingdom.GetEmpire().countWarriors()- kingdom.countTotalWarriors()) return false;
                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isAlive()) return false;
                    if (kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
                    if (kingdom.GetEmpire().Emperor == null) return false;
                    return true;
                },
                action = minister_acquire_empire
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "minister_acquire_title",
                path_icon = "ministerAcquireTitle.png",
                group_id = "diplomacy",
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
                    if (!kingdom.isInEmpire()) return false;
                    if (pActor.HasTitle()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire.Emperor == null) return false; 
                    if (empire.Emperor.GetOwnedTitle().Count()<=1) return false; 
                    if (pActor.GetPeeragesLevel()==PeeragesLevel.peerages_2) return false;
                    if (pActor.GetIdentity(empire) == null) return false;
                    if (pActor.GetIdentity(empire).officialLevel!=OfficialLevel.officiallevel_1) return false;

                    return true;
                },
                check_should_continue = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!kingdom.isAlive()) return false;
                    if (!kingdom.isInEmpire()) return false;
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
            AssetManager.plots_library.add(PlotsLibrary.new_war = new PlotAsset
            {
                id = "new_war",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_new_war",
                group_id = "diplomacy",
                min_level = 3,
                min_warfare = 6,
                money_cost = 20,
                min_renown_kingdom = 50,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                requires_diplomacy = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!DiplomacyHelpers.isWarNeeded(kingdom))
                    {
                        return false;
                    }
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
                    if (kingdom.isInEmpire())
                    {
                        if (!kingdom.GetEmpire().IsAllowToMakeWar())
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
                    if (warTarget.isInSameEmpire(pActor.kingdom))
                    {
                        Empire empire = warTarget.GetEmpire();
                        if (!empire.IsAllowToMakeWar()&&warTarget==empire.CoreKingdom)
                        {
                            return false;
                        }
                    }
                    if (!kingdom.isNeighbourWith(warTarget))
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
                    return DiplomacyHelpers.isWarNeeded(pActor.kingdom) ? true : false;
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
                if (kingdom.isInEmpire())
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
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "alliance_join",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_alliance_create",
                group_id = "diplomacy",
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
                    if (kingdom.isSupreme())
                    {
                        return false;
                    }
                    if (kingdom.hasAlliance())
                    {
                        return false;
                    }
                    if (kingdom.isInEmpire())
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
                color_special1 = pActor.kingdom.getColor()._colorText,
                color_special2 = pActor.kingdom.GetEmpire().CoreKingdom.getColor()._colorText
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
                    war.SetEmpireWarType(EmpireWarType.AquireEmpire);
                }
            }
            return true;
        }
        private static bool BecomeEmpireAndStartEnfeoff(Actor pActor)
        {
            Kingdom kingdom = pActor.kingdom;
            Empire empire = ModClass.EMPIRE_MANAGER.newEmpire(kingdom);
            if (kingdom.hasAlliance())
            {
                foreach (Kingdom kingdom1 in kingdom.getAlliance().kingdoms_hashset) 
                {
                    kingdom1.SetCountryLevel(countryLevel.countrylevel_3);
                    kingdom1.SetIndependentValue(50);
                    empire.join(kingdom1);
                }
            }
            empire.DivideIntoProvince();
            //empire.AutoEnfeoff();
            return true;
        }

        public static bool StartEnfeoff(Actor pActor)
        {
            Kingdom kingdom = pActor.kingdom;
            kingdom.GetEmpire().DivideIntoProvince();
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
                bool flag = pInitiatorKingdom.isInEmpire() ? pInitiatorKingdom.GetEmpire().IsNeighbourWith(tKingdom) : pInitiatorKingdom.isNeighbourWith(tKingdom);
                if (!tKingdom.isInSameEmpire(pInitiatorKingdom)&&!pInitiatorKingdom.isOpinionTowardsKingdomGood(tKingdom)&&num2>num&&flag)
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
        private static bool CheckEmperorCanReclaim(Actor pActor)
        {
            if (!pActor.IsEmperor()) return false;

            Kingdom kingdom = pActor.kingdom;
            if (kingdom == null) return false;

            Empire empire = kingdom.GetEmpire();
            if (empire == null) return false;
            if(!empire.isNeedToGetBackProvince()) return false;

            return !kingdom.getWars().Any();
        }

    }
}
