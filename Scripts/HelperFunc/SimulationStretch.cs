namespace EmpireCraft.Scripts.HelperFunc;

// 算不过来时的降频(用户 2026-11-17 选"全面降频补量")：独立核心设定流速很高、实际又跟不上时，把一部分"每月一结、每秒一查"的活
// 拉成 Months 个月一结(见 .server/Program.cs 的 TuneStretch；平时、游戏里都是 1，什么都不变)。
// 动了的：城市的经济与人口增长(按月数补量)、各国的行为任务表(该不该称帝、立储、改政体这类判断，走得慢些)、
// 城市更新的间隔(攒下的时间一并交给它)、战略调度里各国按月的那几项检查、各国的军团月度征召(补员按月数放量，军饷、休整等按月补跑，
// 见 CorpsSystem.DueMonths)。
// 不动的：行军、战线分派与推进、决议
public static class SimulationStretch
{
    public static int Months = 1;
}
