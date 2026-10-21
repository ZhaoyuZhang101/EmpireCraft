using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 水土的窗口行(城、王国窗口，见 CityWindowPatch、KingdomWindowPatch)：
//   · 城：旱地还是农地(领土陆地里旱地占几成)、这座城的境况(水土相宜/离草几年/不服水土几年、第几档)，到档的另起一行写折损；
//   · 王国：按所属政权(帝国成员看帝国核心那一份)写生活方式、直辖城与旱地城数、离草或不服水土的境况，到档的另起一行写折损。
// 新主人还没结算(每政权一年一次)的城只写地；政权还没结算的不写。
public static partial class ShuiTuSystem
{
    public const string ColorCalm = "#B8C6CC", ColorTiming = "#E6A166", ColorStrained = "#E66B66";

    private static string TierText(int tier) =>
        tier <= 0 ? LM.Get("shuitu_tier_none") : string.Format(LM.Get("shuitu_tier"), tier);

    private static string FitText(Fit fit) => LM.Get(fit switch
    {
        Fit.Nomad => "shuitu_fit_nomad",
        Fit.Both => "shuitu_fit_both",
        Fit.Farm => "shuitu_fit_farm",
        _ => "shuitu_fit_none"
    });

    // 城：line 写地与境况，effect 写到档的折损(没到档为空)；color 按境况(相宜、在计未到档、到档)
    public static bool DescribeCity(City city, out string line, out string effect, out string color)
    {
        line = effect = null;
        color = ColorCalm;
        if (!Valid(city)) return false;
        DryEntry ground = Dry(city);
        string land = string.Format(LM.Get(ground.Dry ? "shuitu_city_dry" : "shuitu_city_farm"), ground.Share * 100f);
        string state;
        if (!Lookup(city, out CityInfo info, out UnitInfo unit)) state = LM.Get("shuitu_state_pending");
        else if (unit.Fit == Fit.Nomad && unit.OffGrass && !info.Dry)
        {
            state = string.Format(LM.Get("shuitu_state_offgrass"), unit.OffYears, TierText(unit.OffTier));
            color = unit.OffTier > 0 ? ColorStrained : ColorTiming;
            if (unit.OffTier > 0)
                effect = string.Format(LM.Get("shuitu_effect_offgrass_city"), NomadLevy(city.kingdom, unit.OffTier),
                    StabilityFor(unit.OffTier));
        }
        else if (unit.Fit == Fit.Farm && info.Dry)
        {
            // 本族为主、占满 AdaptYears 年的城逐年退档(见 UnfitTierOf，用户 2026-10-21 选"水土收窄")：退了档的写"渐服水土"，
            // 退完的写"已服水土"——以前只有"不服水土 N 年，未到档"，看不出是还没到档还是已经适应
            bool adapting = info.Tier < TierOf(info.Years);
            state = adapting && info.Tier <= 0 ? string.Format(LM.Get("shuitu_state_adapted"), info.Years)
                : string.Format(LM.Get(adapting ? "shuitu_state_adapting" : "shuitu_state_unfit"), info.Years, TierText(info.Tier));
            color = info.Tier > 0 ? ColorStrained : adapting ? ColorCalm : ColorTiming;
            if (info.Tier > 0)
                effect = string.Format(LM.Get("shuitu_effect_unfit_city"), SettledLevyFor(info.Tier), StabilityFor(info.Tier),
                    SettledTaxFor(info.Tier));
        }
        else state = LM.Get(unit.Fit == Fit.None ? "shuitu_state_exempt" : "shuitu_state_fit");
        line = string.Format(LM.Get("shuitu_line"), land, state);
        return true;
    }

    // 王国(按所属政权)：line 写生活方式与直辖城数，state 写境况，effect 写到档的折损(没到档为空)
    public static bool DescribeKingdom(Kingdom kingdom, out string line, out string state, out string effect, out string color)
    {
        line = state = effect = null;
        color = ColorCalm;
        UnitInfo unit = UnitInfoOf(kingdom);
        if (unit == null) return false;
        line = string.Format(LM.Get("shuitu_kingdom_line"), FitText(unit.Fit), unit.Direct, unit.Dry);
        if (unit.Fit == Fit.Nomad)
        {
            if (!unit.OffGrass) state = LM.Get("shuitu_state_steppe");
            else
            {
                state = string.Format(LM.Get("shuitu_state_offgrass"), unit.OffYears, TierText(unit.OffTier));
                color = unit.OffTier > 0 ? ColorStrained : ColorTiming;
                if (unit.OffTier > 0)
                    effect = string.Format(LM.Get("shuitu_effect_offgrass_kingdom"), NomadLevy(kingdom, unit.OffTier),
                        StabilityFor(unit.OffTier), NomadCombatFor(unit.OffTier));
            }
        }
        else if (unit.Fit == Fit.Farm && unit.Unfit > 0)
        {
            state = string.Format(LM.Get("shuitu_state_unfit_kingdom"), unit.Unfit, unit.UnfitYears, TierText(unit.UnfitTop));
            color = unit.UnfitTop > 0 ? ColorStrained : ColorTiming;
            if (unit.UnfitTop > 0)
                effect = string.Format(LM.Get("shuitu_effect_unfit_kingdom"), SettledLevyFor(unit.UnfitTop),
                    StabilityFor(unit.UnfitTop), SettledTaxFor(unit.UnfitTop), SettledCombatFor(unit.UnfitTop));
        }
        else state = LM.Get(unit.Fit == Fit.None ? "shuitu_state_exempt" : "shuitu_state_fit");
        return true;
    }
}
