using System.Collections.Generic;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.Data;

public enum InstitutionReformStage
{
    Debate,
    Legislation,
    Implementation,
    Consolidation
}

// 制度的真正持有者是"文化"，不是国家：某个政权把改革推完之后，节点写进这里，
// 同文化的所有政权立刻共享（持久类效果由 InstitutionSystem 的年度同步逐个落地）。
public sealed class CultureInstitutionState
{
    // 本文化已掌握的制度（自研完成的 + 从别的线吸收来的 + 开局的根节点）
    public List<string> enacted_node_ids = new();
    // 上面这些里面，哪些是从别的线吸收来的（只用于 UI 区分显示和历史记录）
    public List<string> absorbed_node_ids = new();
    // 0 表示按已掌握制度自动评级；正数表示玩家在窗口里手动指定的文明等级。
    public int manual_level;
    // 外来节点的接触度 / 接触年数。只对"有资格被本文化吸收"的外来节点累计，
    // 所以这几张表不会无限膨胀成全世界所有节点。
    public Dictionary<string, float> exposure = new();
    public Dictionary<string, int> contact_years = new();
    public Dictionary<string, double> last_exposure_timestamp = new();
    // 通过 unlock_succession_law 节点效果解锁的继承法；三种默认法(嫡长子/幼子/兄终弟及)
    // 不需要出现在这里，判定统一走 SuccessionLawSystem.IsSuccessionLawUnlocked。
    public List<SuccessionLawType> unlocked_succession_laws = new();
    // 各制度开始施行的时间：反对阶层会逐渐适应，怨气压力随施行年数衰减。
    // 旧存档没有记录的节点，在第一次结算阶层怨气时从当时开始计。
    public Dictionary<string, double> enacted_timestamps = new();
}

// 帝国只持有"正在推进的这一次改革"和"本政权已经落地过哪些节点的持久效果"。
// applied_node_ids 是幂等同步用的：文化掌握了某节点之后，同文化的每个帝国在自己的年度
// 更新里把该节点的持久效果补上一次，补过就记在这里，不会重复补；政体变更会清空它，
// 因为 LoadRegime 会把 regime 从模板重新克隆，之前改过的选项和解锁过的诉求都被抹掉了，
// 得整体重放一遍。
public sealed class InstitutionEmpireState
{
    public InstitutionReformState active_reform;
    public List<string> applied_node_ids = new();
    // 阶层怨气是帝国政治状态，不随文化共享。键是阶层，值为 0~100。
    public Dictionary<SocialClass, float> class_grievances = new();
    // 节点 id，或 regression:<旧等级>:<新等级>，用于 UI 和历史还原具体导火索。
    public Dictionary<SocialClass, string> class_grievance_causes = new();
    public double last_social_rebellion_timestamp = -1d;
    public long social_rebellion_war_id = -1L;
    public double last_update_timestamp = -1d;
    public double last_ai_reform_attempt_timestamp = -1d;
    public double last_reform_completed_timestamp = -1d;
}

public sealed class InstitutionReformState
{
    public string node_id = "";
    // 发起并承担政治成败的派系。空串表示君主/国家直接发起。
    public string sponsor_faction_id = "";
    public float progress;
    public float radicalism;
    public float support;
    public float opposition;
    public InstitutionReformStage stage = InstitutionReformStage.Debate;
    public double started_timestamp = -1d;
    public bool forced;
    public bool resistance_triggered;
    // 叛乱战争进行时冻结改革进度，战争结局负责继续或撤销改革。
    public long resistance_war_id = -1L;
}

public static class InstitutionStateNormalizer
{
    public static void Normalize(InstitutionEmpireState state)
    {
        if (state == null) return;
        state.applied_node_ids ??= new List<string>();
        state.class_grievances ??= new Dictionary<SocialClass, float>();
        state.class_grievance_causes ??= new Dictionary<SocialClass, string>();
        foreach (SocialClass socialClass in global::System.Enum.GetValues(typeof(SocialClass)))
        {
            if (!state.class_grievances.ContainsKey(socialClass)) state.class_grievances[socialClass] = 0f;
            if (!state.class_grievance_causes.ContainsKey(socialClass)) state.class_grievance_causes[socialClass] = "";
            state.class_grievance_causes[socialClass] ??= "";
            state.class_grievances[socialClass] = global::System.Math.Max(0f,
                global::System.Math.Min(100f, state.class_grievances[socialClass]));
        }
        if (state.active_reform != null)
        {
            state.active_reform.node_id ??= "";
            state.active_reform.sponsor_faction_id ??= "";
        }
    }

    public static void Normalize(CultureInstitutionState state)
    {
        if (state == null) return;
        state.manual_level = global::System.Math.Max(0, state.manual_level);
        state.enacted_node_ids ??= new List<string>();
        state.absorbed_node_ids ??= new List<string>();
        state.exposure ??= new Dictionary<string, float>();
        state.contact_years ??= new Dictionary<string, int>();
        state.last_exposure_timestamp ??= new Dictionary<string, double>();
        state.unlocked_succession_laws ??= new List<SuccessionLawType>();
    }
}
