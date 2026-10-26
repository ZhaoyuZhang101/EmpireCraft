using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 电子地图式的分级显示(用户 2026-10-16："放大后……有颜色但是地形细节会有，再大点就是交通网络，再放大是更细的交通网络，
// 最后变成(街区)这个样子"，选定四级)。按一格占多少屏幕像素分级：
//   ① 远景(一格不到 1 像素)：保留国色和一格一像素的城市粗图(见 CityPlanOverlay)，不叠浅色地形层(平滑与否都不叠：以前非平滑时盖一层
//      不透明度 0.72 的浅色地形底，拉远国色被冲得发白，2026-10-19 撤掉)；
//   ② 中景(1~4.5)：出现干线(官道、公路、柏油公路，见 RoadOverlay)；城市保持粗图；
//   ③ 近景(4.5~8)：全部道路到土路、驿道，城市精细层(街巷、建筑，见 CityDetailOverlay)；
//   ④ 街区(8 以上)：街道名、建筑名、小区名标注(见 MapLabelOverlay)。
// 道路在交界处约两成的范围里渐变；缩放只控制精细城市层，底图粗图始终保留。
public static class MapZoomTier
{
    // 近景、街区门槛原为 2.5、6：常用的缩放(一格两三个像素)城市细节就全冒出来，只有拉到最远才是简图
    // (用户 2026-10-17 "为什么只有我放到最大才省略显示城市，就不能正常大小下就省略细节吗")，调高到 4.5、8
    public const float Mid = 1f, Near = 4.5f, Street = 8f;

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
