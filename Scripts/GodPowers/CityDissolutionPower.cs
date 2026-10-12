using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 撤城：点一座城，百姓与城里的人物迁到本国最近的城，城市当即废弃(见 CityDissolution)
public static class CityDissolutionPower
{
    public static void init()
    {
        AssetManager.powers.add(new GodPower { id = "dissolve_city", name = "dissolve_city", click_action = Dissolve });
    }

    private static bool Dissolve(WorldTile tile, string power)
    {
        City city = tile?.zone_city;
        if (city?.data == null || city.isRekt())
        {
            WorldTip.showNow(LM.Get("dissolve_city_none"), false, "top", 3f);
            return false;
        }
        string name = city.GetCityName();
        string reason = CityDissolution.Dissolve(city);
        WorldTip.showNow(reason == null ? string.Format(LM.Get("dissolve_city_done"), name) : LM.Get(reason), false, "top", 3f);
        return reason == null;
    }
}
