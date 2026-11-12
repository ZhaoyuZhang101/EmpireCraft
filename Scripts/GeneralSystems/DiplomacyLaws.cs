using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.GeneralSystems;

// 外交开关(玩家 2026-11-08"可以单独做个外交和叛乱开关了我这图没发展"，用户定"细化多做几个开关和总开关，
// 但是不限制玩家强制的")：一个总开关 empirecraft_law_ban_diplomacy，外加宣战、结盟、归附、朝贡、和亲、通好六个分开关。
//   · 总开关开着时六项一律停；没开则各分开关各管各的；
//   · 只拦 AI 自己发起的。上帝视角的外交面板(DiplomacyActions.Do)与各神力都在 MembershipCooldown.ByGod
//     作用域里跑，一律放行——玩家要宣战、要结盟、要册封，法则开着照样做得了；
//   · 只拦"新结关系"，不拆已有的：正在打的仗照打完，已结的盟、已有的附庸与朝贡照旧；
//   · 剧本按史实设定的关系(WorldMapScenario)不经这几道闸。
// 给剧本作者用：地图还没铺开时先把世界冻住，自己把版图摆好，铺完再关掉。
public static class DiplomacyLaws
{
    public enum Kind { War, Alliance, Vassal, Tribute, Marriage, Pact }

    // 世界还没建好时取不到法则对象，按没开算
    private static bool On(WorldLawAsset law)
    {
        try
        {
            return law?.isEnabled() == true;
        }
        catch
        {
            return false;
        }
    }

    // 玩家用神力把一条诉求当场推到施行(见 CourtSystem.GodHasten)：这一步里开的仗、结的关系算玩家强制的。
    // 单独一个标记，不借 MembershipCooldown.ByGod——那个还会记"神力放出、拉入"的保护期
    private static int _forced;

    public static void ByPlayer(global::System.Action action)
    {
        _forced++;
        try { action(); }
        finally { _forced--; }
    }

    // 正在神力推满的诉求里(不许自立的闸也放行，见 SecessionGate)
    public static bool Forced => _forced > 0;

    public static bool Blocked(Kind kind)
    {
        // 玩家强制的一律放行
        if (MembershipCooldown.GodActing || _forced > 0) return false;
        if (On(EmpireCraftWorldLawLibrary.empirecraft_law_ban_diplomacy)) return true;
        return On(kind switch
        {
            Kind.War => EmpireCraftWorldLawLibrary.empirecraft_law_ban_ai_war,
            Kind.Alliance => EmpireCraftWorldLawLibrary.empirecraft_law_ban_ai_alliance,
            Kind.Vassal => EmpireCraftWorldLawLibrary.empirecraft_law_ban_ai_vassal,
            Kind.Tribute => EmpireCraftWorldLawLibrary.empirecraft_law_ban_ai_tribute,
            Kind.Marriage => EmpireCraftWorldLawLibrary.empirecraft_law_ban_ai_marriage,
            Kind.Pact => EmpireCraftWorldLawLibrary.empirecraft_law_ban_ai_pact,
            _ => null
        });
    }

    // 宣战：模组各系统经 WarGate.CanDeclare，原版 AI 挑目标经 WarTargetPatch；没经这两处、直接开战的在开战总入口兜底
    // (见 WarReach.LawForbids，起兵内乱不算、归「禁止叛乱」管)
    public static bool WarBlocked => Blocked(Kind.War);
    // 结盟：原版 Alliance.canJoin(见 AlliancePatch)，建盟与入盟都走它
    public static bool AllianceBlocked => Blocked(Kind.Alliance);
    // 归附：小国望风归附(见 SmallStateSubmissionSystem)
    public static bool VassalBlocked => Blocked(Kind.Vassal);
    // 朝贡：称臣纳贡(见 TributaryRelations.Submit)
    public static bool TributeBlocked => Blocked(Kind.Tribute);
    // 和亲：议婚(见 RoyalMarriageSystem.Monthly)
    public static bool MarriageBlocked => Blocked(Kind.Marriage);
    // 通好：跨文明大国通好(见 GreatPowerDiplomacy)
    public static bool PactBlocked => Blocked(Kind.Pact);
}
