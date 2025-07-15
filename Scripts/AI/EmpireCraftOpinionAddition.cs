using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.AI;
public class EmpireCraftOpinionAddition
{
    public static void init()
    {
        OpinionLibrary opl = AssetManager.opinion_library;
        opl.add(new OpinionAsset
        {
            id = "opinion_empire_loyalty",
            translation_key = "opinion_empire_loyalty",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.isInSameEmpire(pTarget))
                {
                    if (!pMain.isEmpire()&&pTarget.isEmpire()&&Date.getYearsSince(pMain.GetEmpire().getFoundedTimestamp())<100)
                    {
                        result = 999;
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_empire_powerful_minister`",
            translation_key_negative = "opinion_empire_powerful_minister",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.isInSameEmpire(pTarget))
                {
                    if (!pMain.isEmpire()&&pTarget.isEmpire()&&(pMain.countTotalWarriors()>pMain.GetEmpire().countWarriors()- pMain.countTotalWarriors()))
                    {
                        result = -50;
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_tianming_aquire",
            translation_key_negative = "opinion_tianming_aquire",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.isInSameEmpire(pTarget))
                {
                    if (!pMain.isEmpire()&&pTarget.isEmpire())
                    {
                        if (Date.getYearsSince(pMain.GetEmpire().getFoundedTimestamp()) >= 100&&pMain.countTotalWarriors()>=pTarget.countTotalWarriors())
                            result = -999;
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_in_same_empire",
            translation_key = "opinion_in_same_empire",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.isInSameEmpire(pTarget))
                {
                    result = 50;
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_empire_clan_been_changed",
            translation_key = "opinion_empire_clan_been_changed",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if ( pMain.isInEmpire()&&pTarget.isInEmpire())
                {
                    if (!pMain.isEmpire()&&pTarget.isEmpire())
                    {
                        if (pMain.GetEmpire().data.original_royal_been_changed)
                        {
                            result = -200;
                        }
                    }
                }

                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_different_empire_with_other_subspecies",
            translation_key_negative = "opinion_different_empire_with_other_subspecies",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (!pMain.isInSameEmpire(pTarget)&&pMain.isEmpire()&&pTarget.isEmpire()&&pMain.getMainSubspecies().getID()==pTarget.getMainSubspecies().getID())
                {
                    result = -999;
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_just_enfeoff",
            translation_key = "opinion_just_enfeoff",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.isInSameEmpire(pTarget))
                {
                    if (Date.getYearsSince(pMain.GetFiedTimestamp())<=50)
                        result = 100;
                }
                return result;
            }
        });

    }
}