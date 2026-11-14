// 谋划对象(原版叫 plot)在服务器上只是个牌子：记着发起人、目标、进度，给模组各处查(actor.plot、plot.target_war……)。
// 模组决议怎么发起、推进、办成或作罢都在模组自己的 DecisionSystem 里(Scripts/GeneralSystems/Decisions)，游戏与服务器同一套；
// 这里不做任何推进。
//# hand Plot: newPlot finishPlot getAsset getAuthor getProgressMax getProgressMod isActive isCancelled isFinished isSameType updateProgressTarget checkInitiatorAndTargets getState
//# hand PlotAsset: canBeDoneByRole checkIsPossible isAllowedByWorldLaws
//# hand PlotManager: newPlot isPlotTypeAlreadyRunning
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Server;

public partial class PlotData
{
    public string plot_type_id;
    public long founder_id;
}

public partial class Plot
{
    private PlotState _state = PlotState.Active;
    private Actor _author;
    public float progress_target;

    [MethodImpl(P.N)]
    public void newPlot(Actor pAuthor, PlotAsset pAsset, bool pForced)
    {
        _plot_asset = pAsset;
        _author = pAuthor;
        data.plot_type_id = pAsset.id;
        data.founder_id = pAuthor.getID();
        data.forced = pForced;
        setName(pAsset.id);
        pAuthor.setPlot(this);
        if (!units.Contains(pAuthor)) units.Add(pAuthor);
        last_update_progress = World.world.getCurWorldTime();
    }

    public PlotState getState() => _state;
    public bool isActive() => _state == PlotState.Active && isAlive();
    public bool isCancelled() => _state == PlotState.Cancelled;
    public bool isFinished() => _state == PlotState.Finished;
    public PlotAsset getAsset() => _plot_asset;
    public bool isSameType(PlotAsset pAsset) => pAsset != null && pAsset == _plot_asset;
    public Actor getAuthor() => _author ??= World.world.units.get(data.founder_id);
    public float getProgressMax() => _plot_asset?.progress_needed ?? 0f;
    public float getProgressMod() => getProgressMax() <= 0f ? 1f : data.progress_current / getProgressMax();

    // 服务器上没有原版人物 AI 推进谋划；模组的补丁(见 DecisionPatch、WarReadinessPatch)挂在这里
    [MethodImpl(P.N)]
    public bool updateProgressTarget(Actor pActor, float pIntelligence) => false;

    // 了结：发起人放下牌子，从名册里撤掉
    [MethodImpl(P.N)]
    public void finishPlot(PlotState pState, Actor pActor)
    {
        if (_state == pState) return;
        _state = pState;
        foreach (Actor unit in units.ToArray())
            if (unit?.plot == this) unit.leavePlot();
        units.Clear();
        World.world.plots.removeObject(this);
    }

    public bool checkInitiatorAndTargets()
    {
        PlotAsset asset = _plot_asset;
        if (asset == null) return false;
        if (asset.check_target_alliance && target_alliance?.isAlive() != true) return false;
        if (asset.check_target_kingdom && (target_kingdom?.isAlive() != true || !target_kingdom.hasCities())) return false;
        if (asset.check_target_war && target_war?.isAlive() != true) return false;
        return true;
    }
}

public partial class PlotAsset
{
    public bool canBeDoneByRole(Actor pActor) =>
        can_be_done_by_king && pActor.isKing() || can_be_done_by_leader && pActor.isCityLeader() ||
        can_be_done_by_clan_member && pActor.hasClan();

    public bool isAllowedByWorldLaws() =>
        (!requires_diplomacy || WorldLawLibrary.world_law_diplomacy?.isEnabled() != false) &&
        (!requires_rebellion || WorldLawLibrary.world_law_rebellions?.isEnabled() != false);

    // 服务器上没有原版的谋划可挑；留着给模组的补丁挂(见 PlotAssetPatch)
    [MethodImpl(P.N)]
    public bool checkIsPossible(Actor pActor, bool pWorldLawsCheck = true) =>
        pActor != null && canBeDoneByRole(pActor) && (!pWorldLawsCheck || isAllowedByWorldLaws()) &&
        (check_is_possible == null || check_is_possible(pActor));
}

public partial class PlotManager
{
    // 立一块牌子(模组 DecisionPatch 在这之后把模组决议收进 DecisionSystem)
    [MethodImpl(P.N)]
    public Plot newPlot(Actor pAuthor, PlotAsset pAsset, bool pForced)
    {
        if (pAuthor == null || pAsset == null) return null;
        Plot plot = newObject();
        plot.newPlot(pAuthor, pAsset, pForced);
        return plot;
    }

    public bool isPlotTypeAlreadyRunning(Actor pActor, PlotAsset pPlotAsset) => list.Any(plot => plot.isActive() && plot.isSameType(pPlotAsset));
}
