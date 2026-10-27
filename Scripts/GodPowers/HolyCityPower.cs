using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 指定圣地(用户 2026-10-27："给我的权能让我能指定宗教圣地在哪里")：点一座城，它就成了它所奉宗教的圣地。
// 教廷是据有圣地的那一国(PapacySystem.Papacy)，教宗跟着换；教廷所在的天下也随圣地走，
// 已授的天下之名每年核对时改授新天下之名(RealmNamingSystem.Rehome)
public static class HolyCityPower
{
    public const string Id = "religion_holy_city";

    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = Id,
            name = Id,
            click_action = Action
        });
    }

    private static bool Action(WorldTile pTile, string pPower)
    {
        City city = pTile?.zone?.city;
        if (city?.data == null || city.isRekt())
        {
            WorldTip.showNow(LM.Get("religion_holy_city_none"), false, "top", 3f);
            return false;
        }
        Religion religion = city.hasReligion() ? city.religion : null;
        if (religion?.data == null || religion.isRekt())
        {
            WorldTip.showNow(string.Format(LM.Get("religion_holy_city_no_faith"), city.GetCityName()), false, "top", 3f);
            return false;
        }
        if (religion.GetCity() == city)
        {
            WorldTip.showNow(string.Format(LM.Get("religion_holy_city_same"), city.GetCityName(), religion.name), false, "top", 3f);
            return false;
        }
        ReligionSphere.SetHolyCity(religion, city);
        WorldTip.showNow(string.Format(LM.Get("religion_holy_city_done"), religion.name, city.GetCityName()), false, "top", 3f);
        return true;
    }
}
