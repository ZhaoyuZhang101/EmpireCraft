#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
function Read-Method([string]$Signature) {
    $start = $source.IndexOf($Signature)
    if ($start -lt 0) { throw "Missing production method: $Signature" }
    $brace = $source.IndexOf('{', $start)
    $depth = 1
    $end = $brace + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$methods = @(
    'private Actor GetLivingLegalPeerageHolder(',
    'private KingdomTitle FindMinisterDukedomTarget(',
    'public bool CanPowerfulMinisterSeekDukedom(',
    'public bool TryGrantPowerfulMinisterDukedom(',
    'private bool AssignLegalPeerage(',
    'public int GetMinisterOppositionPenalty(',
    'private City GetMinisterOpponentPowerBase(',
    'private void ResolveMinisterOpposition('
) | ForEach-Object { Read-Method $_ }
$mocks = @'
public enum PeeragesLevel { peerages_0, peerages_2, peerages_3, peerages_6 }
public enum EmpireWarType { 地方叛乱 }
public class SpecificClan { }
public class NanoObject { }
public class Identity { public long id; }
public class ActorExtra {
    public bool virtual_enfeoff;
    public long virtual_enfeoff_empire_id = -1, virtual_enfeoff_title_id = -1;
    public string virtual_enfeoff_peerage_key = "";
}
public class Actor {
    public long id; public int renown; public bool dead, adult = true, throwOnClanCheck;
    public Kingdom kingdom; public SpecificClan clan; public OfficeObject office;
    public ActorExtra extra = new(); public PeeragesLevel level = PeeragesLevel.peerages_6;
    public List<long> titles = new(); public List<string> history = new(); public int moves;
    public bool isRekt() => dead;
    public bool isAdult() => adult;
    public bool isKing() => kingdom?.king == this;
    public ActorExtra GetOrCreate() => extra;
    public bool HasVirtualEnfeoff(Empire e) => extra.virtual_enfeoff && extra.virtual_enfeoff_empire_id == e.data.id;
    public SpecificClan GetSpecificClan() => clan;
    public void CheckSpecificClan(bool unused) { if (throwOnClanCheck) throw new InvalidOperationException("test failure"); }
    public Identity GetPersonalIdentity() => new Identity { id = id * 10 };
    public string GetPeerageDisplayName() => extra.virtual_enfeoff_peerage_key;
    public PeeragesLevel GetPeeragesLevel() => level;
    public void SetPeeragesLevel(PeeragesLevel p) => level = p;
    public void joinCity(City c) { moves++; kingdom = c.kingdom; }
    public void goTo(object tile) { }
    public string getName() => "Actor" + id;
    public void RecordPersonalHistory(string s, long relatedActorId = -1) => history.Add(s);
    public OfficeObject GetOffice() => office;
    public List<long> GetOwnedTitle() => titles;
    public object GetFaction() => null;
}
public class OfficeObject { public bool is_local; public long actor_id; public NanoObject meta_object; }
public class Regime { public void SetAllowArmy(bool b) { } public void SetAllowSupportCenterArmy(bool b) { } }
public class Kingdom : NanoObject {
    public string name = "Test"; public Empire empire; public Actor king; public City capital;
    public bool rebel, hostile; public Regime regime = new();
    public Empire GetEmpire() => empire;
    public Regime GetRegime() => regime;
    public bool IsLocalRebelling() => rebel;
    public bool IsFactionRebelling() => false;
    public bool isInWarWith(Kingdom k) => hostile;
    public void StartLocalRebelling(EmpireWarType t) => rebel = true;
}
public class City : NanoObject {
    public long id; public bool dead; public Kingdom kingdom; public object _city_tile = new();
    public bool isRekt() => dead;
    public Kingdom makeOwnKingdom(Actor a, bool pRebellion) {
        var k = new Kingdom { king = a, capital = this }; kingdom = k; a.kingdom = k; return k;
    }
}
public class KingdomTitle {
    public long id; public City title_capital; public Actor owner; public List<City> cities = new();
    public bool isRekt() => false;
    public List<City> getCities() => cities;
}
public class TitleManager { public Dictionary<long, KingdomTitle> all = new(); public KingdomTitle get(long id) => all.GetValueOrDefault(id); }
public static class ModClass { public static TitleManager KINGDOM_TITLE_MANAGER = new(); }
public class UnitManager { public Dictionary<long, Actor> all = new(); public Actor get(long id) => all.GetValueOrDefault(id); }
public class War { public void SetEmpireWarType(EmpireWarType t, string pre, NanoObject nanoObject, object belongingFaction) { } }
public static class WarTypeLibrary { public static object rebellion = new(); }
public class Diplomacy {
    public int wars;
    public War startWar(Kingdom a, Kingdom b, object type) { wars++; a.hostile = true; return new War(); }
}
public class World {
    public static World world = new(); public UnitManager units = new(); public Diplomacy diplomacy = new();
    public double getCurWorldTime() => 100;
}
public static class LM { public static string Get(string key) => key +
    (key == "personal_history_powerful_minister_duke" ? " {0}" : " {0} {1}"); }
public static class TranslateHelper {
    public static void LogPowerfulMinisterAcquireTitle(Actor actor, Empire empire, string name) { }
}
public class EmpireData {
    public long id = 1, powerful_minister_title_id = -1, minister_usurper_id = -1;
    public int powerful_minister_stage = 1;
    public double powerful_minister_stage_timestamp;
    public bool is_been_controlled;
    public Dictionary<long, long> legal_peerage_holders = new(), legal_peerage_holder_identities = new();
    public Dictionary<long, string> legal_peerage_types = new();
}
public partial class Empire : NanoObject {
    public const int PowerfulMinisterStageDominant = 1, PowerfulMinisterStageDuke = 2, PowerfulMinisterStageKing = 3;
    public EmpireData data = new(); public Kingdom CoreKingdom; public SpecificClan EmpireSpecificClan = new();
    public Actor Emperor => CoreKingdom.king;
    public Actor minister; public List<Actor> units = new(); public List<KingdomTitle> titles = new();
    public List<string> history = new(); public int mandate = 50;
    public List<Actor> getUnits() => units.Where(a => a.kingdom?.GetEmpire() == this).ToList();
    public Actor GetPowerfulMinister() => minister;
    private List<KingdomTitle> GetLegalPeerageTitles() => titles;
    private bool CanAdvanceMinisterPlot(Actor actor) => actor == minister;
    public void AddMandate(int n) => mandate += n;
    public void RecordHistory(string directContent, long actorId) => history.Add(directContent);
    public void leave(Kingdom k, bool recalc, bool detach) => k.empire = null;
    public City TestPowerBase(Actor a) => GetMinisterOpponentPowerBase(a);
    public void TestOpposition(int influence) => ResolveMinisterOpposition(minister, influence: influence);
}
'@
$cases = @'
public static class Cases {
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    static Empire Setup() {
        World.world = new(); ModClass.KINGDOM_TITLE_MANAGER = new();
        var e = new Empire(); e.CoreKingdom = new Kingdom { empire = e };
        e.CoreKingdom.capital = new City { id = 1, kingdom = e.CoreKingdom };
        e.CoreKingdom.king = AddActor(e, 1, 200, true);
        e.minister = AddActor(e, 2, 800, false); return e;
    }
    static Actor AddActor(Empire e, long id, int influence, bool royal) {
        var a = new Actor { id = id, renown = influence, clan = royal ? e.EmpireSpecificClan : new(), kingdom = e.CoreKingdom };
        e.units.Add(a); World.world.units.all[id] = a; return a;
    }
    static KingdomTitle Title(Empire e, long id, Actor holder) {
        var t = new KingdomTitle { id = id, title_capital = new City { id = 100 + id, kingdom = new Kingdom { empire = e } } };
        e.titles.Add(t); ModClass.KINGDOM_TITLE_MANAGER.all[id] = t;
        if (holder != null) {
            holder.extra.virtual_enfeoff = true; holder.extra.virtual_enfeoff_empire_id = e.data.id;
            holder.extra.virtual_enfeoff_title_id = id; holder.extra.virtual_enfeoff_peerage_key = "default_peerages_2";
            holder.level = PeeragesLevel.peerages_2;
            e.data.legal_peerage_holders[id] = holder.id; e.data.legal_peerage_holder_identities[id] = holder.id * 10;
        }
        return t;
    }
    public static int Run() {
        var e = Setup(); var low = AddActor(e, 3, 10, true); var high = AddActor(e, 4, 100, true);
        var outsider = AddActor(e, 5, 0, false);
        Title(e, 1, null); Title(e, 2, high); Title(e, 3, low); Title(e, 4, outsider);
        Check(e.TryGrantPowerfulMinisterDukedom(e.minister), "occupied royal title must be grantable");
        Check(e.data.powerful_minister_title_id == 3, "lowest royal influence before vacant title");
        Check(!low.HasVirtualEnfeoff(e) && low.extra.virtual_enfeoff_title_id == -1, "revocation clears actor identity");
        Check(low.level == PeeragesLevel.peerages_6, "revocation is not an emperor promotion");
        Check(e.data.legal_peerage_holders[3] == 2 && e.data.legal_peerage_holder_identities[3] == 20, "registry and hereditary identity transferred");
        Check(e.data.legal_peerage_types[3] == "tang_peerage_guogong", "new holder is a duke");
        Check(high.HasVirtualEnfeoff(e) && outsider.HasVirtualEnfeoff(e), "other peerages preserved");
        Check(e.minister.kingdom == e.CoreKingdom && e.minister.moves == 0, "minister stays at court");
        Check(low.history.Count == 1 && e.history.Count == 2, "revocation and grant history recorded once");
        Check(!e.TryGrantPowerfulMinisterDukedom(e.minister), "cannot repeat grant");
        e = Setup(); Title(e, 8, null);
        Check(e.TryGrantPowerfulMinisterDukedom(e.minister) && e.data.powerful_minister_title_id == 8, "vacant fallback");
        e = Setup(); Title(e, 8, AddActor(e, 4, 0, false));
        Check(!e.CanPowerfulMinisterSeekDukedom(e.minister), "non-royal occupied titles cannot be stolen");
        e = Setup(); low = AddActor(e, 3, 10, true); Title(e, 1, low); e.minister.throwOnClanCheck = true;
        try { e.TryGrantPowerfulMinisterDukedom(e.minister); } catch (InvalidOperationException) { }
        Check(low.HasVirtualEnfeoff(e) && e.data.legal_peerage_holders[1] == low.id, "failed grant restores prior holder");
        e = Setup(); low = AddActor(e, 3, 501, true); var title = Title(e, 1, low);
        Check(e.TestPowerBase(low) == null, "virtual peerage supplies no actual land");
        low.titles.Add(title.id); title.owner = low; title.cities.Add(title.title_capital);
        Check(e.TestPowerBase(low) == title.title_capital, "royal landed title supplies a power base");
        low.clan = new SpecificClan();
        Check(e.TestPowerBase(low) == title.title_capital, "non-royal landed lord also commands local power");
        var governor = AddActor(e, 4, 501, false); var city = new City { id = 8, kingdom = e.CoreKingdom };
        governor.office = new OfficeObject { is_local = true, actor_id = governor.id, meta_object = city };
        Check(e.TestPowerBase(governor) == city, "actual local governor has a power base");
        governor.office.actor_id = 999;
        Check(e.TestPowerBase(governor) == null, "stale office must not command land");
        governor.office.actor_id = governor.id; governor.office.meta_object = e.CoreKingdom.capital;
        Check(e.TestPowerBase(governor) == null, "imperial capital cannot be split off");
        governor.office.meta_object = city;
        e.TestOpposition(1000);
        Check(World.world.diplomacy.wars == 0 && city.kingdom == e.CoreKingdom, "high influence deters all special opposition");
        e.TestOpposition(999);
        Check(World.world.diplomacy.wars == 2, "landed royal and local governor both rise immediately");
        Check(city.kingdom.rebel && city.kingdom.king == governor && city.kingdom.empire == null, "governor actually forms a rebel kingdom");
        e.TestOpposition(0);
        Check(World.world.diplomacy.wars == 2, "detached rebels do not rise twice");
        e = Setup(); e.data.is_been_controlled = true; e.data.powerful_minister_stage = 3;
        Check(e.GetMinisterOppositionPenalty() == -40, "nine bestowments activates opinion penalty");
        e.minister.renown = 1000; Check(e.GetMinisterOppositionPenalty() == 0, "recovered influence removes resentment");
        e.CoreKingdom.king = e.minister; e.data.minister_usurper_id = e.minister.id; e.data.powerful_minister_stage = 0;
        e.minister.renown = 500; Check(e.GetMinisterOppositionPenalty() == -100, "opposition persists after usurpation");
        e.CoreKingdom.king = AddActor(e, 9, 0, true);
        Check(e.GetMinisterOppositionPenalty() == 0, "next emperor does not inherit minister penalty");
        return passed;
    }
}
'@
$rules = Get-Content (Join-Path $root 'Scripts/Layer/PowerfulMinisterRules.cs') -Raw
$code = "using System; using System.Linq; using System.Collections.Generic; using EmpireCraft.Scripts.Layer;`n" +
    "namespace MinisterTransitionTests {`n$mocks`npublic partial class Empire {`n" +
    ($methods -join "`n") + "`n}`n$cases`n}"
# Compile and execute production methods against a fake world, rather than copying their logic into tests.
Add-Type -TypeDefinition ($code + "`n" + ($rules -replace '^using System;', ''))
Write-Output "$([MinisterTransitionTests.Cases]::Run()) production-transition assertions passed."
