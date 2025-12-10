using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckKingdomType: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.isRekt()) return BehResult.Continue;
        SyncKingdomStatus(pKingdom);
        SyncOffice(pKingdom);
        return BehResult.Continue;
    }

    private static void SyncOffice(Kingdom pKingdom)
    {
        OfficeObject office = pKingdom.GetOffice();
        if (office == null)
        {
            pKingdom.InitialRegime();
            return;
        }
        office.meta_object = pKingdom;
        office.is_local = true;
        office.actor_id = pKingdom.king?.id??-1L;
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            empire.data.centerOffice.SyncMetaObject(pKingdom);
        }
    }

    public static void SyncKingdomStatus(Kingdom pKingdom)
    {
        var originalKingdomType = pKingdom.GetKingdomType();
        //计算当前国家类别
        KingdomType newkingdomType = CalcKingdomType(pKingdom);
        pKingdom.SetKingdomType(newkingdomType);
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            empire.SetEmpireName(pKingdom.GetKingdomName());
        }
        //获取国家政体后同步国家官位
        var regime = pKingdom.GetRegime();
        if (pKingdom.GetOffice()?.regimeType != regime.type||originalKingdomType != newkingdomType)
        {
            BureauSetting setting = regime.bureau_config.kingdoms[newkingdomType];
            OfficeObject officeObject = new OfficeObject();
            officeObject.InitialOffice(setting);
            officeObject.regimeType = regime.type;
            officeObject.meta_object = pKingdom;
            officeObject.is_local = true;
            if (officeObject.leader_select_method != LeaderSelectMethod.Default)
            {
                regime.SetLeaderSelectMethod(officeObject.leader_select_method);
            }
            if (pKingdom.hasKing())
            {
                officeObject.SetActor(pKingdom.king);
            }
            pKingdom.SetOffice(officeObject);
            foreach (var city in pKingdom.cities)
            {
                city.InitialRegime();
            }
        }
        
        var kingdomFront = pKingdom.capital.GetCityName();
        if (!pKingdom.capital.hasTitle())
        {
            kingdomFront = pKingdom.GetKingdomName();
        }
        else
        {
            kingdomFront = pKingdom.capital.GetCityName();
        }
        
        if (pKingdom.GetRegime().GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
        {
            if (pKingdom.HasMainTitle())
            {
                if (pKingdom.GetMainTitle()?.name != null)
                {
                    kingdomFront = pKingdom.GetMainTitle().name;
                }
            }
        }

        if (pKingdom.IsEmpire())
        {
            kingdomFront = pKingdom.GetEmpire().GetEmpireName();
        }
        var kingdomBack = LM.Get(newkingdomType.ToString());
        if (!pKingdom.IsFactionRebelling()&&pKingdom.getWars().Count()<=0)
        {
            pKingdom.data.name = string.Join("\u200A", kingdomFront, kingdomBack);
        }
        foreach (var city in pKingdom.cities)
        {
            var cityBack = LM.Get(city.GetCityType().ToString());
            city.data.name = string.Join("\u200A", city.GetCityName(), cityBack);
        }
    }

    public static CityType CalcCityType(Kingdom kingdom)
    {
        Regime regime = kingdom.GetRegime();
        if (regime == null)
        {
            LogService.LogInfo("国家政策为空");
            return CityType.Feudalism_city;
        }
        KingdomType  kingdomType = kingdom.GetKingdomType();
        switch (regime.type)
        {
            case RegimeType.Arabic:
                return CityType.Arabic_city;
            case RegimeType.Feudalism:
                switch (kingdomType)
                {
                    case KingdomType.Feudalism_empire:
                        return CityType.Feudalism_dirC;
                    case KingdomType.Feudalism_papal_state:
                        return CityType.Feudalism_religion_district;
                    default:
                        return CityType.Feudalism_city;
                }
            case RegimeType.LvLing:
                return CityType.LvLing_city;
            case RegimeType.Republic:
                return CityType.Republic_city;
            case RegimeType.ZhouFeudalism:
                return CityType.ZhouFeudalism_city;
            case RegimeType.YouMu:
                return CityType.YouMu_city;
            default:
                return CityType.Feudalism_city;
        }
    }
    
    //依据制度的不同选项动态调整国家后缀
    public static KingdomType CalcKingdomType(Kingdom kingdom)
    {
        var regime = kingdom.GetRegime();
        if (kingdom.IsInEmpire())
        {
            var empire = kingdom.GetEmpire();
            switch (regime.type)
            {
                //律令制依照制度的选项不同来更新后缀
                case RegimeType.LvLing:
                {
                    if (kingdom.IsEmpire())
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
                    if (kingdom.IsEmpire())
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
                    if (kingdom.IsEmpire())
                    {
                        return  KingdomType.Feudalism_empire;
                    }

                    if (kingdom.GetControlledTitles().Count >= 2)
                    {
                        return  KingdomType.Feudalism_kingdom;
                    }
                    if (kingdom.GetControlledTitles().Count >= 1)
                    {
                        return kingdom.GetSpecificClan() == empire?.EmpireSpecificClan ?  KingdomType.Feudalism_grand_duchy :  KingdomType.Feudalism_duchy;
                    }

                    return kingdom.IsBorder() ?  KingdomType.Feudalism_march :  KingdomType.Feudalism_county;
                
                case RegimeType.ZhouFeudalism:
                    if (kingdom.IsEmpire())
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
                    if (kingdom.IsEmpire())
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
                case RegimeType.YouMu:
                    if (kingdom.IsEmpire())
                    {
                        return KingdomType.YouMu_centre;
                    }

                    if (regime.IsAllowSupportCenterArmy())
                    {
                        return KingdomType.YouMu_bu;
                    }
                    return KingdomType.YouMu_kingdom;
            }
        }
        else
        {
            switch (regime.type)
            {
                case RegimeType.Republic:
                    return  KingdomType.Republic_republic;
                case RegimeType.Feudalism:
                    return regime.GetReligionLevel() == ReligionLevel.High ? KingdomType.Feudalism_papal_state : KingdomType.Feudalism_kingdom;
                case RegimeType.LvLing:
                    return  KingdomType.LvLing_kingdom;
                case RegimeType.Arabic:
                    return regime.GetReligionLevel() is ReligionLevel.High or ReligionLevel.Medium ?  KingdomType.Arabic_sultanate :  KingdomType.Arabic_emirate;
                case RegimeType.ZhouFeudalism:
                    return  KingdomType.ZhouFeudalism_zi;
                case RegimeType.YouMu:
                    return  KingdomType.YouMu_kingdom;
            }
        }
        return  KingdomType.LvLing_kingdom;
    }
}