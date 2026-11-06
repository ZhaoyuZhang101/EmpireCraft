using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using static EmpireCraft.Scripts.GeneralSystems.EthnicRankRules;

namespace EmpireCraft.Scripts.GeneralSystems;

// 族群等第(用户 2026-11-06 选"统一「族群等第」框架"，不叫种姓)：征服王朝按文化、阶层、归附先后给治下之民分等，
// 一套引擎，规则按方案换：
//   · 四等人(FourClasses)：统治族群施行了游牧线的「四等人制」(特性 ethnic_ranks)、已入主采纳属地制度的征服王朝。
//     国族(统治文化) / 色目(其余异族) / 汉人(属地文化一系、定制时已归附的城) / 南人(定制以后才打下的城)；
//   · 旗民分治(BannerCivil)：八旗多元制(见 BannerSystem)自带。旗人(旗人阶层，不论文化；本族) / 藩部(其余异族，因俗而治) /
//     民人(属地文化一系)；没有第四等。
// 各等的差别(数值见 EthnicRankRules)：
//   · 赋税：按城中各等人口加权的税率折扣(国族、藩部从轻，南人重赋；旗人的豁免归八旗)；
//   · 稳定：等第低的城不服，四等人重、旗民分治轻；
//   · 择官：帝国核心的官先尽上等——四等人七成半的缺给国族色目，满汉复职各半；
//   · 征兵：四等人的禁军(中央的军)不收南人；汉人、南人禁执兵器，团练打折；
//   · 融合：四等人每年融合少一分(防汉化)。
// 融合完成(统治文化就是制度文化)、不再是征服王朝时等第作废。
public static class EthnicRankSystem
{
    public const string Feature = "ethnic_ranks";

    public static Scheme SchemeOf(Empire empire)
    {
        if (empire?.data == null || empire.CoreKingdom?.data == null || empire.CoreKingdom.isRekt()) return Scheme.None;
        if (BannerSystem.Practices(empire)) return Scheme.BannerCivil;
        if (!CompositeEmpireService.IsComposite(empire)) return Scheme.None;
        string ruling = CompositeEmpireService.GetRulingCulture(empire);
        if (string.Equals(ruling, CompositeEmpireService.GetInstitutionalCulture(empire), StringComparison.Ordinal)) return Scheme.None;
        return InstitutionSystem.HasFeature(ruling, Feature) ? Scheme.FourClasses : Scheme.None;
    }

    public static Scheme SchemeOf(Kingdom kingdom) =>
        kingdom?.data == null || kingdom.isRekt() ? Scheme.None : SchemeOf(kingdom.GetEmpire());

    #region 分等

    // 这座城算不算定制时已归附(四等人的汉人与南人之分)
    public static bool EarlySubject(City city, Empire empire) =>
        city?.data != null && empire?.CoreKingdom != null && city.GetOrCreate().rank_early_core == empire.CoreKingdom.id;

    public static int RankOf(Scheme scheme, Empire empire, City city, PopGroup group)
    {
        string culture = string.IsNullOrEmpty(group?.culture) ? CultureService.GetMainCulture(city, initialize: false) : group.culture;
        return RankOf(scheme, empire, city, culture, group?.social_class ?? SocialClass.Peasant);
    }

    private static int RankOf(Scheme scheme, Empire empire, City city, string culture, SocialClass socialClass)
    {
        if (scheme == Scheme.BannerCivil && socialClass == SocialClass.Banner) return Ruling;
        if (string.Equals(culture, CompositeEmpireService.GetRulingCulture(empire), StringComparison.Ordinal)) return Ruling;
        if (CultureService.SameKin(culture, CompositeEmpireService.GetInstitutionalCulture(empire)))
            return scheme == Scheme.FourClasses && !EarlySubject(city, empire) ? Late : Early;
        return Allied;
    }

    // 一城四等各占几成(背景人口；没有人口账的按主流文化整城算)
    public static float[] Shares(City city, out Scheme scheme)
    {
        var shares = new float[Count];
        Empire empire = city?.kingdom?.GetEmpire();
        scheme = SchemeOf(empire);
        if (scheme == Scheme.None || city?.data == null || city.isRekt()) return shares;
        CityPopulationData data = CityPopulationSystem.Get(city);
        float total = 0f;
        foreach (PopGroup group in data?.groups ?? new List<PopGroup>())
        {
            float size = group.Background;
            if (size <= 0f) continue;
            shares[RankOf(scheme, empire, city, group)] += size;
            total += size;
        }
        if (total <= 0f)
        {
            shares[RankOf(scheme, empire, city, CultureService.GetMainCulture(city, initialize: false), SocialClass.Peasant)] = 1f;
            return shares;
        }
        for (int rank = 0; rank < Count; rank++) shares[rank] /= total;
        return shares;
    }

    // 城里人数最多的一等
    public static int MainRank(City city, out Scheme scheme)
    {
        float[] shares = Shares(city, out scheme);
        int best = Ruling;
        for (int rank = 1; rank < Count; rank++)
            if (shares[rank] > shares[best]) best = rank;
        return best;
    }

