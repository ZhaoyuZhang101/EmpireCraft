using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.TipAndLog;
using NeoModLoader.services;
using System.Linq;

namespace EmpireCraft.Scripts.AI
{
    public static class EmpireCraftPlotsAddition
    {
        public static void init()
        {
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "become_empire",
                path_icon = "ChineseCrown.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 30,
                progress_needed = 30f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (kingdom.isEmpire()) return false;
                    if (kingdom.isInEmpire()) return false;
                    if (ModClass.EMPIRE_MANAGER.Select(e=>e.empire.getMainSubspecies()==kingdom.getMainSubspecies()).Count()>2) return false;
                    if ((kingdom.countCities() < 5)&&(!kingdom.isSupreme()&&World.world.kingdoms.Count()>=2)) return false;
                    return true;
                },
                
                action = BecomeEmpireAndStartEnfeoff
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "empire_enfeoff",
                path_icon = "ChineseCrown.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                money_cost = 30,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!pActor.isKing()) return false;
                    if (!kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
                    if (kingdom.hasEnemies()) return false;
                    if (kingdom.countCities() < 3) return false;
                    return true;
                },
                
                action = StartEnfeoff
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "ministor_acquire_empire",
                path_icon = "MinistorAcquireEmpire.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 1,
                money_cost = 30,
                progress_needed = 30f,
                can_be_done_by_king = true,
                requires_diplomacy = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!DiplomacyHelpers.isWarNeeded(kingdom)) return false;
                    if (!pActor.isKing()) return false;
                    if (kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
                    if (!pActor.HasTitle()|| (pActor.hasClan()?pActor.clan.getID()!=kingdom.GetEmpire().empire_clan.getID():true)) return false;
                    if (kingdom.countTotalWarriors()<kingdom.GetEmpire().countWarriors()- kingdom.countTotalWarriors()) return false;
                    return true;
                },
                action = ministor_acquire_empire
            });
            AssetManager.plots_library.add(new PlotAsset
            {
                id = "ministor_acquire_title",
                path_icon = "MinistorAcquireTitle.png",
                group_id = "diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 30f,
                can_be_done_by_king = true,
                requires_diplomacy = true,
                check_is_possible = delegate (Actor pActor)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (!DiplomacyHelpers.isWarNeeded(kingdom)) return false;
                    if (!pActor.isKing()) return false;
                    if (kingdom.isEmpire()) return false;
                    if (!kingdom.isInEmpire()) return false;
                    if (pActor.HasTitle()) return false;
                    if (pActor.GetPeeragesLevel()==PeeragesLevel.peerages_2) return false;
                    if (kingdom.countTotalWarriors()<kingdom.GetEmpire().countWarriors()- kingdom.countTotalWarriors()) return false;
                    return true;
                },
                action = delegate (Actor pActor) 
                {
                    string title = pActor.kingdom.capital.SelectKingdomName();
                    title = (title==""||title==null)? pActor.kingdom.GetKingdomName() : title;
                    pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_2);
                    pActor.SetTitle(title);
                    return true;
                }
            });
            LogService.LogInfo($"目前加载{AssetManager.plots_library.getList().Count().ToString()}个政策");
            foreach (var p in AssetManager.plots_library.list)
            {
                LogService.LogInfo(p.id);
            }
            AssetManager.plots_library.linkAssets();
        }

        private static bool ministor_acquire_empire(Actor pActor)
        {

            new WorldLogMessage(EmpireCraftWorldLogLibrary.ministor_try_aqcuire_empire_log, pActor.GetTitle(), pActor.data.name, pActor.kingdom.GetEmpire().data.name)
            {
                color_special1 = pActor.kingdom.getColor()._colorText,
                color_special2 = pActor.kingdom.GetEmpire().empire.getColor()._colorText
            }.add();

            if ((float)pActor.kingdom.countCities() / (float)(pActor.kingdom.GetEmpire().countCities()- pActor.kingdom.countCities())>=4)
            {
                pActor.kingdom.GetEmpire().replaceEmpire(pActor.kingdom);
            } 
            else
            {
                War war = World.world.diplomacy.startWar(pActor.kingdom, pActor.kingdom.GetEmpire().empire, WarTypeLibrary.normal);
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
            empire.AutoEnfeoff();
            return true;
        }

        private static bool StartEnfeoff(Actor pActor)
        {
            Kingdom kingdom = pActor.kingdom;
            kingdom.GetEmpire().AutoEnfeoff();
            return true;
        }

    }
}
