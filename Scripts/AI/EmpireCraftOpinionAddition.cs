using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                        var emp = pTarget.GetEmpire();
                        if (emp != null && !emp.isRekt() && !emp.IsArchived())
                        {
                            var core = emp.CoreKingdom;
                            if ((core?.GetMoney() ?? 0) < 0)
                            {
                                result = 999;
                            }
                        }
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
                    if (!pMain.IsInEmpire() && pTarget.IsEmpire())
                    {
                        var emp = pTarget.GetEmpire();
                        if (emp != null && !emp.isRekt() && !emp.IsArchived())
                        {
                            if (pMain.countTotalWarriors() * 2 <= emp.countWarriors())
                            {
                                var adds = emp.Additions;
                                if (adds != null && adds.addition != null && adds.addition.ContainsKey(OfficerPowerType.礼仪))
                                {
                                    result = adds.addition[OfficerPowerType.礼仪] * 5;
                                }
                            }
                        }
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
                        var titleCities = title?.getCities();
                        if (titleCities != null)
                        {
                            var value = pTarget.cities.Intersect(titleCities).Count();
                            result = -(value*100);
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
                    if (!pMain.IsEmpire()&&pTarget.IsEmpire())
                    {
                        var emp = pMain.GetEmpire();
                        if (emp != null && !emp.isRekt() && !emp.IsArchived())
                        {
                            if (pMain.countTotalWarriors() > emp.countWarriors() - pMain.countTotalWarriors())
                            {
                                result = -50;
                            }
                        }
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
                        var emp = pMain.GetEmpire();
                        if (emp != null && !emp.isRekt() && !emp.IsArchived())
                        {
                            if (emp.data.original_royal_been_changed)
                            {
                                result = -200;
                            }
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
                if (pMain.king == pTarget.king && pMain.king != null && !pMain.king.isRekt())
                {
                    result = 999;
                }
                return result;
            }
        });

    }
}
