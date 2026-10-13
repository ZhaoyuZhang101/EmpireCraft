using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 撤城：点一座城，百姓与城里的人物迁到本国最近的城，城市当即废弃(见 CityDissolution)；
// 本国最后一座城：第一次点只提示会灭国，ConfirmSeconds 秒内再点同一座城就连同国家彻底清除
public static class CityDissolutionPower
{
    private const float ConfirmSeconds = 5f;
    private static long _armedCity = -1L;
    private static float _armedAt = -100f;

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
        bool last = CityDissolution.IsLastCity(city);
        bool confirmed = last && _armedCity == city.data.id && UnityEngine.Time.unscaledTime - _armedAt <= ConfirmSeconds;
        string kingdomName = city.kingdom?.GetKingdomName() ?? "";
        string reason = CityDissolution.Dissolve(city, confirmed);
        if (reason == "dissolve_city_last")
        {
            _armedCity = city.data.id;
            _armedAt = UnityEngine.Time.unscaledTime;
        }
        else _armedCity = -1L;
        string text = reason != null ? LM.Get(reason)
            : confirmed ? string.Format(LM.Get("dissolve_city_kingdom_gone"), name, kingdomName)
            : string.Format(LM.Get("dissolve_city_done"), name);
        WorldTip.showNow(text, false, "top", 3f);
        return reason == null;
    }
}
