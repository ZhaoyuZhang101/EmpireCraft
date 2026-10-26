using System.Collections.Generic;
using EmpireCraft.Scripts.Compatibility;

namespace EmpireCraft.Scripts.GeneralSystems;

// 无小人模式下留在地图上的实体不走原版决策(用户 2026-10-26："无小人模式下地图上依旧存在的实体人口需要把它们的ai全部关掉，
// 不然也会造成卡顿")。留下的多是朝堂上的人——官员、要紧宗室、举人进士、派系成员、族长、地主——生计、生养、住房、走路都由背景
// 人口与模组结算(见 CityPopulationSystem、RoyalHouseSystem、VirtualGenealogySystem)，原版逐人的决策(找活干、觅食、社交、
// 找对象、闲逛)和寻敌全是白算：
//   · 决策(OptimizationPatch.Prefix_CivilianDecision)：每 ModBehSeconds 秒(世界时间，约一个月)跑一次模组的人物逻辑
//     (do_mod_actor_beh：任官、入派、阶层、纳妾、税、法令、特质……)；每 VanillaSeconds 秒(约半年)让原版决策一次——
//     宗室、官员发起的谋划(太后立子、宗室作乱等，见 EmpireCraftPlotsAddition)走原版决策，全关就断了；
//     其余时间挂在闲居(DormantTask，等几秒再看)，比原版每一两秒决策一次少九成以上；
//   · 寻敌(OptimizationPatch.Prefix_CivilianAi)：不寻。
// 照原版走的：君主、城主(原版谋划——宣战、结盟、叛乱——由他们发起)、军人与军团、军团统领、船(不是智慧种族)、谋划中的人、
// 没有城的人、玩家收藏或镜头跟随的人、古战模组的人
public static class DormantActorAI
{
    public const string DormantTask = "ec_dormant";
    public const string ModBehTask = "do_mod_actor_beh";
    private const double ModBehSeconds = 5d, VanillaSeconds = 30d;

    // 每人上次跑模组人物逻辑、上次走原版决策的时刻
    private static readonly Dictionary<long, (double mod, double vanilla)> Last = new();
    private static object _world;

    public static bool Dormant(Actor actor) =>
        CityPopulationSystem.AbstractPopulationEnabled && actor?.data != null && !actor.isRekt() && actor.isAlive() &&
        actor.isSapient() && actor.hasCity() && !actor.isKing() && !actor.isCityLeader() && !actor.isWarrior() &&
        !actor.hasArmy() && actor.plot == null && !actor.isFavorite() && !actor.isCameraFollowingUnit() &&
        !CorpsSystem.IsGeneral(actor) && !AncientWarfareCompatibility.OwnsObject(actor);

    // 该做的下一件事：到时候了跑一次模组的人物逻辑，或让原版决策一次(返回 null)，否则闲居
    public static string NextTask(Actor actor)
    {
        if (!ReferenceEquals(_world, World.world) || Last.Count > 50000)
        {
            _world = World.world;
            Last.Clear();
        }
        double now = World.world.getCurWorldTime();
        // 头一回见到：先跑模组的人物逻辑，原版决策半年后
        if (!Last.TryGetValue(actor.id, out var last) || now < last.mod || now < last.vanilla)
        {
            Last[actor.id] = (now, now);
            return ModBehTask;
        }
        if (now - last.vanilla >= VanillaSeconds)
        {
            Last[actor.id] = (last.mod, now);
            return null;
        }
        if (now - last.mod >= ModBehSeconds)
        {
            Last[actor.id] = (now, last.vanilla);
            return ModBehTask;
        }
        return DormantTask;
    }
}
