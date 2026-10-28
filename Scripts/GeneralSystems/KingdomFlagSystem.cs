using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国旗(用户 2026-10-29："给我有写过的文明都弄一个符合现实世界的古典国旗，古典时代都用这个""现代就优先使用理念作为国旗"
// "酋长国或者原始国家就只用随机的原版图")：
//   · 原始政体(部落、酋长)照原版随机旗；
//   · 近代政体按政权信奉的理念挂理念旗(没有对应理念旗的照文明旗)；
//   · 其余各国挂本文明的古典旗；
//   · 穿越政权有自己的旗(见 TransmigrantSystem)，不动。
// 旗是 3:2 正式国旗的像素图(Tools/IconForge/flag_formal.py，ui/flags/，最近邻显示保留像素感)，原版旗帜的底、纹两层都乘国家颜色，
// 这里换成整面旗、不再染色(见 KingdomFlagPatch)
public static class KingdomFlagSystem
{
    private const string Folder = "ui/flags/";
    private static readonly Dictionary<string, Sprite> Sprites = new();

    public static Sprite FlagOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.isNeutral()) return null;
        if (Compatibility.AncientWarfareCompatibility.Owns(kingdom) || TransmigrantSystem.IsTransmigrant(kingdom)) return null;
        Kingdom realm = kingdom.GetEmpire()?.CoreKingdom ?? kingdom;
        if (TribalFissionService.IsTribal(kingdom) || RegimeManager.IsPrimitive(realm.GetRegime()?.type)) return null;
        if (ModernStability.IsModern(kingdom))
        {
            PartyIdeology ideology = IdeologyFamilies.StateIdeology(realm);
            if (ideology < PartyIdeology.Confucianism && Load("ideology_" + ideology) is Sprite modern) return modern;
        }
        string culture = CultureService.GetRealmCulture(realm);
        return string.IsNullOrEmpty(culture) ? null : Load("culture_" + culture);
    }

    private static Sprite Load(string name)
    {
        if (Sprites.TryGetValue(name, out Sprite cached)) return cached;
        Sprite sprite = null;
        try
        {
            sprite = SpriteTextureLoader.getSprite(Folder + name);
        }
        catch
        {
            // 没有这面旗：照原版
        }
        if (sprite?.texture != null) sprite.texture.filterMode = FilterMode.Point;
        Sprites[name] = sprite;
        return sprite;
    }
}
