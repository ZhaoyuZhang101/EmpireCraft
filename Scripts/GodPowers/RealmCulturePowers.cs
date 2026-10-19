using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 一键易俗的范围开关与复旧法理(用户 2026-10-19："一键变更对应文化的权能，从统治者到制度科技等全部变更，或者一键恢复法理的初始文化")：
//   · 举国易俗 / 举朝易俗(两个开关，同时只开一个)：开着时，易俗笔刷(见 CityCulturePower)点一座城，改它所属的整个国家 /
//     整个帝国(见 RealmCultureSystem)；都关着照旧只改这座城；
//   · 复旧法理：点一座城，它所属国家(在帝国里就是整个帝国)名下的法理都恢复初始文化(见 CultureService.RestoreInitialTitleCulture)。
public static class RealmCulturePowers
{
    public const string Kingdom = "realm_culture_kingdom", Empire = "realm_culture_empire", Restore = "restore_title_culture";

    private static bool On(string option) => PlayerConfig.dict.TryGetValue(option, out PlayerOptionData data) && data.boolVal;

    // 0 只改城、1 举国、2 举朝
    public static int Scope => On(Empire) ? 2 : On(Kingdom) ? 1 : 0;

    public static void Init()
    {
        foreach (string option in new[] { Kingdom, Empire })
        {
            if (!AssetManager.options_library.has(option))
                AssetManager.options_library.add(new OptionAsset { id = option, type = OptionType.Bool, default_bool = false });
            if (!PlayerConfig.dict.ContainsKey(option))
                PlayerConfig.instance.data.add(new PlayerOptionData(option) { boolVal = false });
            string other = option == Kingdom ? Empire : Kingdom;
            if (AssetManager.powers.get(option) == null)
                AssetManager.powers.add(new GodPower
                {
                    id = option,
                    name = option,
                    toggle_name = option,
                    toggle_action = _ => Exclusive(option, other)
                });
        }
        if (AssetManager.powers.get(Restore) == null)
            AssetManager.powers.add(new GodPower { id = Restore, name = Restore, click_action = RestoreTitles });
    }

    // 原版先调这里、再翻转开关值：这一个要打开时把另一个关掉
    private static void Exclusive(string option, string other)
    {
        if (On(option) || !PlayerConfig.dict.TryGetValue(other, out PlayerOptionData data) || !data.boolVal) return;
        data.boolVal = false;
        foreach (PowerButton button in PowerButton.toggle_buttons.ToList())
            if (button != null && button.godPower?.id == other) button.checkToggleIcon();
    }

    // 易俗笔刷在举国/举朝开着时走这里(见 CityCulturePower.Apply)
    public static bool ApplyRealm(City city, string culture)
    {
        Kingdom kingdom = city?.kingdom;
        string cultureName = culture.GetCultureTranslate();
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild)
        {
            WorldTip.showNow(LM.Get("city_culture_none"), false, "top", 3f);
            return false;
        }
        bool wholeEmpire = Scope == 2;
        Empire empire = kingdom.GetEmpire();
        (int kingdoms, int cities) = RealmCultureSystem.Convert(kingdom, culture, wholeEmpire);
        if (kingdoms == 0) return false;
        string text = wholeEmpire && empire != null
            ? string.Format(LM.Get("realm_culture_empire_done"), empire.GetEmpireName(), cultureName, kingdoms, cities)
            : string.Format(LM.Get("realm_culture_kingdom_done"), kingdom.GetKingdomName(), cultureName, cities);
        WorldTip.showNow(text, false, "top", 4f);
        return true;
    }

    // 复旧法理：所属国家(帝国就是整个帝国，连帝国法理)名下的法理都恢复初始文化
    private static bool RestoreTitles(WorldTile tile, string power)
    {
        City city = tile?.zone_city;
        Kingdom kingdom = city?.kingdom;
        if (city?.data == null || city.isRekt() || kingdom?.data == null || kingdom.isRekt())
        {
            WorldTip.showNow(LM.Get("city_culture_none"), false, "top", 3f);
            return false;
        }
        Empire empire = kingdom.GetEmpire();
        var titles = new HashSet<KingdomTitle>();
        IEnumerable<Kingdom> scope = empire?.kingdoms_list != null && !empire.isRekt()
            ? empire.kingdoms_list.Where(member => member?.data != null && !member.isRekt())
            : new[] { kingdom };
        foreach (Kingdom member in scope)
            foreach (KingdomTitle title in member.GetControlledTitles())
                if (title?.data != null && !title.isRekt()) titles.Add(title);
        if (empire != null && EmpireCoreManager.Get(empire) is EmpireCore core)
            foreach (KingdomTitle title in EmpireCoreManager.GetTitles(core))
                if (title?.data != null && !title.isRekt()) titles.Add(title);
        int restored = titles.Count(CultureService.RestoreInitialTitleCulture);
        string name = empire != null ? empire.GetEmpireName() : kingdom.GetKingdomName();
        WorldTip.showNow(restored > 0
                ? string.Format(LM.Get("restore_title_culture_done"), name, restored)
                : string.Format(LM.Get("restore_title_culture_none"), name), false, "top", 4f);
        if (restored > 0) EmpireCraft.Scripts.GameLibrary.EmpireCraftNamePlateLibrary.RequestRefresh();
        return restored > 0;
    }
}
