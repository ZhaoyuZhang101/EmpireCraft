using System.IO;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 高分辨率图标(Tools/IconForge/imperial_forge.py 生成的 56×56 写实图标)改用双线性过滤。
// NML 载入模组图片一律用最近邻(Point)，原版 28×28 像素图标这样才清晰；但平滑绘制的大图
// 在界面里缩小显示时，最近邻会出锯齿。这里在模组初始化时把这些图标的贴图改成 Bilinear。
// SpriteTextureLoader 会缓存 Sprite，所以改一次之后各处用到的都是平滑版本。
public static class HiResIconFilter
{
    // 生成高分辨率图标的目录(相对 GameResources)，以及单独的几个文件
    private static readonly string[] Folders = { "ui/icons/plots", "ui/icons/cultures", "ui/icons/actor_traits" };
    private static readonly string[] Files =
        { "TabBureau", "TabConstitution", "TabDynasty", "TabSetting", "TabInstitutions" };
    private const int MinSize = 48;

    public static void Apply()
    {
        string root = Path.Combine(ModClass._declare.FolderPath, "GameResources");
        int count = 0;
        foreach (string folder in Folders)
        {
            string dir = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.GetFiles(dir, "*.png"))
                if (Smooth(SpriteTextureLoader.getSprite(folder + "/" + Path.GetFileNameWithoutExtension(file))))
                    count++;
        }
        foreach (string file in Files)
            if (Smooth(SpriteTextureLoader.getSprite(file))) count++;
        LogService.LogInfo($"[EmpireCraft] 高分辨率图标改用平滑过滤：{count} 个");
    }

    // 只处理够大的图(原版风格的 28×28 像素图标保持最近邻)
    public static bool Smooth(Sprite sprite)
    {
        Texture2D texture = sprite?.texture;
        if (texture == null || texture.width < MinSize) return false;
        texture.filterMode = FilterMode.Bilinear;
        return true;
    }
}
