namespace EmpireCraft.Scripts.Enums;

public enum FactionType
{
    转军府, //当地方省份位于边疆或者周边有任一军府时，且对帝国忠诚度低，会自行发起转军府决议
    扩张地盘, //当地方军府领导控制力大于当前地盘且对帝国忠诚度低时可发起扩张
    独立,
    转世袭,
    索取皇位,
    地方官叛乱,
    农民起义,
    支持皇子,
    对外扩张
}