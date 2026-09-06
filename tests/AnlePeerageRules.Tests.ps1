$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rules = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/DefeatedEmpireHouse.cs') -Raw
$harness = @'
namespace EmpireCraft.Tests
{
    public static class AnleTests
    {
        public static int Run()
        {
            int count = 0;
            System.Action<bool, string> check = (condition, scenario) =>
            {
                if (!condition) throw new System.Exception(scenario);
                count++;
            };
            var houses = new[] { new EmpireCraft.Scripts.Layer.DefeatedEmpireHouse
            {
                empire_id = 2, emperor_id = 20, royal_clan_id = 200
            } };
            System.Func<long, long, bool, bool, int> priority = (actor, clan, resident, ruler) =>
                EmpireCraft.Scripts.Layer.AnlePeerageRules.CandidatePriority(houses, 1, actor, clan, resident, ruler);
            check(priority(30, 300, true, false) == 0, "Ordinary meritorious minister is not eligible");
            check(priority(20, 200, true, false) == 2, "Defeated former emperor has first priority");
            check(priority(21, 200, true, false) == 1, "Defeated royal house is fallback");
            check(priority(20, 200, false, false) == 0, "Do not seize a foreign resident");
            check(priority(20, 200, true, true) == 0, "Do not invest a reigning ruler");
            check(priority(21, 200, false, false) == 0, "Royal fallback must also be resident");
            check(priority(21, 200, true, true) == 0, "Royal fallback cannot be a ruler");
            check(priority(20, -1, true, false) == 2, "Recorded former emperor survives clan change");
            check(priority(21, -1, true, false) == 0, "Missing clan is not royal evidence");
            check(priority(-1, 200, true, false) == 0, "Missing actor is ineligible");
            check(EmpireCraft.Scripts.Layer.AnlePeerageRules.CandidatePriority(houses, 2, 20, 200, true, false) == 0,
                "Own empire is not a defeated foreign empire");
            check(EmpireCraft.Scripts.Layer.AnlePeerageRules.CandidatePriority(null, 1, 20, 200, true, false) == 0,
                "Legacy save without evidence leaves title vacant");
            check(EmpireCraft.Scripts.Layer.AnlePeerageRules.CandidatePriority(
                new[] { new EmpireCraft.Scripts.Layer.DefeatedEmpireHouse() }, 1, 20, 200, true, false) == 0,
                "Empty snapshot cannot establish eligibility");
            return count;
        }
    }
}
'@
Add-Type -TypeDefinition ($rules + [Environment]::NewLine + $harness)
Write-Output "$([EmpireCraft.Tests.AnleTests]::Run()) Anle eligibility assertions passed."

$actors = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/ActorExtension.cs') -Raw
$war = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/WarPatch.cs') -Raw
if (!$actors.Contains('peerageKey == "tang_honorary_anle_gong" && empire.GetAnlePeeragePriority(a) <= 0')) {
    throw 'Direct grants bypass Anle eligibility'
}
if (!$war.Contains('if (winner != WarWinner.Attackers && winner != WarWinner.Defenders) return;')) {
    throw 'Draws incorrectly qualify as victories'
}
if ($war.IndexOf('RememberDefeatedWarHouses(pWar, pWinner);') -gt $war.IndexOf('pWar.endForSides(pWinner);')) {
    throw 'Defeat evidence recorded after war cleanup'
}
Write-Output '3 source wiring checks passed.'
