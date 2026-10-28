using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图的比例尺：一块区块合多少公里(随存档保存；不是世界地图为 0)。
// 行政控制力的治理半径按区块算、照原版地图调的，世界地图一块区块七八十公里，满控制力能铺到两三千公里外，
// 处处九成以上(用户 2026-10-29："控制力也全是百分之90以上的全满，完全没有体现地形的影响")；
// 世界地图上按比例尺把半径折回去(见 AdministrativeControlSystem.Reach)
public static class WorldMapScale
{
    // 原版地图上一块区块大致合多少公里(治理半径就是照这个尺度调的)
    private const float ReferenceKm = 30f;

    public static float ZoneKm { get; set; }

    public static float ReachFactor => ZoneKm <= 0f ? 1f : Mathf.Clamp(ReferenceKm / ZoneKm, 0.15f, 1f);

    // 开局：区块 8 格，经度一度约 111 公里，按中纬度折一点
    public static void SetFromFrame(float degreesPerTile) => ZoneKm = degreesPerTile * 8f * 111f * 0.85f;
}
