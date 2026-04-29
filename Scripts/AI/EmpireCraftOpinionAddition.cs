using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI;
public static class EmpireCraftOpinionAddition
{
    //被劫掠
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
                if (pMain.IsInSameEmpire(pTarget))
                {
                    if (!pMain.IsEmpire()&&pTarget.IsEmpire())
                    {
                        var mandate = pTarget.GetEmpire().Mandate;
                        result = (mandate-50)*5;
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_empire_polite",
            translation_key = "opinion_empire_polite",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (!pMain.IsInSameEmpire(pTarget))
                {
                    if (!pMain.IsInEmpire() && pTarget.IsEmpire()&&pMain.countTotalWarriors()*2<=pTarget.GetEmpire().countWarriors())
                    {
                        result = pTarget.GetEmpire().Additions.addition[OfficerPowerType.礼仪] * 5;
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_occupied_title",
            translation_key = "opinion_occupied_title",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (!pMain.IsInSameEmpire(pTarget))
                {
                    if (pMain.HasMainTitle())
                    {
                        var title = pMain.GetMainTitle();
                        var value = pTarget.cities.Intersect(title.getCities()).Count();
                        result = -(value*100);
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_different_faction",
            translation_key = "opinion_different_faction",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.IsInSameEmpire(pTarget))
                {
                    if ((pMain?.king?.HasFaction()??false)&&(pTarget?.king?.HasFaction()??false))
                    {
                        if (pMain.king.GetFaction() != pTarget.king.GetFaction())
                        {
                            result = -300;
                        }
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_empire_maintained",
            translation_key = "opinion_empire_maintained",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.IsInSameEmpire(pTarget))
                {
                    if (pTarget.IsEmpire())
                    {
                        if (pMain.IsNeedToMaintainGoodOpinion())
                        {
                            result = 99999;
                        }
                    }
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_religion_place",
            translation_key = "opinion_religion_place",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.hasReligion()&&pMain.GetRegime()!=null)
                {
                    Religion religion = pMain.religion;
                    if (religion.GetCity() == pTarget.capital && pTarget.religion == religion)
                    {
                        result = 100*(int)pMain.GetRegime().GetReligionLevel();
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
                if (pMain.IsInSameEmpire(pTarget))
                {
                    if (!pMain.IsEmpire()&&pTarget.IsEmpire()&&(pMain.countTotalWarriors()>pMain.GetEmpire().countWarriors()- pMain.countTotalWarriors()))
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
                if (pMain.IsInSameEmpire(pTarget))
                {
                    if (!pMain.IsEmpire()&&pTarget.IsEmpire())
                    {
                        if (pMain.GetCorruptionRate()>0.8f&&pMain.countTotalWarriors()>=pTarget.countTotalWarriors())
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
                if (pMain.IsInSameEmpire(pTarget))
                {
                    result = 100;
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
                if ( pMain.IsInEmpire()&&pTarget.IsInEmpire())
                {
                    if (!pMain.IsEmpire()&&pTarget.IsEmpire())
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
                if (!pMain.IsInSameEmpire(pTarget)&&pMain.IsEmpire()&&pTarget.IsEmpire()&&pMain.getSpecies()==pTarget.getSpecies()&&pMain.king!=pTarget.king)
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
                if (pMain.IsInSameEmpire(pTarget))
                {
                    if (Date.getYearsSince(pMain.GetFiedTimestamp())<=50)
                        result = 100;
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_same_ruler",
            translation_key = "opinion_same_ruler",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain.king == pTarget.king&&!pMain.king.isRekt())
                {
                    result = 999;
                }
                return result;
            }
        });
        opl.add(new OpinionAsset
        {
            id = "opinion_local_claim_failed",
            translation_key_negative = "opinion_local_claim_failed",
            calc = delegate (Kingdom pMain, Kingdom pTarget)
            {
                int result = 0;
                if (pMain == null || pTarget == null) return result;
                if (!pMain.IsInSameEmpire(pTarget)) return result;
                if (pMain.IsEmpire() || !pTarget.IsEmpire()) return result;
                Empire empire = pTarget.GetEmpire();
                if (empire == null) return result;
                result = -(pMain.GetLocalClaimFailedOpinion(empire) * 20);
                return result;
            }
        });

    }
}
