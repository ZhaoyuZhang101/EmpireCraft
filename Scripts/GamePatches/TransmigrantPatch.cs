using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 穿越政体的原版接线：原版王国旗帜的纹样表末尾补上元老院旗、启明星旗(见 TransmigrantSystem.AppendBanners)
public class TransmigrantPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(TransmigrantPatch)).Patch(
            AccessTools.Method(typeof(KingdomBannerLibrary), nameof(KingdomBannerLibrary.get), new[] { typeof(string) }),
            postfix: new HarmonyMethod(GetType(), nameof(BannerGet)));
    }

    public static void BannerGet(BannerAsset __result) => TransmigrantSystem.AppendBanners(__result);
}
