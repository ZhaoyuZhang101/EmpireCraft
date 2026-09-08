$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$enums = Get-Content -LiteralPath (Join-Path $root 'Scripts/Enums/PeeragesLevel.cs') -Raw
$enums = $enums -replace 'namespace EmpireCraft\.Scripts\.Enums;\s*', ''
$regimeSource = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/RegimeSystem.cs') -Raw
$rules = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/WesternPeerageRules.cs') -Raw
$rules = $rules -replace 'using EmpireCraft\.Scripts\.Enums;\s*', ''
$rules = $rules -replace 'namespace EmpireCraft\.Scripts\.Regimes;\s*', ''
$kingdomTypeStart = $regimeSource.IndexOf('public enum KingdomType')
$kingdomTypeEnd = $regimeSource.IndexOf('public enum ArmyOfficialType', $kingdomTypeStart)
if ($kingdomTypeStart -lt 0 -or $kingdomTypeEnd -lt 0) { throw 'KingdomType enum not found' }
$kingdomType = $regimeSource.Substring($kingdomTypeStart, $kingdomTypeEnd - $kingdomTypeStart)

$harness = @'
namespace EmpireCraft.Tests
{
    public static class WesternPeerageCases
    {
        static int count;
        static void Check(EmpireCraft.Scripts.Regimes.KingdomType type,
            EmpireCraft.Scripts.Enums.PeeragesLevel expected)
        {
            if (!EmpireCraft.Scripts.Regimes.WesternPeerageRules.TryGetRulerLevel(type, out var actual) || actual != expected)
                throw new System.Exception(type + " mapped to " + actual + ", expected " + expected);
            count++;
        }

        public static int Run()
        {
            Check(EmpireCraft.Scripts.Regimes.KingdomType.Feudalism_empire, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_0);
            Check(EmpireCraft.Scripts.Regimes.KingdomType.Feudalism_kingdom, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_2);
            Check(EmpireCraft.Scripts.Regimes.KingdomType.Feudalism_grand_duchy, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_3);
            Check(EmpireCraft.Scripts.Regimes.KingdomType.Feudalism_duchy, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_3);
            Check(EmpireCraft.Scripts.Regimes.KingdomType.Feudalism_march, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_4);
            Check(EmpireCraft.Scripts.Regimes.KingdomType.Feudalism_county, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_5);
            Check(EmpireCraft.Scripts.Regimes.KingdomType.Feudalism_papal_state, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_6);
            Check(EmpireCraft.Scripts.Regimes.KingdomType.default_country_post, EmpireCraft.Scripts.Enums.PeeragesLevel.peerages_3);
            if (EmpireCraft.Scripts.Regimes.WesternPeerageRules.TryGetRulerLevel(
                EmpireCraft.Scripts.Regimes.KingdomType.LvLing_kingdom, out _))
                throw new System.Exception("Non-Western type was remapped");
            count++;
            return count;
        }
    }
}
'@

$code = "using EmpireCraft.Scripts.Enums;`nnamespace EmpireCraft.Scripts.Enums {`n$enums`n}`n" +
    "namespace EmpireCraft.Scripts.Regimes {`n$kingdomType`n$rules`n}" + [Environment]::NewLine + $harness
Add-Type -TypeDefinition $code
Write-Output "$([EmpireCraft.Tests.WesternPeerageCases]::Run()) western peerage mapping assertions passed."

$kingdomPatch = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/KingdomPatch.cs') -Raw
$status = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckKingdomType.cs') -Raw
if (!$kingdomPatch.Contains('WesternPeerageRules.TryGetRulerLevel(kingdomType')) {
    throw 'New Western rulers are not synchronized immediately'
}
if (!$status.Contains('WesternPeerageRules.TryGetRulerLevel(newkingdomType')) {
    throw 'Western rulers are not synchronized after country rank changes'
}
Write-Output '2 western peerage wiring checks passed.'
