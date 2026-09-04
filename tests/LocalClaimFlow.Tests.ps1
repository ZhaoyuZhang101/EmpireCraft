#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$start = $source.IndexOf('public class TemporaryPushProgress')
$end = $source.IndexOf('public static class KingdomExtension', $start)
$production = $source.Substring($start, $end - $start)
# Execute the real progress class against a small fake world, without Unity/game DLLs.
$mocks = @'
public enum TemporaryFactionType { 转军府, 供养宗室 }
public enum MetaType { None, Kingdom }
public class ActorData { public int renown = 200; }
public class OfficeIdentity { public double TotalPerformance; }
public class Plot { public bool isActive() => true; }
public class Actor
{
    public long id; public ActorData data = new(); public Kingdom kingdom;
    public FixedFaction faction; public OfficeIdentity identity; public Plot plot; public bool dead;
    public int renown => data.renown;
    public bool isRekt() => dead;
    public bool isKing() => kingdom?.king == this;
    public OfficeIdentity GetIdentity() => identity;
    public FixedFaction GetFaction() => faction;
}
public class KingdomData { public TemporaryPushProgress PushProgress = new(); }
public class Kingdom
{
    public long id; public Actor king; public Empire empire; public Regime regime; public bool hostile;
    public KingdomData data = new();
    public KingdomData GetOrCreate() => data;
    public Empire GetEmpire() => empire;
    public Regime GetRegime() => regime;
    public bool isRekt() => false;
    public bool isInWarWith(Kingdom other) => hostile;
}
public class Regime
{
    public List<FixedFaction> factions = new();
    public List<FixedFaction> GetPlayerFactions() => factions;
}
public class Empire
{
    public long id = 1; public Kingdom CoreKingdom; public Actor Emperor = new();
    public TemporaryFaction RunningTemporaryFaction; public List<Kingdom> kingdoms_list = new();
    public bool isRekt() => false;
}
public class FixedFaction
{
    public string id = "local"; public bool Ban;
    public List<TemporaryFaction> TemporaryFactions = new();
    public string GetID() => id;
    public bool IsAnyTFactionRuns() => TemporaryFactions.Any(t => t.IsStarted());
}
public class TemporaryFaction
{
    public TemporaryFactionType type; public bool Active = true, canBePushByLocal, ShowAsPlot, started;
    public bool conditions = true, validTarget = true; public int CountDown, starts;
    public long EmpireID, KingdomID, TargetID = 42; public MetaType TargetType = MetaType.Kingdom, pusherType;
    public string factionID; public Empire empire;
    public TemporaryFaction Clone(FixedFaction faction)
    {
        var clone = (TemporaryFaction)MemberwiseClone(); clone.factionID = faction.id; return clone;
    }
    public void SetEmpire(Empire value) { empire = value; EmpireID = value.id; }
    public void SetKingdom(Kingdom value) => KingdomID = value.id;
    public bool CheckLocalCondition(Kingdom value) => conditions;
    public bool CheckLocalContinue(Kingdom value) => true;
    public bool CheckContinue() => true;
    public bool CheckTarget() => validTarget;
    public bool IsStarted() => started;
    public void Start() { started = true; starts++; empire.RunningTemporaryFaction = this; }
}
public class UnitManager : Dictionary<long, Actor>
{
    public Actor get(long id) => TryGetValue(id, out var actor) ? actor : null;
}
public class World
{
    public static World world = new(); public double time; public UnitManager units = new();
    public double getCurWorldTime() => time;
}
public static class Date
{
    public static int getMonthsSince(double date) => (int)(World.world.time - date);
    public static int getYearsSince(double date) => getMonthsSince(date) / 12;
}
public class Scenario
{
    public Empire empire = new(); public Actor actor = new() { id = 10 }; public FixedFaction faction = new();
    public Kingdom local; public TemporaryFaction template = new() { canBePushByLocal = true };
    public TemporaryPushProgress request => local.data.PushProgress;
    public Scenario()
    {
        World.world = new World();
        var regime = new Regime(); regime.factions.Add(faction);
        empire.CoreKingdom = new Kingdom { id = 1, regime = regime, empire = empire };
        local = new Kingdom { id = 2, empire = empire, king = actor };
        actor.kingdom = local; actor.faction = faction; World.world.units[actor.id] = actor;
        faction.TemporaryFactions.Add(template); empire.kingdoms_list.Add(local);
    }
    public void Start() => request.StartToPush(actor, template.type);
}
public static class LocalClaimTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool passed, string scenario) { if (!passed) throw new Exception(scenario); count++; }
        var s = new Scenario(); s.template.canBePushByLocal = false; s.Start();
        Check(!s.request.StartedToPushTf, "Per-claim switch blocks start");
        s.template.canBePushByLocal = true; s.template.conditions = false; s.Start();
        Check(!s.request.StartedToPushTf, "Unmet claim conditions block start");
        s.template.conditions = true; s.Start();
        Check(s.request.StartedToPushTf, "Enabled zero-valued enum claim starts");
        Check(!s.template.started && s.request.PendingClaim != s.template, "Selection does not mutate central template");
        s.request.PushOneMonths();
        Check(s.actor.renown == 200 && s.request.Progress == 0, "Timestamp zero does not charge immediately");
        World.world.time = 1; s.request.PushOneMonths();
        Check(s.actor.renown == 150 && s.request.Progress == 1, "Deduct exactly 50; rulers without office identity progress");
        s.request.PushOneMonths(); Check(s.actor.renown == 150, "No repeated charge in same month");
        s.actor.data.renown = 49; World.world.time = 2; s.request.PushOneMonths();
        Check(s.actor.renown == 49 && s.request.Progress == 1 && s.request.StartedToPushTf, "Insufficient funds pause without reset");
        s.request.Progress = 99; s.actor.data.renown = 50; World.world.time = 3;
        s.empire.RunningTemporaryFaction = new TemporaryFaction { started = true }; s.request.PushOneMonths();
        Check(s.request.Progress == 100 && s.request.StartedToPushTf && !s.template.started, "Busy court retains capped queue entry");
        s.actor.data.renown = 100; World.world.time = 4; s.request.PushOneMonths();
        Check(s.actor.renown == 100, "Waiting at 100 is free");
        s.empire.RunningTemporaryFaction = null; TemporaryPushProgress.TrySubmitReadyRequests(s.empire);
        Check(s.template.started && !s.request.StartedToPushTf && s.template.TargetID == 42, "Queued request submits with its original target");
        Check(s.template.pusherType == MetaType.Kingdom && s.template.KingdomID == 2, "Submission identifies local origin");
        s.request.Execute(); Check(s.template.starts == 1, "Submission is not duplicated");
        s = new Scenario(); s.Start(); s.template.canBePushByLocal = false; s.request.PushOneMonths();
        Check(!s.request.StartedToPushTf, "Disabling claim cancels pending request");
        s = new Scenario(); s.Start(); s.local.king = new Actor(); s.request.PushOneMonths();
        Check(!s.request.StartedToPushTf, "Dismissed pusher cannot continue spending");
        s = new Scenario(); s.Start(); s.actor.faction = new FixedFaction(); s.request.PushOneMonths();
        Check(!s.request.StartedToPushTf, "Faction change cancels request");
        s = new Scenario(); s.Start(); s.local.hostile = true; s.request.PushOneMonths();
        Check(!s.request.StartedToPushTf, "Rebellion against core invalidates submission");
        s = new Scenario(); s.Start(); s.request.PendingClaim.validTarget = false; s.request.PushOneMonths();
        Check(!s.request.StartedToPushTf, "Invalidated target stops request");
        s = new Scenario(); s.Start(); var supporter = new Actor { id = 11, kingdom = s.local, faction = s.faction };
        World.world.units[11] = supporter; s.request.AddSupporter(supporter, 1);
        World.world.time = 1; s.request.PushOneMonths();
        Check(s.request.Progress == 2 && supporter.renown == 150, "Supporter contributes after one month");
        World.world.time = 12; s.request.PushOneMonths();
        Check(s.request.Supporters.Count == 0 && supporter.renown == 150, "One-year support expires without extra charge");
        return count;
    }
}
'@
Add-Type -TypeDefinition ("using System; using System.Collections.Generic; using System.Linq; namespace LocalClaimHarness {`n" + $production + $mocks + "`n}")
Write-Output "$([LocalClaimHarness.LocalClaimTests]::Run()) real progress-class flow assertions passed."
$converter = Get-Content -LiteralPath (Join-Path $root 'Scripts/HelperFunc/TemporaryFactionConverter.cs') -Raw
foreach ($field in 'canBePushByLocal','pusherType','KingdomID','progressMax') {
    if (!$converter.Contains('["' + $field + '"]')) { throw "Missing saved claim field: $field" }
}
$scheduler = Get-Content -LiteralPath (Join-Path $root 'Scripts/ModClass.cs') -Raw
if (!$scheduler.Contains('.SelectMany(f => f.TemporaryFactions)')) { throw 'Scheduler excludes non-dominant local claims' }
$centralAI = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckTemporaryFaction.cs') -Raw
if (!$centralAI.Contains('tf.IsLocallyPushed && tf.Active && tf.CheckLocalContinue(tf.GetKingdom())')) {
    throw 'Central AI cancels local submissions'
}
if (!$centralAI.Contains('TemporaryPushProgress.TrySubmitReadyRequests(empire);')) { throw 'Missing ready queue submission' }
Write-Output '7 source wiring and save-field checks passed.'
