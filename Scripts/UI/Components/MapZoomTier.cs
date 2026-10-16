using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 电子地图式的分级显示(用户 2026-10-16："放大后……有颜色但是地形细节会有，再大点就是交通网络，再放大是更细的交通网络，
// 最后变成(街区)这个样子"，选定四级)。按一格占多少屏幕像素分级，各图层只调透明度(不重画底图)，切换不卡：
//   ① 远景(一格不到 1 像素)：国色半透明盖在地形上——林地绿、荒漠米黄、雪山白，国界清楚(见 TerrainOverlay)；
//   ② 中景(1~2.5)：出现干线(官道、公路、柏油公路，见 RoadOverlay)；
//   ③ 近景(2.5~6)：全部道路到土路、驿道，城市精细层(街巷、建筑，见 CityDetailOverlay)；
//   ④ 街区(6 以上)：街道名、建筑名、小区名标注(见 MapLabelOverlay)。
// 交界处约两成的范围里渐变，不会一下子跳。
public static class MapZoomTier
{
    public const float Mid = 1f, Near = 2.5f, Street = 6f;

    public static float PixelsPerTile(Camera camera) =>
        camera == null || camera.orthographicSize <= 0f ? 0f : Screen.height / (2f * camera.orthographicSize);

    public static float Current => PixelsPerTile(World.world?.camera);

    // 0~1：拉近越过 threshold 的程度(threshold 上下各两成里渐变)
    public static float Past(float threshold)
    {
        float ppt = Current;
        return Mathf.Clamp01((ppt - threshold * 0.8f) / (threshold * 0.4f));
    }
}
