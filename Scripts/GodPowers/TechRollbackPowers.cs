using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 科技回退神力(用户 2026-11-07 "单独做个godpower来回退科技"；群友"保存当前地图城市边界的同时，能把文化全部退回原始时代""一个个科技
// 都成为原始，但问题很快他就反弹了")，放在文化与营建页的易俗一组：
//   · 科技回退：点一座城，城所属国家的文化(同科技窗口)退回上一个时代，再点再退，一路退到原始时代；
//   · 举世退回原始：点一下按钮预备，ConfirmSeconds 秒内再点一下确认，天下所有文化一齐退回原始时代。
// 城界、国家、帝国、人口照旧；退了什么、怎么不反弹见 TechRollbackSystem
public static class TechRollbackPowers
{
    public const string CityPowerId = "tech_rollback";
    public const string WorldPowerId = "tech_rollback_world";
    public static readonly string[] Ids = { CityPowerId, WorldPowerId };
    private const float ConfirmSeconds = 5f;
    private static float _armedAt = -100f;

    public static void Init()
    {
        if (AssetManager.powers.get(CityPowerId) != null) return;
        AssetManager.powers.add(new GodPower { id = CityPowerId, name = CityPowerId, click_action = ClickCity });
        AssetManager.powers.add(new GodPower { id = WorldPowerId, name = WorldPowerId, select_button_action = ClickWorld });
    }

    private static bool ClickCity(WorldTile tile, string power)
    {
        City city = tile?.zone_city;
        if (city?.data == null || city.isRekt())
        {
            Tip(LM.Get("tech_rollback_no_city"));
            return false;
        }
        string culture = TechnologySystem.GetCultureOf(city);
        if (!CultureService.IsValidCulture(culture))
        {
            Tip(LM.Get("tech_rollback_no_culture"));
            return false;
        }
        if (!TechnologySystem.IsEnabled)
        {
            Tip(LM.Get("tech_rollback_disabled"));
            return false;
        }
        TechRollbackResult result = TechRollbackSystem.StepBack(culture, city.kingdom);
        string name = culture.GetCultureTranslate();
        Tip(result.techs > 0 || result.to_era < result.from_era
            ? string.Format(LM.Get("tech_rollback_done"), name, TechnologySystem.EraName(result.to_era),
                TechnologySystem.EraName(result.from_era), result.techs, result.institutions, result.cities)
            : string.Format(LM.Get("tech_rollback_already"), name, result.institutions, result.cities), 4f);
        return result.Changed;
    }

    // 按钮：第一下预备，ConfirmSeconds 秒内第二下确认
    private static bool ClickWorld(string power)
    {
        if (World.world == null) return false;
        if (!TechnologySystem.IsEnabled)
        {
            Tip(LM.Get("tech_rollback_disabled"));
            return false;
        }
        float now = UnityEngine.Time.unscaledTime;
        if (now - _armedAt > ConfirmSeconds)
        {
            _armedAt = now;
            Tip(LM.Get("tech_rollback_world_confirm"), ConfirmSeconds);
            return false;
        }
        _armedAt = -100f;
        TechRollbackResult result = TechRollbackSystem.WorldToPrimitive();
        Tip(string.Format(LM.Get("tech_rollback_world_done"), result.cultures, result.techs, result.institutions, result.cities), 6f);
        return false;
    }

    private static void Tip(string text, float seconds = 3f) => WorldTip.showNow(text, false, "top", seconds);
}
