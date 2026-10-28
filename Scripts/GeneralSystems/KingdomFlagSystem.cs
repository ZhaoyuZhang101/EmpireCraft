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
//   · 穿越政权有自己的旗(见 TransmigrantSystem)，旗帜框里挂它的徽章。
// 旗是 3:2 正式国旗的像素图(Tools/IconForge/flag_formal.py，ui/flags/，最近邻显示保留像素感)，原版旗帜的底、纹两层都乘国家颜色，
// 这里换成整面旗、不再染色(见 KingdomFlagPatch)。
// 旗帜框里挂的是圆形徽章(用户 10-29 "这个国旗的大小太突兀了，建议都改成圆形徽章，图案不变""直接就切成圆形吗")：
// 不是把成品旗切圆，而是 Tools/IconForge/badge_forge.py 用同一面旗的画法重新排进圆里(旗面底色与纹样照旧，方边框换成联珠环)，
// 出图 ui/flags/badge_<旗名>；穿越政权的星拳旗(元老院旗)、启明星旗(国旗)也有徽章(badge_aosong_*)，按它旗帜框里挂的纹样取
public static class KingdomFlagSystem
{
    private const string Folder = "ui/flags/";
    private const string SenateIcon = "ui/banners/aosong_senate";
    private static readonly Dictionary<string, Sprite> Sprites = new();

    public static Sprite FlagOf(Kingdom kingdom) => FlagName(kingdom) is string name ? Load(name) : null;

    // 旗名(culture_<文化>、ideology_<理念>)；原始政体、部落、穿越政权等没有国旗的返回 null
    private static string FlagName(Kingdom kingdom)
    {
        if (!Flies(kingdom) || TransmigrantSystem.IsTransmigrant(kingdom)) return null;
        Kingdom realm = kingdom.GetEmpire()?.CoreKingdom ?? kingdom;
        if (TribalFissionService.IsTribal(kingdom) || RegimeManager.IsPrimitive(realm.GetRegime()?.type)) return null;
        if (ModernStability.IsModern(kingdom))
        {
            PartyIdeology ideology = IdeologyFamilies.StateIdeology(realm);
            if (ideology < PartyIdeology.Confucianism && Load("ideology_" + ideology) != null) return "ideology_" + ideology;
        }
        string culture = CultureService.GetRealmCulture(realm);
        return string.IsNullOrEmpty(culture) || Load("culture_" + culture) == null ? null : "culture_" + culture;
    }

    private static bool Flies(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && !kingdom.isNeutral() &&
        !Compatibility.AncientWarfareCompatibility.Owns(kingdom);

    // 旗帜框里挂的徽章；没有徽章图的照整面旗
    public static Sprite BadgeOf(Kingdom kingdom)
    {
        if (Flies(kingdom) && TransmigrantSystem.IsTransmigrant(kingdom))
            return Load(FliesSenateFlag(kingdom) ? "badge_aosong_senate" : "badge_aosong_national");
        string name = FlagName(kingdom);
        return name == null ? null : Load("badge_" + name) ?? Load(name);
    }

    // 穿越政权旗帜框里挂的是星拳纹(元老院旗)还是启明星纹(国旗，见 TransmigrantSystem.ApplyFlag)
    private static bool FliesSenateFlag(Kingdom kingdom)
    {
        BannerAsset banner = AssetManager.kingdom_banners_library.get(kingdom.getActorAsset()?.banner_id);
        int index = kingdom.data.banner_icon_id;
        return banner?.icons != null && index >= 0 && index < banner.icons.Count && banner.icons[index] == SenateIcon;
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