    // 四等人的汉人、南人只在属地是华夏一系时这么叫；征服别的文明叫先附、新附
    public static string RankName(Scheme scheme, int rank, Empire empire = null)
    {
        string key = $"ethnic_rank_{scheme}_{rank}";
        if (scheme == Scheme.FourClasses && rank >= Early && empire != null &&
            InstitutionSystem.GetCultureLine(CompositeEmpireService.GetInstitutionalCulture(empire)) != "Huaxia")
            key += "_generic";
        return LM.Get(key);
    }

    public static string SchemeName(Scheme scheme) => LM.Get($"ethnic_rank_scheme_{scheme}");

    #endregion

    #region 各项差别

    public static float TaxFactor(City city)
    {
        float[] shares = Shares(city, out Scheme scheme);
        return EthnicRankRules.TaxFactor(scheme, shares);
    }

    // 稳定度增减(正为加)，detail 写进稳定度说明
    public static float StabilityShift(City city, out string detail)
    {
        detail = null;
        float[] shares = Shares(city, out Scheme scheme);
        float shift = Stability(scheme, shares);
        if (Math.Abs(shift) < 0.5f) return 0f;
        int main = 0;
        for (int rank = 1; rank < Count; rank++)
            if (shares[rank] > shares[main]) main = rank;
        detail = string.Format(LM.Get("ethnic_rank_stability_detail"), SchemeName(scheme), RankName(scheme, main, city.kingdom?.GetEmpire()),
            shares[main]);
        return shift;
    }

    public static float MilitiaFactor(City city)
    {
        float[] shares = Shares(city, out Scheme scheme);
        return EthnicRankRules.MilitiaFactor(scheme, shares);
    }

    // 帝国核心择官这一次要不要先尽上等(按配额掷一次)
    public static bool FavorsUpper(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom) return false;
        Scheme scheme = SchemeOf(empire);
        return scheme != Scheme.None && EthnicRankRules.FavorsRuling(scheme, Randy.randomFloat(0f, 1f));
    }

    // 上等之人：四等人是国族、色目；满汉复职的满缺只给国族
    public static bool IsUpper(Kingdom kingdom, Actor actor)
    {
        Empire empire = kingdom?.GetEmpire();
        Scheme scheme = SchemeOf(empire);
        if (scheme == Scheme.None || actor == null) return false;
        string culture = CultureService.GetActorCulture(actor);
        if (string.Equals(culture, CompositeEmpireService.GetRulingCulture(empire), StringComparison.Ordinal)) return true;
        return scheme == Scheme.FourClasses && CultureService.IsValidCulture(culture) &&
               !CultureService.SameKin(culture, CompositeEmpireService.GetInstitutionalCulture(empire));
    }

    // 征兵：四等人的中央禁军不收南人
    public static bool MayRecruit(Kingdom kingdom, CorpsData corps, City source, PopGroup group)
    {
        if (corps == null || source == null || group == null) return true;
        if ((CityPopulationSystem.ArmyPost)corps.post != CityPopulationSystem.ArmyPost.Central && !corps.manual) return true;
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom) return true;
        Scheme scheme = SchemeOf(empire);
        return scheme == Scheme.None || CentralRecruits(scheme, RankOf(scheme, empire, source, group));
    }

    // 融合每年少几分
    public static int IntegrationDrag(Empire empire) => SchemeOf(empire) == Scheme.FourClasses ? FourIntegrationDrag : 0;

    #endregion

    #region 每年

    // 每年(帝国例行检查)：四等人定制那年把治下的城都记作已归附(汉人)，以后打下的是南人；等第作废时清掉
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.CoreKingdom?.data == null || empire.CoreKingdom.isRekt()) return;
        Scheme scheme = SchemeOf(empire);
        if (scheme == Scheme.None)
        {
            empire.data.ethnic_ranks_since = -1d;
            return;
        }
        if (empire.data.ethnic_ranks_since >= 0d) return;
        empire.data.ethnic_ranks_since = World.world.getCurWorldTime();
        long core = empire.CoreKingdom.id;
        int marked = 0;
        foreach (Kingdom kingdom in empire.kingdoms_list?.ToList() ?? new List<Kingdom>())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                city.GetOrCreate().rank_early_core = core;
                marked++;
            }
        }
        string text = string.Format(LM.Get("ethnic_rank_established_history"), empire.GetEmpireName(), SchemeName(scheme));
        empire.RecordHistory(directContent: text, kingdomId: core);
        if (scheme == Scheme.FourClasses) TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        LogService.LogInfo($"[EmpireCraft][族群等第] {empire.GetEmpireName()} 定{scheme}，{marked} 城记作已归附");
    }

    #endregion

    public sealed class Summary
    {
        public Scheme scheme;
        public readonly float[] people = new float[Count];
        public float total;
    }

    public static Summary SummaryOf(Empire empire)
    {
        var summary = new Summary { scheme = SchemeOf(empire) };
        if (summary.scheme == Scheme.None) return summary;
        foreach (Kingdom kingdom in empire.kingdoms_list?.ToList() ?? new List<Kingdom>())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                float people = CityPopulationSystem.GetBackgroundTotal(city);
                float[] shares = Shares(city, out _);
                for (int rank = 0; rank < Count; rank++) summary.people[rank] += people * shares[rank];
                summary.total += people;
            }
        }
        return summary;
    }
}
