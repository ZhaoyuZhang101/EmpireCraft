using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
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
                        item.joinAnotherKingdom(empire.empire);
                        //ModClass.PROVINCE_MANAGER.newProvince(item);
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
