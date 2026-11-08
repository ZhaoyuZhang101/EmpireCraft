using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国号不用地名(用户 2026-11-08 截图"岭西朝"："为什么华夏会出现省份或者城市名为帝国名的情况""这种应该是绝对避免的"，
// "国号应当和法理绑死，帝国号也是，除了美号")：
//   · 帝国号只能是所绑帝国法理的名字、自家发家的王国法理名，或美号；城名、行政区(道、省、路)名一律不当帝国号；
//   · 王国法理照旧可以叫地名(汉代有济南国、河间国)，王国国号跟着法理走，只是称帝时不拿它当帝国号；
//   · 王国法理生成时名字取自城名、或当时所属行政区的名字，记下 place_name，那个行政区没了也认得出来。
public static partial class RealmNamingSystem
{
    // 这个名字是不是地名：现存的城名、现存行政区的名字、照地名起的王国法理名
    public static bool IsPlaceName(string name)
    {
        string plain = EmpireCoreManager.NormalizeCoreName(name);
        if (string.IsNullOrWhiteSpace(plain) || World.world == null) return false;
        bool Same(string other) => string.Equals(EmpireCoreManager.NormalizeCoreName(other), plain, StringComparison.Ordinal);
        foreach (City city in World.world.cities?.list ?? Enumerable.Empty<City>())
            if (city?.data != null && !city.isRekt() && Same(city.GetCityName())) return true;
        foreach (Kingdom kingdom in World.world.kingdoms?.list ?? Enumerable.Empty<Kingdom>())
            if (kingdom?.data != null && !kingdom.isRekt() && kingdom.IsAdministrativeKingdomType() && Same(kingdom.EnsureKingdomCoreName()))
                return true;
        return ModClass.KINGDOM_TITLE_MANAGER != null &&
               ModClass.KINGDOM_TITLE_MANAGER.Any(title => title?.data != null && title.data.place_name && Same(title.data.name));
    }

    // 王国法理名能不能当帝国号
    public static bool PlaceTitle(KingdomTitle title) =>
        title?.data != null && (title.data.place_name || IsPlaceName(title.data.name));
}
