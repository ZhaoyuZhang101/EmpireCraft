using System;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckKingdomType:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isRekt()) return BehResult.Continue;
        var kingdomBack = LM.Get(SyncKingdomType(pKingdom).ToString());
        pKingdom.data.name = string.Join("\u200A", pKingdom.GetKingdomName(), kingdomBack);
        return BehResult.Continue;
    }
    //依据制度的不同选项动态调整国家后缀
    public static KingdomType SyncKingdomType(Kingdom kingdom)
    {
        var regime = kingdom.GetRegime();
        if (kingdom.isInEmpire())
        {
            var empire = kingdom.GetEmpire();
            switch (regime.type)
            {
                //律令制依照制度的选项不同来更新后缀
                case RegimeType.LvLing:
                {
                    if (kingdom.isEmpire())
                    {
                        return  KingdomType.LvLing_centre;
                    }

                    switch (regime.options["option_leader_select_method"][0])
                    {
                        case 1 when regime.options["toggle_allow_diplomacy"][0]==1:
                            return  KingdomType.LvLing_jiedushi;
                        case 1 when regime.options["toggle_allow_diplomacy"][0]==0:
                            return  KingdomType.LvLing_province;
                        case 0 when
                            kingdom.species_id == empire?.CoreKingdom.species_id:
                            return  KingdomType.LvLing_kingdom;
                        case 0 when
                            kingdom.species_id != empire?.CoreKingdom.species_id:
                            return  KingdomType.LvLing_jimizhou;
                    }

                    break;
                }
                case RegimeType.Republic:
                    //共和依照制度的选项不同来更新后缀
                    if (kingdom.isEmpire())
                    {
                        return  KingdomType.Republic_republic;
                    }

                    switch (regime.options["toggle_allow_army"][0])
                    {
                        case 0 when kingdom.species_id == empire?.CoreKingdom.species_id:
                            return  KingdomType.Republic_province;
                        case 1 when kingdom.species_id == empire?.CoreKingdom.species_id:
                            return  KingdomType.Republic_state;
                    }

                    if (kingdom.species_id != empire?.CoreKingdom.species_id)
                    {
                        return  KingdomType.Republic_autonomous_prefecture;
                    }

                    break;
                case RegimeType.Feudalism:
                    if (regime.options["option_religion_type"][0] == 3)
                    {
                        return  KingdomType.Feudalism_papal_state;
                    }
                    if (kingdom.isEmpire())
                    {
                        return  KingdomType.Feudalism_empire;
                    }

                    if (kingdom.GetOwnedTitle().Count >= 2)
                    {
                        return  KingdomType.Feudalism_kingdom;
                    }
                    if (kingdom.GetOwnedTitle().Count >= 1)
                    {
                        return kingdom.GetSpecificClan() == empire?.EmpireSpecificClan ?  KingdomType.Feudalism_grand_duchy :  KingdomType.Feudalism_duchy;
                    }

                    return kingdom.isBorder() ?  KingdomType.Feudalism_march :  KingdomType.Feudalism_county;
                
                case RegimeType.ZhouFeudalism:
                    if (kingdom.isEmpire())
                    {
                        return  KingdomType.ZhouFeudalism_empire;
                    }
                    
                    if (kingdom.species_id != empire?.CoreKingdom.species_id)
                    {
                        return  KingdomType.ZhouFeudalism_zi;
                    }
                    
                    if (kingdom.GetSpecificClan()==empire?.EmpireSpecificClan)
                    {
                        return  KingdomType.ZhouFeudalism_gong;
                    }

                    switch (kingdom.cities.Count)
                    {
                        case >= 2:
                            return  KingdomType.ZhouFeudalism_hou;
                        case >= 1:
                            return  KingdomType.ZhouFeudalism_bo;
                    }

                    break;
                case RegimeType.Arabic:
                    //帝国称哈里发国
                    if (kingdom.isEmpire())
                    {
                        return  KingdomType.Arabic_caliphate;
                    }
                    //无军事外交为行省
                    if (!kingdom.GetRegime().IsAllowDiplomacy())
                    {
                        return  KingdomType.Arabic_province;
                    }

                    //有军事外交，宗教等级高者为苏丹国，低为酋长国
                    return regime.options["option_religion_type"][0] <= 2 ?  KingdomType.Arabic_sultanate :  KingdomType.Arabic_emirate;
            }
        }
        else
        {
            switch (regime.type)
            {
                case RegimeType.Republic:
                    return  KingdomType.Republic_republic;
                case RegimeType.Feudalism:
                    return  KingdomType.Feudalism_kingdom;
                case RegimeType.LvLing:
                    return  KingdomType.LvLing_kingdom;
                case RegimeType.Arabic:
                    return regime.GetReligionLevel() is ReligionLevel.High or ReligionLevel.Medium ?  KingdomType.Arabic_sultanate :  KingdomType.Arabic_emirate;
                case RegimeType.ZhouFeudalism:
                    return  KingdomType.ZhouFeudalism_zi;
            }
        }
        return  KingdomType.default_country_post;
    }
}