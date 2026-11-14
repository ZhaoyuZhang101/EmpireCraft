// 国、城、人的行为任务：游戏里每个国家、每座城每帧走一步自己的任务表(原版 AiSystem)，模组把它的国家行为(立君、立储、收税、科举、
// 律令……)、城市行为(城主、官职、律令、收税、谋叛……)、帝国行为(内阁、天命、爵位)、人物行为(任官、入派、纳妾、交税……)
// 都登记在这些任务表里。服务器没有原版那套 AI，这里补上：任务表照原版的登记方式建起来，ServerAi 照原版的走法逐步执行。
//# hand BehaviourTaskBase`1: addBeh
//# hand JobAsset`2: addTask
//# hand ai.behaviours.KingdomBehRandomWait: ctor execute
//# hand ai.behaviours.CityBehRandomWait: ctor execute
//# hand ai.behaviours.BehRandomWait: ctor execute
//# hand City: update
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EmpireCraft.Server;

public partial class BehaviourTaskBase<T>
{
    // 同原版：行为的名字取类名(去掉 ai.behaviours. 前缀)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void addBeh(T pAction)
    {
        if (pAction == null) return;
        pAction.id = pAction.GetType().ToString().Replace("ai.behaviours.", "");
        list.Add(pAction);
        pAction.create();
    }
}

public partial class JobAsset<TCondition, TSimObject>
{
    // 这一种"活计"依次要办的任务
    internal readonly List<string> ShimTasks = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void addTask(string pTask)
    {
        if (!string.IsNullOrEmpty(pTask)) ShimTasks.Add(pTask);
    }
}

namespace ai.behaviours
{
    // 歇一会儿：歇多久交给 ServerAi(原版写在国、城、人身上的计时)
    public partial class KingdomBehRandomWait
    {
        private readonly float _min, _max;

        public KingdomBehRandomWait() : this(0f, 1f)
        {
        }

        public KingdomBehRandomWait(float pMin = 0f, float pMax = 1f)
        {
            _min = pMin;
            _max = pMax;
        }

        public override BehResult execute(global::Kingdom pKingdom)
        {
            ServerAi.Wait = Randy.randomFloat(_min, _max);
            return BehResult.Continue;
        }
    }

    public partial class CityBehRandomWait
    {
        private readonly float _min, _max;

        public CityBehRandomWait() : this(0f, 1f)
        {
        }

        public CityBehRandomWait(float pMin = 0f, float pMax = 1f)
        {
            _min = pMin;
            _max = pMax;
        }

        public override BehResult execute(global::City pCity)
        {
            ServerAi.Wait = Randy.randomFloat(_min, _max);
            return BehResult.Continue;
        }
    }

    public partial class BehRandomWait
    {
        private readonly float _min, _max;

        public BehRandomWait() : this(0f, 1f)
        {
        }

        public BehRandomWait(float pMin = 0f, float pMax = 1f, bool pLand = false)
        {
            _min = pMin;
            _max = pMax;
        }

        public override BehResult execute(global::Actor pActor)
        {
            ServerAi.Wait = Randy.randomFloat(_min, _max);
            return BehResult.Continue;
        }
    }
}

public partial class City
{
    // 每座城每帧一回(原版的城市更新)：模组挂在这里的前置(城主补位、土地经济、占领扩散、城市就业、年度扫描……)照常跑；
    // 本体只走城自己的任务表(见 ServerAi)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void update(float pElapsed) => ServerAi.StepCity(this, pElapsed);
}
