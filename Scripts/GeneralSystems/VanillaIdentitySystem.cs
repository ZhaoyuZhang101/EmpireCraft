using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 模组不需要反复派生原版语言与亚种；现有存档对象不合并、不删除。
// 保留每物种第一门语言(原版宗教创建依赖它)，后来的单位直接复用。
public static class VanillaIdentitySystem
{
    public static bool BlocksPlot(string id) => id is "language_divergence" or "clan_ascension";

    public static bool TryReuseLanguage(Actor actor)
    {
        if (actor?.asset?.civ != true || actor.hasLanguage() ||
            actor.subspecies?.has_advanced_communication != true || World.world?.languages == null) return false;
        foreach (Language language in World.world.languages)
        {
            if (language?.data == null || language.isRekt() ||
                !string.Equals(language.species_id, actor.asset.id, StringComparison.Ordinal)) continue;
            actor.joinLanguage(language);
            return true;
        }
        return false;
    }

    public static void ApplyPlotPolicy(PlotAsset plot)
    {
        if (plot == null) return;
        if (BlocksPlot(plot.id))
        {
            plot.check_is_possible = _ => false;
            plot.check_should_continue = _ => false;
            plot.check_can_be_forced = _ => false;
            plot.try_to_start_advanced = (_, _, _) => false;
            plot.action = _ => false;
            plot.post_action = null;
        }
        else if (plot.id == "new_language")
        {
            // 多支决议同时起步时，后完成的也复用先完成的语言，不再额外创建。
            var action = plot.action;
            plot.action = actor => actor?.hasLanguage() == true || TryReuseLanguage(actor) || action?.Invoke(actor) == true;
        }
    }
}
