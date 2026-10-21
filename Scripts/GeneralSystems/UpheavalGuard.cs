using System.Linq;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 大分裂冷却与单次上限(玩家 2026-10-21 反馈"现在帝国容易突然碎一地"；审计：近两天几处改动各开了一条成员成批出走的路——
// 游牧雄主身后、平常换汗几乎每回都乱，应仁之乱、哈里发更替之乱的门槛落在正统的正常区间，土崩瓦解收束时把两年里的
// 一切损失都算进去、帝国灭亡全员独立；这几路互不相让、也不共用冷却，同一次 CompleteSuccession 里会依次叠加。
// 用户 2026-10-21 选定下面这一套)：
//   · 计入并互相挡住的"大分裂"：汗位更替之乱、雄主身后分裂(含兀鲁思分封后的诸部自立)、哈里发/苏丹更替之乱
//     (NomadCycleSystem)、应仁之乱(MilitaryRegency)、土崩瓦解首义(ProvincialCollapseSystem)、宗室另立(EmpirePartitionSystem)；
//   · 真的走掉了至少一家成员才记时间(EmpireData.last_upheaval_at，旧档 -1)；此后 CooldownYears 年内这些事件一律不触发——
//     雄主身后的"必乱"冷却里当次作废，不往后顺延；
//   · 土崩瓦解进行中(两年收束期内)不判任何更替之乱和应仁之乱；
//   · 单次上限：一次带走的城不超过事件开始时帝国总城数(含成员)的 CapShare，雄主身后那一回 HeroCapShare；
//     按各路径原有的次序(离心度、远近等)逐个放走，下一家放进来就超过上限时就停。
//     土崩瓦解不受这个上限(否则帝国永远倒不了)，它的收束另见 ProvincialCollapseSystem.TryFall。
public static class UpheavalGuard
{
    public const int CooldownYears = 15;
    public const float CapShare = 1f / 3f, HeroCapShare = 0.5f;

    // 帝国(连同各成员)眼下的城数
    public static int Cities(Empire empire) =>
        empire?.kingdoms_list?.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt()).Sum(kingdom => kingdom.cities.Count) ?? 0;

    // 上次大分裂之后还没满 CooldownYears 年
    public static bool InCooldown(Empire empire)
    {
        double at = empire?.data?.last_upheaval_at ?? -1d;
        if (at < 0d || World.world == null) return false;
        return World.world.getCurWorldTime() >= at && Date.getYearsSince(at) < CooldownYears;
    }

    // 这时不判更替之乱、应仁之乱、宗室另立：大分裂冷却里，或土崩瓦解正在收束。why 给日志用
    public static bool Blocked(Empire empire, out string why)
    {
        why = null;
        if (InCooldown(empire)) why = $"大分裂冷却中(上次在 {Date.getYearsSince(empire.data.last_upheaval_at)} 年前，冷却 {CooldownYears} 年)";
        else if (ProvincialCollapseSystem.Active(empire)) why = "土崩瓦解收束期内";
        return why != null;
    }

    // 真走掉了成员：记下时间。occasion 为空时不记日志(土崩瓦解各省响应时刷新时间用)
    public static void Mark(Empire empire, string occasion = null, Budget budget = null)
    {
        if (empire?.data == null || World.world == null) return;
        empire.data.last_upheaval_at = World.world.getCurWorldTime();
        if (occasion == null) return;
        string cap = budget == null ? "不设上限" : $"带走 {budget.Taken} 城，上限 {budget.Cap}/{budget.Total} 城{(budget.Capped ? "，已到上限" : "")}";
        LogService.LogInfo($"[EmpireCraft][大分裂冷却] {empire.GetEmpireName()} {occasion}({cap})，此后 {CooldownYears} 年不再判大分裂");
    }

    // 单次上限：事件开始时数一次帝国总城数
    public sealed class Budget
    {
        public readonly int Total, Cap;
        public int Taken { get; private set; }
        // 有一家因放不下而被拦住
        public bool Capped { get; private set; }

        public Budget(Empire empire, float share)
        {
            Total = Cities(empire);
            Cap = Mathf.FloorToInt(Total * share);
        }

        // 再放走 cities 座城是否仍不超过上限；放不下就记下"已到上限"，调用方据此停手
        public bool Fits(int cities)
        {
            if (Taken + cities <= Cap) return true;
            Capped = true;
            return false;
        }

        public void Take(int cities) => Taken += cities;
    }
}
