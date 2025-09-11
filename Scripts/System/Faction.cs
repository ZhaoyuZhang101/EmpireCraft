using System.Collections.Generic;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace EmpireCraft.Scripts.System;
public enum FactionType
{
    转军府, //当地方省份位于边疆或者周边有任一军府时，且对帝国忠诚度低，会自行发起转军府决议
    扩张地盘, //当地方军府领导控制力大于当前地盘且对帝国忠诚度低时可发起扩张
    独立,
    转世袭,
    索取皇位,
    地方官叛乱,
    降低赋税,
    自由信仰,
    渴望共和,
    复辟帝制,
    宗教同化,
    农民起义,
    支持皇子,
    对外扩张,
    收回地盘,
    谋求统一
}
public class Faction
{
    public long id;
    public string name;
    public FactionType type;
    public List<long>  kingdoms;
    public long MainKingdom;
    public NanoObject target;
    public float progress = 0.0f;

    public void Start(Kingdom kingdom, FactionType factionType, NanoObject ptarget = null)
    {
        SetMain(kingdom);
        type = factionType;
        id = OverallHelperFunc.IdGenerator.NextId();
        target = ptarget;
    }
    public void JoinKingdom(Kingdom kingdom)
    {
        kingdoms.Add(kingdom.id);
    }
    //更新：每年一次共计十年
    public void Update()
    {
        progress += 0.1f;
        if (progress >= 1.0f) Execute();
    }
    public void Execute()
    {
        switch (type)
        {
            case FactionType.降低赋税:
                break;
            case FactionType.自由信仰:
                break;
            case FactionType.渴望共和:
                break;
            case FactionType.宗教同化:
                break;
            case FactionType.农民起义:
                break;
            case FactionType.地方官叛乱:
                break;
            case FactionType.对外扩张:
                break;
            case FactionType.扩张地盘:
                break;
            case FactionType.支持皇子:
                break;
            case FactionType.独立:
                break;
            case FactionType.索取皇位:
                break;
            case FactionType.谋求统一:
                break;
            case FactionType.转世袭:
                break;
            case FactionType.收回地盘:
                break;
            case FactionType.转军府:
                break;;
            case FactionType.复辟帝制:
                break; 
        }
    }
    public void SetMain(Kingdom kingdom)
    {
        MainKingdom = kingdom.id;
        if (!kingdoms.Contains(MainKingdom)) kingdoms.Add(kingdom.id);
    }
}