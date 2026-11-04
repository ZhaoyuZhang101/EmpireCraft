using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 伏波军海军的规模(用户 2026-11-04 "临高的海军应该是及其发达的"，原著见灰机 wiki《伏波军海军》：
// 1636 年编成有立春级轻巡 4、掣电级一等炮舰 15、河川炮舰 4、8154 型驱逐舰 4，共 27 艘主力，另有武装大发艇 24 等辅船，
// 分三支大洋舰队、三支河川舰队；"凡是海上飘的都归海军"，自有海军工业集团)。这里不碰游戏类型，取数在 NavySystem：
//   · 全军定额：按阶段定，阶段 5(元老院内阁，1634 年底起)正好是 27 艘；定额以内由海军工业集团造，不走国库，国库空了也照样下水；
//   · 定额以外照常花钱，每港定额比别国高(别国太平 2、交战 6)；
//   · 下水快一倍(别国太平 0.5、交战 1)；
//   · 舰炮代差：同样的船战力再乘 PowerEdge。舰种本来就按蒸汽铁甲算，再乘这一下，一支舰队打得过当地几支水师
public static class AosongNavyRules
{
    // 下标是阶段(见 TransmigrantSystem.StageNodes)：降临 1、第一届执委会 2、共同纲领 3、大宋澳洲行在 4、元老院内阁 5、北伐功成 6
    private static readonly int[] Warships = { 4, 4, 8, 14, 20, 27, 40 };
    private static readonly int[] Transports = { 4, 4, 8, 14, 20, 28, 40 };

    public const float PowerEdge = 1.6f;
    private const int PeacePortWarships = 6, WarPortWarships = 12, PeacePortTransports = 4, WarPortTransports = 8;

    private static int Clamp(int stage) => stage < 0 ? 0 : stage >= Warships.Length ? Warships.Length - 1 : stage;

    public static int FloorWarships(int stage) => Warships[Clamp(stage)];

    public static int FloorTransports(int stage) => Transports[Clamp(stage)];

    public static bool UnderWarshipFloor(int stage, int realmWarships) => realmWarships < FloorWarships(stage);

    public static bool UnderTransportFloor(int stage, int realmTransports) => realmTransports < FloorTransports(stage);

    // 这座港要造到几艘：本港定额，和全军定额摊到每港的份额，取大的；港口少的时候全军定额也造得满
    public static int WantWarships(int stage, bool war, float level, int realmPorts) =>
        Math.Max((int)Math.Round((war ? WarPortWarships : PeacePortWarships) * level), Share(FloorWarships(stage), realmPorts));

    public static int WantTransports(int stage, bool war, float level, int realmPorts) =>
        Math.Max((int)Math.Round((war ? WarPortTransports : PeacePortTransports) * level), Share(FloorTransports(stage), realmPorts));

    // 每港每月下水几艘
    public static float Pace(bool war) => war ? 2f : 1f;

    private static int Share(int floor, int ports) => (int)Math.Ceiling(floor / (float)Math.Max(1, ports));
}
