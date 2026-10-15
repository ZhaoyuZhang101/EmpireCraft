using System.Collections.Generic;

namespace EmpireCraft.Scripts.GameLibrary;

// 舰队旗舰(见 NavySystem)：四个舰种各一种船，照原版运兵船复制，贴图在 actors/boats/ec_warship_N
// (Tools/IconForge/ship_forge.py 体素建模渲染：楼船、远洋帆舰、蒸汽铁甲、现代舰艇)。
// 船型另立 boat_type_warship，不占原版码头的运兵船名额；只留"母港码头没了就自沉"一项决策，航向由舰队指挥
public static class EmpireCraftWarshipLibrary
{
    public static readonly string[] Ids = { "ec_warship_0", "ec_warship_1", "ec_warship_2", "ec_warship_3" };
    private static readonly float[] Health = { 1200f, 1600f, 2600f, 3600f };

    public static void init()
    {
        ActorAssetLibrary library = AssetManager.actor_library;
        string template = library.dict.ContainsKey("boat_transport_human") ? "boat_transport_human" : "$boat_transport$";
        if (!library.dict.ContainsKey(template)) return;
        for (int i = 0; i < Ids.Length; i++)
        {
            if (library.dict.ContainsKey(Ids[i])) continue;
            ActorAsset source = library.dict[template];
            ActorAsset asset = library.clone(Ids[i], template);
            // 原版复制不带不序列化的字段，"自带画法"等标记要等原版初始化收尾时才算；战船在那之后才复制，照运兵船补上，
            // 不然预加载按人物贴图去找(报 texture_path_main doesn't exist … heads_male)，船身也按人物贴图画不出来
            asset.has_override_sprite = asset.get_override_sprite != null;
            asset.has_override_avatar_frames = asset.get_override_avatar_frames != null;
            asset.has_avatar_prefab = !string.IsNullOrEmpty(asset.avatar_prefab);
            asset.color = source.color;
            asset.architecture_asset = source.architecture_asset;
            asset.name_locale = Ids[i];
            asset.boat_type = "boat_type_warship";
            asset.is_boat_transport = false;
            asset.decision_ids = new List<string> { "boat_check_existence" };
            asset.base_stats["health"] = Health[i];
            asset.base_stats["speed"] = 25f + i * 5f;
            asset.icon = "iconBoat";
        }
    }

    public static string IdFor(float tier) => Ids[tier >= 4f ? 3 : tier >= 2.5f ? 2 : tier >= 1.5f ? 1 : 0];
}
