$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rules = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/PeerageSuccessionRules.cs') -Raw
$harness = @'
namespace EmpireCraft.Tests
{
    public static class PeerageSuccessionTests
    {
        private class Person
        {
            public long Id;
            public bool Eligible = true;
            public bool InLine = true;
            public Person[] Children = new Person[0];
        }

        private static int count;
        private static void Check(bool condition, string scenario)
        {
            if (!condition) throw new System.Exception(scenario);
            count++;
        }

        private static Person Heir(Person previous)
        {
            return EmpireCraft.Scripts.Layer.PeerageSuccessionRules.FindDescendant(previous,
                p => p.Children, p => p.InLine, p => p.Eligible, p => p.Id);
        }

        public static int Run()
        {
            count = 0;
            var titles = new System.Collections.Generic.Dictionary<string, long>();
            Check(!EmpireCraft.Scripts.Layer.PeerageSuccessionRules.IsReserved<string>(null, "duke"), "Legacy null registry");
            Check(!EmpireCraft.Scripts.Layer.PeerageSuccessionRules.IsReserved(titles, "duke"), "First grant available");
            titles["duke"] = 10;
            Check(EmpireCraft.Scripts.Layer.PeerageSuccessionRules.IsReserved(titles, "duke"), "Occupied title blocks regrant");
            Check(!EmpireCraft.Scripts.Layer.PeerageSuccessionRules.IsReserved(titles, "marquis"), "Separate title remains available");
            titles["duke"] = -1;
            Check(EmpireCraft.Scripts.Layer.PeerageSuccessionRules.IsReserved(titles, "duke"), "Vacant lineage not freshly granted");
            titles["duke"] = 20;
            Check(EmpireCraft.Scripts.Layer.PeerageSuccessionRules.IsReserved(titles, "duke"), "Heir occupies same title");

            var previous = new Person { Id = 1, Eligible = false };
            var elder = new Person { Id = 2 };
            var younger = new Person { Id = 3 };
            var grandchild = new Person { Id = 4 };
            Check(Heir(null) == null, "Missing genealogy safely leaves vacancy");
            Check(Heir(previous) == null, "Extinct lineage has no heir");
            previous.Children = new[] { elder, younger };
            Check(Heir(previous) == elder, "Elder eligible child precedes younger");
            elder.Children = new[] { grandchild };
            Check(Heir(previous) == elder, "Living elder precedes own descendants");
            elder.Eligible = false;
            Check(Heir(previous) == grandchild, "Deceased elder represented by grandchild");
            elder.InLine = false;
            Check(Heir(previous) == younger, "Excluded branch cannot inherit through descendant");
            elder.InLine = true;
            grandchild.Eligible = false;
            Check(Heir(previous) == younger, "Empty elder branch falls through");
            younger.Eligible = false;
            Check(Heir(previous) == null, "No available descendants");
            grandchild.Children = new[] { previous, elder };
            Check(Heir(previous) == null, "Corrupt cyclic genealogy terminates");
            grandchild.Children = new[] { new Person { Id = 5 } };
            Check(Heir(previous).Id == 5, "Multiple deceased generations retain lineage");
            previous.Children = new Person[] { null, elder, elder, younger };
            Check(Heir(previous).Id == 5, "Null and duplicate relatives safely skipped");
            Check(Heir(previous).Id == 5, "Repeated search is stable");
            return count;
        }
    }
}
'@
Add-Type -TypeDefinition ($rules + [Environment]::NewLine + $harness)
$count = [EmpireCraft.Tests.PeerageSuccessionTests]::Run()
Write-Output "$count peerage succession rule assertions passed."

# Source-level wiring checks supplement the pure genealogy tests; these are not game integration tests.
$empire = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$actors = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/ActorExtension.cs') -Raw
$logs = Get-Content -LiteralPath (Join-Path $root 'Scripts/HelperFunc/TranslateHelper.cs') -Raw
if (!$actors.Contains('empire.IsHonoraryPeerageReserved(peerageKey)')) { throw 'Direct grants lack reservation guard' }
if (!$empire.Contains('if (IsHonoraryPeerageReserved(peerageKey)) continue;')) { throw 'Auto grants lack reservation guard' }
if ($empire.Contains('data.honorary_peerage_holders.Remove(peerageKey)')) { throw 'Vacancy erases hereditary reservation' }
if (!$empire.Contains('GrantLegalPeerage(successor, title, predecessor)')) { throw 'Legal succession discards predecessor' }
if (!$logs.Contains('}.RecordNationalHistoryIntoEmpire(empire, heir);')) { throw 'Inheritance lacks actor-bound imperial history' }
if (!$logs.Contains('}.RecordNationalHistoryIntoEmpire(empire, actor);')) { throw 'Investiture lacks actor-bound imperial history' }
Write-Output '6 source wiring checks passed.'
