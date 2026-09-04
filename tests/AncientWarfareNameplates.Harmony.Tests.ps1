# Verify real NML Harmony ordering and manager suppression, without a live game/save.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = & (Join-Path $PSScriptRoot 'AncientWarfareNameplates.Tests.ps1') -FixtureOnly
$stubs = $fixture.Stubs
foreach ($heading in 'namespace HarmonyLib', 'public static class NameplateTests') {
    $start = $stubs.IndexOf($heading)
    $end = $stubs.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($stubs[$end] -eq '{') { $depth++ }
        if ($stubs[$end] -eq '}') { $depth-- }
        $end++
    }
    $stubs = $stubs.Remove($start, $end - $start)
}
$stubs = $stubs.Replace('public class NameplateManager { public MetaType getCurrentMode() { return MetaType.City; } }', @'
public class NameplateManager {
    public MetaType drawnMode;
    public MetaType getCurrentMode() { return Zones.names ? MetaType.City : MetaType.None; }
    public void update() { drawnMode = getCurrentMode(); }
}
'@)
$stubs = [regex]::Replace($stubs, '(public (?:static )?(?:bool|MetaType|void) \w+\()',
    '[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] $1')
$main = @'
public static class PlateRuntimeSmoke {
    private static int passed;
    private static bool awSelected, canvasEnabled = true;
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); passed++; }
    private static bool AwBorderPrefix(ref MetaType __result) {
        __result = awSelected ? (MetaType)219 : MetaType.None;
        return false;
    }
    private static bool AwManagerPrefix() {
        canvasEnabled = !AncientWarfare3.core.policy.HierarchicalVassalMapModeService.IsActive();
        return canvasEnabled;
    }
    private static void InstallAw() {
        var aw = new Harmony("AW-map-fixture");
        aw.Patch(AccessTools.Method(typeof(Zones), "getCurrentMapBorderMode"), prefix:
            new HarmonyMethod(typeof(PlateRuntimeSmoke), "AwBorderPrefix") { priority = Priority.Last });
        aw.Patch(AccessTools.Method(typeof(NameplateManager), "update"), prefix:
            new HarmonyMethod(typeof(PlateRuntimeSmoke), "AwManagerPrefix"));
    }
    public static int Main(string[] args) {
        try {
            bool lateAw = args.Contains("late-aw");
            if (!lateAw) InstallAw();
            AncientWarfareCompatibility.Loaded = true;
            AncientWarfareNameplates.Install();
            if (lateAw) InstallAw();
            Check(LogService.warnings.Count == 0, string.Join("\n", LogService.warnings));
            var manager = new NameplateManager();
            foreach (var mode in new[] { MetaTypeExtension.Empire, MetaTypeExtension.KingdomTitle, MetaType.Kingdom }) {
                EmpireCraftMetaTypeLibrary.empire.option = mode == MetaTypeExtension.Empire;
                EmpireCraftMetaTypeLibrary.kingdomTitle.option = mode == MetaTypeExtension.KingdomTitle;
                World.world.displayed = new MetaTypeAsset { map_mode = mode };
                if (mode != MetaType.Kingdom)
                    Check(Zones.getCurrentMapBorderMode(false) == mode, "AW late fallback cannot erase EC mode");
                manager.update();
                Check(canvasEnabled, "Stale AW hierarchical toggle no longer hides EC canvas");
                Check(manager.drawnMode == mode, "Correct empire/legal/kingdom nameplate action selected");
            }
            World.world.displayed.map_mode = (MetaType)219;
            awSelected = true;
            Check(Zones.getCurrentMapBorderMode(false) == (MetaType)219, "Explicit AW view remains AW");
            manager.update();
            Check(!canvasEnabled, "AW hierarchical view still owns its nameplates");
            World.world.displayed.map_mode = MetaTypeExtension.Empire;
            awSelected = false;
            manager.update();
            Check(canvasEnabled && manager.drawnMode == MetaTypeExtension.Empire, "Return from AW view restores canvas and EC labels");
            Zones.names = false;
            manager.update();
            Check(manager.drawnMode == MetaType.None, "Hide-nameplates setting respected");
            Console.WriteLine(passed + " real-Harmony nameplate assertions passed (" + (lateAw ? "AW later" : "AW first") + ").");
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
'@
$code = $stubs + $fixture.Definitions + [regex]::Replace($fixture.Source, '(?m)^using [^;]+;\s*', '') + $main
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($code)
$library = (Resolve-Path (Join-Path $root '../../worldbox_Data/StreamingAssets/mods/NML/Assemblies')).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$references = [Microsoft.CodeAnalysis.MetadataReference[]]@(
    @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object {
        [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $framework $_))
    }
    [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $library '0Harmony.dll'))
)
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('NameplateHarmonySmoke', [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree), $references, $options)
$output = Join-Path ([IO.Path]::GetTempPath()) ('EmpireCraft-Nameplates-Smoke-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$exe = Join-Path $output 'NameplateHarmonySmoke.exe'
$stream = [IO.File]::Create($exe)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (-not $result.Success) { throw (($result.Diagnostics | Where-Object Severity -eq Error) -join "`n") }
foreach ($name in '0Harmony.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll', 'Mono.Cecil.dll') {
    Copy-Item -LiteralPath (Join-Path $library $name) -Destination (Join-Path $output $name)
}
& $exe
if ($LASTEXITCODE -ne 0) { throw 'AW-first nameplate smoke test failed' }
& $exe late-aw
if ($LASTEXITCODE -ne 0) { throw 'AW-later nameplate smoke test failed' }
