using System.Collections.Generic;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;
public enum TemporaryFactionType
{
    转军府, //当地方省份位于边疆或者周边有任一军府时，且对帝国忠诚度低，会自行发起转军府决议
    扩张地盘, //当地方军府领导控制力大于当前地盘且对帝国忠诚度低时可发起扩张
    独立,
    提供岁币,
    割让城池,
    转世袭,
    索取皇位,
    开科取士,
    削藩,
    拓展金融霸权,
    缩减金融霸权,
    输出革命,
    扶持革命党,
    夺取诸侯开战权,
    允许诸侯自由开战,
    分割继承,
    强者继承法,
    地方官叛乱,
    撤销军府,
    降低赋税,
    提高赋税,
    提高福利,
    自由信仰,
    渴望共和,
    清除移民,
    复辟帝制,
    宗教同化,
    农民起义,
    支持皇子,
    对外扩张,
    汉化,
    收回地盘,
    开放移民,
    谋求统一
}
public abstract class TemporaryFaction
{
    public TemporaryFactionType type;
    public List<long>  kingdoms;
    public long MainKingdom = -1L;
    public NanoObject target;
    public float progress = 0.0f;
    public bool started = false;

    public void Start(Kingdom kingdom, TemporaryFactionType pTemporaryFactionType, NanoObject ptarget = null)
    {
        SetMain(kingdom);
        type = pTemporaryFactionType;
        target = ptarget;
        started = true;
    }

    public void End()
    {
        SetMain(null);
        kingdoms.Clear();
        started = false;
    }
    public void JoinKingdom(Kingdom kingdom)
    {
        kingdoms.Add(kingdom.id);
    }
    //更新：每年一次共计十年
    public void Update()
    {
        if (MainKingdom == -1L)
        {
            return;
        }
        if (!World.world.kingdoms.get(MainKingdom)?.hasKing() ?? true)
        {
            return;
        }
        if (started)
        {
            progress += 0.1f;
            if (progress >= 1.0f) Execute();
        }
        else
        {
            progress = 0.0f;
        }
    }
    /// <summary>
    /// 触发条件成功后的执行动作
    /// </summary>
    public abstract void Execute();
    
    /// <summary>
    /// 检查条件是否满足
    /// </summary>
    /// <returns>返回条件是否满足的结果</returns>
    public abstract bool CheckCondition();
    public void SetMain(Kingdom kingdom)
    {
        if (kingdom == null)
        {
            MainKingdom = -1L;
            return;
        }
        MainKingdom = kingdom.id;
        if (!kingdoms.Contains(MainKingdom)) kingdoms.Add(kingdom.id);
    }
}