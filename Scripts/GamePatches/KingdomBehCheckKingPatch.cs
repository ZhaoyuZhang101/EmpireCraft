using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.TipAndLog;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches
{
    public class KingdomBehCheckKingPatch : GamePatch
    {
        public ModDeclare declare { get; set; }

        public void Initialize()
        {
            new Harmony(nameof(kingdom_apart)).Patch(
                AccessTools.Method(typeof(KingdomBehCheckKing), nameof(KingdomBehCheckKing.checkKingdomChaos)),
                prefix: new HarmonyMethod(GetType(), nameof(kingdom_apart))
            );
        }

        public static bool kingdom_apart(KingdomBehCheckKing __instance, Kingdom pMainKingdom) 
        {
            bool flag = false;
            using ListPool<City> listPool = new ListPool<City>(pMainKingdom.cities.Count);
            foreach (City city in pMainKingdom.cities)
            {
                if (city != pMainKingdom.capital && city.hasLeader())
                {
                    listPool.Add(city);
                }
            }
            if (listPool.Count == 0)
            {
                return false;
            }
            foreach (City item in listPool.LoopRandom())
            {
                Actor leader = item.leader;
                if (leader != null && leader.isAlive())
                {
                    if (pMainKingdom.isInEmpire()&&!pMainKingdom.isEmpire())
                    {
                        Empire empire = pMainKingdom.GetEmpire();
                        countryLevel cl = countryLevel.countrylevel_4;
                        PeeragesLevel pl = PeeragesLevel.peerages_4;
                        Kingdom newKingdom = pMainKingdom.GetEmpire().setEnfeoff(item, leader);
                        newKingdom.setCapital(item);
                        newKingdom.data.name = item.data.name;
                        LogService.LogInfo($"{item.GetCityName()}");
                        newKingdom.SetCountryLevel(cl);
                        newKingdom.SetFiedTimestamp(World.world.getCurWorldTime());
                        leader.SetPeeragesLevel(pl);
                        new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_enfeoff_log, empire.name)
                        {
                            location = empire.empire.location,
                            color_special1 = empire.empire.kingdomColor.getColorText()
                        }.add();
                        empire.join(newKingdom, true, false);
                        flag = true;

                    } else if (pMainKingdom.isInEmpire() && pMainKingdom.isEmpire())
                    {
                        item.makeOwnKingdom(leader, pRebellion: false, pFellApart: true).setKing(leader);
                        item.kingdom.data.name = item.data.name;
                        item.kingdom.empireLeave(true);
                    } 
                    else
                    {
                        item.makeOwnKingdom(leader, pRebellion: false, pFellApart: true).setKing(leader);
                        item.kingdom.data.name = item.data.name;
                        item.kingdom.empireLeave(true);
                        flag = true;
                    }

                }
            }
            if (flag)
            {
                if (pMainKingdom.hasAlliance())
                {
                    pMainKingdom.getAlliance().leave(pMainKingdom);
                }
                WorldLog.logFracturedKingdom(pMainKingdom);
            }
            return false;
        }
    }
}
