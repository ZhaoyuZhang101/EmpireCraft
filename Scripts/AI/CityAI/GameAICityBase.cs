using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.CityAI;

public abstract class GameAICityBase : BehaviourActionCity
{
    public abstract Type OriginalBeh { get; }

    // 为真时顶替原版行为留在它原来的任务里(见 GameAIMain.CityAIs)，不并进模组的大任务"do_mod_city_beh"：
    // 大任务里前面一步返回 Stop，后面各步都轮不到
    public virtual bool InPlace => false;
}
