#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read-Method([string]$Path, [string]$Signature) {
    $source = Get-Content (Join-Path $root $Path) -Raw
    $start = $source.IndexOf($Signature)
    if ($start -lt 0) { throw "Missing production method: $Signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$layerMethod = Read-Method 'Scripts/GameLibrary/EmpireCraftMetaTypeLibrary.cs' 'private static IMetaObject getKingdomTitleLayerMeta0('
$colorMethod = Read-Method 'Scripts/Layer/CoreEmpireTitleManager.cs' 'public static KingdomTitle GetColorTitle('
$mocks = @'
public interface IMetaObject { }
public class Empire : IMetaObject {
    public bool dead, archived;
    public bool isRekt() => dead;
    public bool IsArchived() => archived;
}
public class KingdomTitle : IMetaObject {
    public bool dead; public string color;
    public bool isRekt() => dead;
}
public class EmpireCore {
    public long empire_id = -1;
    public City capital;
    public List<KingdomTitle> titles = new();
}
public class City {
    public KingdomTitle title; public EmpireCore core;
    public KingdomTitle GetTitle() => title;
    public EmpireCore GetEmpireCore() => core;
}
public class TileZone { public City city; }
public class EmpireManager {
    public Dictionary<long, Empire> items = new();
    public Empire get(long id) => items.GetValueOrDefault(id);
}
public static class ModClass { public static EmpireManager EMPIRE_MANAGER = new(); }
public static partial class EmpireCoreManager {
    public static City GetRepresentativeCity(EmpireCore core) => core.capital;
    public static bool ContainsTitle(EmpireCore core, KingdomTitle title) => core.titles.Contains(title);
    public static List<KingdomTitle> GetTitles(EmpireCore core) => core.titles;
}
public static partial class Layer {
    public static IMetaObject Resolve(TileZone zone) => getKingdomTitleLayerMeta0(zone);
}
public static class Cases {
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    public static int Run() {
        var a = new KingdomTitle { color = "red" };
        var b = new KingdomTitle { color = "blue" };
        var c = new KingdomTitle { color = "green" };
        var core = new EmpireCore { titles = new() { a, b } };
        var za = new TileZone { city = new City { title = a, core = core } };
        var zb = new TileZone { city = new City { title = b, core = core } };
        core.capital = za.city;
        Check(Layer.Resolve(za) == a && Layer.Resolve(zb) == a, "Unclaimed core uses one shared color/meta identity");
        Check(a.color == "red" && b.color == "blue", "Rendering does not overwrite kingdom title colors");
        Check(zb.city.GetTitle() == b, "Kingdom-level mode retains its own title");
        core.empire_id = 99;
        Check(Layer.Resolve(zb) == a, "Missing historical empire uses representative title");
        var empire = new Empire(); ModClass.EMPIRE_MANAGER.items[99] = empire;
        Check(Layer.Resolve(za) == empire && Layer.Resolve(zb) == empire, "Live empire color behavior unchanged");
        empire.archived = true;
        Check(Layer.Resolve(zb) == a, "Archived empire no longer fragments de jure colors");
        empire.archived = false; empire.dead = true;
        Check(Layer.Resolve(zb) == a, "Destroyed empire no longer fragments de jure colors");
        a.dead = true;
        Check(Layer.Resolve(za) == b && Layer.Resolve(zb) == b, "Invalid representative falls back to a living title");
        a.dead = false; core.capital = new City { title = c };
        Check(Layer.Resolve(zb) == a, "Capital outside the core cannot supply its color");
        core.capital = null;
        Check(Layer.Resolve(za) == Layer.Resolve(zb), "Missing capital still shares a representative");
        var other = new EmpireCore { titles = new() { c }, capital = new City { title = c } };
        Check(Layer.Resolve(new TileZone { city = new City { title = c, core = other } }) == c,
            "Different de jure empires keep separate colors");
        Check(Layer.Resolve(new TileZone { city = new City { title = c, core = core } }) == c,
            "Stale core assignment cannot recolor unrelated title");
        Check(Layer.Resolve(new TileZone { city = new City { title = b } }) == b, "Unassigned title unchanged");
        Check(Layer.Resolve(null) == null && Layer.Resolve(new TileZone()) == null, "Empty zones safe");
        Check(Layer.Resolve(new TileZone { city = new City { core = core } }) == null, "Untitled city unchanged");
        Check(EmpireCoreManager.GetColorTitle(null) == null, "Missing core safe");
        Check(EmpireCoreManager.GetColorTitle(new EmpireCore()) == null, "Empty core safe");
        return passed;
    }
}
'@
# Execute the actual layer resolver and actual color-selection method with minimal world stubs.
$code = "using System; using System.Collections.Generic; using System.Linq;`nnamespace EmpireCoreColorTests {`n" +
    $mocks + "`npublic static partial class Layer {`n$layerMethod`n}`n" +
    "public static partial class EmpireCoreManager {`n$colorMethod`n}`n}"
Add-Type -TypeDefinition $code
Write-Output "$([EmpireCoreColorTests.Cases]::Run()) empire-core layer color assertions passed."
