# Real installed Harmony + .NET Framework smoke test, without loading a WorldBox save.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Get-Content (Join-Path $PSScriptRoot 'AncientWarfareNaming.Tests.ps1') -Raw
$stubs = [regex]::Match($fixture, "(?s)\`$stubs = @'\r?\n(.*?)\r?\n'@").Groups[1].Value
if (-not $stubs) { throw 'Naming fixture not found' }
function Remove-Block([string]$source, [string]$heading) {
    $start = $source.IndexOf($heading)
    if ($start -lt 0) { throw "Missing fixture block: $heading" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Remove($start, $end - $start)
}
$stubs = Remove-Block $stubs 'namespace HarmonyLib'
$stubs = Remove-Block $stubs 'public static class NamingTests'
$stubs = $stubs.Replace('public bool xia; }', 'public bool xia; public int politicalWork; }')
$stubs = $stubs.Replace('public static void ApplyIdentity(Kingdom k) { }',
    'public static void ApplyIdentity(Kingdom k) { k.setName("AW-restored", false); k.politicalWork++; }')
$stubs = $stubs.Replace('return default;', 'return new StateNameCommitResult { Success = true };')
$stubs = [regex]::Replace($stubs, '(?m)(public static (?:void|bool|string|int|StateNameCommitResult|Kingdom\[\]) \w+\()',
    '[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] $1')
$main = @'
public static class RuntimeSmoke {
    private static int count;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; }
    private static bool AwGenerator(Actor pActor, MetaType pType, ref string __result) { __result = "AW-generator"; return false; }
    public static int Main(string[] args) {
        try {
            bool lateAw = args.Contains("late-aw");
            var aw = new Harmony("AW-fixture");
            var generator = AccessTools.Method(typeof(NameGenerator), "generateName");
            var awPrefix = new HarmonyMethod(typeof(RuntimeSmoke), "AwGenerator") { priority = Priority.Last };
            if (!lateAw) aw.Patch(generator, prefix: awPrefix);
            var namingPrefix = new HarmonyMethod(typeof(AncientWarfare3.patch.AW_CivMonkeyNamingPatch), "Prefix");
            var namingPostfix = new HarmonyMethod(typeof(AncientWarfare3.patch.naming.AW_ActorLocalizedNamePatch), "Postfix");
            var mixedPostfix = new HarmonyMethod(typeof(AncientWarfare3.patch.AW_BabyNamePatch), "Postfix");
            if (!lateAw) {
                aw.Patch(generator, prefix: namingPrefix, postfix: namingPostfix);
                aw.Patch(generator, postfix: mixedPostfix);
            }
            AncientWarfareCompatibility.Loaded = true;
            AncientWarfareNaming.Install();
            if (lateAw) {
                aw.Patch(generator, prefix: awPrefix);
                aw.Patch(generator, prefix: namingPrefix, postfix: namingPostfix);
                aw.Patch(generator, postfix: mixedPostfix);
                // Exercise the same sweep used by the delayed registration check.
                typeof(AncientWarfareNaming).GetField("_nextSweep", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, DateTime.MinValue);
                AncientWarfareNaming.Install();
            }
            Check(LogService.warnings.Count == 0, string.Join("\n", LogService.warnings));
            var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains("EmpireCraft.AncientWarfareNaming")).ToArray();
            Check(patched.Length == 41, "All 41 real patches installed");
            var callbacks = Harmony.GetPatchInfo(generator);
            Check(callbacks.Prefixes.Concat(callbacks.Postfixes).All(p => !AncientWarfareNaming.IsNamingCallback(p.PatchMethod)), "Pure AW naming callbacks removed from original game method");
            Check(callbacks.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(AncientWarfare3.patch.AW_BabyNamePatch)), "Mixed birth callback remains installed");
            var normal = new Kingdom { data = new KingdomData { id = 1, name = "EC Empire", original_actor_asset = "human" } };
            var xia = new Kingdom { data = new KingdomData { id = 2, name = "Xia", original_actor_asset = "Xia" } };
            World.world.kingdoms.items[1] = normal;
            World.world.kingdoms.items[2] = xia;
            Check(AncientWarfare3.core.naming.AWLocalizedKingdomNameService.ProjectStored(normal) == "EC Empire", "Real string prefix retains EC name");
            Check(AncientWarfare3.core.naming.AWLocalizedKingdomNameService.ProjectStored(xia) == "Xia", "Xia uses its actual name without AW projection");
            Check(!AncientWarfare3.core.naming.AWLocalizedNameService.CommitChineseName(normal.data), "Real bool prefix rejects AW overwrite");
            Check(!AncientWarfare3.core.naming.AWLocalizedNameService.CommitChineseName(xia.data), "Xia AW writes blocked");
            Check(!AncientWarfare3.core.lineage.StateNameService.EnsureBoundStateName(normal).Success, "Skipped foreign struct result is failure");
            Check(!AncientWarfare3.core.lineage.StateNameService.EnsureBoundStateName(xia).Success, "Xia AW name commit blocked");
            Check(AncientWarfare3.core.lineage.RulerAppellationService.ResolveProjectedStateName(normal, false) == "", "Do not override EC nameplate");
            Check(AncientWarfare3.core.lineage.RulerAppellationService.ResolveProjectedStateName(xia, false) == "", "No AW nameplate for Xia either");
            var actor = new Actor { kingdom = normal, culture = new Culture(), data = new BaseSystemData { name = "EC-actor" } };
            normal.king = actor;
            string generated = NameGenerator.generateName(actor, MetaType.Kingdom, 0);
            Check(generated == "EC-name", "EC generator wins before AW prefix, actual: " + generated);
            Check(NameGenerator.generateName(new Actor { kingdom = xia, culture = new Culture() }, MetaType.Kingdom, 0) == "EC-name", "Xia generator follows EC");
            Check(AncientWarfare3.patch.AW_BabyNamePatch.births == 2, "Mixed hook still executes its non-naming work");
            Check(actor.data.name == "EC-actor", "Periodic lineage naming is suppressed within mixed hook");
            Check(AncientWarfare3.core.naming.AWLocalizedNameService.ProjectActor(actor) == "EC-actor", "Actor projection cannot overwrite EC name");
            Check(AncientWarfare3.core.lineage.RulerAppellationService.GetFullLivingAppellation(normal) == "EC-actor", "Ruler tooltip uses EC personal name");
            Check(!AncientWarfare3.content.XiaNamingRepair.TryRenameCulture(new Culture(), actor, true), "Forced culture repair blocked");
            Check(!AncientWarfare3.content.XiaNamingRepair.TryApplyFullyXiaizedKingdomName(xia), "Conversion repair blocked");
            Check(AncientWarfare3.core.lineage.WesternLineageAdmissionService.SynchronizeOriginalClan(actor), "Western clan operation completes");
            Check(actor.data.name == "EC-actor" && AncientWarfare3.core.lineage.WesternLineageAdmissionService.royalBindings == 1, "Western clan naming suppressed without cancelling royal binding");
            AncientWarfare3.core.naming.ActorManualRenameService.ApplyInheritedFamily(actor, "AW-family");
            Check(actor.data.name == "EC-actor", "Inherited family display cannot reintroduce AW name");
            AncientWarfare3.core.lineage.KingdomIdentityContinuityService.ApplyIdentity(normal);
            Check(normal.writes == 0 && normal.politicalWork == 1, "Real transpiler filters only name write");
            AncientWarfare3.core.lineage.KingdomIdentityContinuityService.ApplyIdentity(xia);
            Check(xia.writes == 0 && xia.politicalWork == 1, "Real transpiler retains Xia political work but not name write");
            normal.data.level = 2;
            Check(AncientWarfare3.core.naming.AWLocalizedKingdomNameService.ProjectStored(normal) == "EC Empire", "Runtime Xiaization never restores AW naming");
            Console.WriteLine(count + " real-Harmony assertions passed (" + (lateAw ? "AW later" : "AW first") + ").");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
'@
$sources = foreach ($name in 'AncientWarfareNaming', 'AncientWarfareRules') {
    [regex]::Replace((Get-Content (Join-Path $root "Scripts/Compatibility/$name.cs") -Raw), '(?m)^using [^;]+;\s*', '')
}
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($stubs + "`n" + ($sources -join "`n") + "`n" + $main)
$harmonyPath = (Resolve-Path (Join-Path $root '../../worldbox_Data/StreamingAssets/mods/NML/Assemblies/0Harmony.dll')).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$paths = @((Join-Path $framework 'mscorlib.dll'), (Join-Path $framework 'System.dll'), (Join-Path $framework 'System.Core.dll'), $harmonyPath)
$references = [Microsoft.CodeAnalysis.MetadataReference[]]@($paths | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_) })
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('NamingHarmonySmoke', [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree), $references, $options)
$output = Join-Path ([IO.Path]::GetTempPath()) ('EmpireCraft-Naming-Smoke-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$exe = Join-Path $output 'NamingHarmonySmoke.exe'
$stream = [IO.File]::Create($exe)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (-not $result.Success) { throw (($result.Diagnostics | Where-Object Severity -eq Error) -join "`n") }
Copy-Item -LiteralPath $harmonyPath -Destination (Join-Path $output '0Harmony.dll')
foreach ($dependency in 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll', 'Mono.Cecil.dll') {
    Copy-Item -LiteralPath (Join-Path (Split-Path $harmonyPath -Parent) $dependency) -Destination (Join-Path $output $dependency)
}
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Real-Harmony AW-first test failed' }
& $exe late-aw
if ($LASTEXITCODE -ne 0) { throw 'Real-Harmony AW-later test failed' }
