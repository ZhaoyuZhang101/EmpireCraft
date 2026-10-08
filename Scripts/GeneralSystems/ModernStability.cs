using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// 现代国家治理能力：警察、常备军、行政与国家认同让现代国家远比前现代政权稳定。
//   · 叛乱门槛：现代政体(按所在帝国的核心国判定)每次满足叛乱条件时，只有 RebellionFactor 的概率真正起事——
//     城主叛乱、阶层起义、土地起义、现代革命、WarBox 成员国起兵、民族独立运动都经过这道门槛；
//   · 治理忠诚：现代政体的城市忠诚 +Loyalty(EmpireCraftLoyaltyLibrary "modern_state_capacity")。
// 军阀时期的统一进程(民心所向、兵败起义)是内战的一部分，不受此限。
public static class ModernStability
{
    public const float RebellionFactor = 0.3f;
    public const int Loyalty = 15;

    public static bool IsModern(Kingdom kingdom)
    {
        Kingdom realm = kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;
        return realm?.GetRegime()?.type == RegimeType.Modern;
    }

    // 开国保护、新地方稳定期和当前正统统一在改动归属前判断。
    public static bool PassRebellionGate(Kingdom kingdom) =>
        RebellionSystem.CanAttempt(kingdom) && UnityEngine.Random.value < RebellionSystem.ChanceFactor(kingdom);
}
