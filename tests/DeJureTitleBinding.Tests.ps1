$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rules = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/DeJureTitleBindingRules.cs') -Raw
$harness = @'
namespace EmpireCraft.Tests
{
    public static class DeJureTitleBindingTests
    {
        private static int count;
        private static void Check(bool condition, string scenario)
        {
            if (!condition) throw new System.Exception(scenario);
            count++;
        }

        public static int Run()
        {
            count = 0;
            Check(EmpireCraft.Scripts.Layer.DeJureTitleBindingRules.WeakMandateThreshold == 60,
                "Mandate threshold changed");
            Check(EmpireCraft.Scripts.Layer.DeJureTitleBindingRules.IsAdministrativeAcquisitionBlocked(true, true, 61),
                "Strong LvLing administration can acquire title");
            Check(!EmpireCraft.Scripts.Layer.DeJureTitleBindingRules.IsAdministrativeAcquisitionBlocked(true, true, 60),
                "Weak mandate boundary blocks acquisition");
            Check(!EmpireCraft.Scripts.Layer.DeJureTitleBindingRules.IsAdministrativeAcquisitionBlocked(true, false, 100),
                "Non-administrative vassal is incorrectly blocked");
            Check(!EmpireCraft.Scripts.Layer.DeJureTitleBindingRules.IsAdministrativeAcquisitionBlocked(false, true, 100),
                "Non-LvLing administration is incorrectly blocked");
            Check(EmpireCraft.Scripts.Layer.DeJureTitleBindingRules.GetLandedPeerageKey(true) == "default_peerages_2",
                "Imperial clan is not a king");
            Check(EmpireCraft.Scripts.Layer.DeJureTitleBindingRules.GetLandedPeerageKey(false) == "tang_peerage_guogong",
                "Different clan is not a state duke");
            return count;
        }
    }
}
'@
Add-Type -TypeDefinition ($rules + [Environment]::NewLine + $harness)
$count = [EmpireCraft.Tests.DeJureTitleBindingTests]::Run()
Write-Output "$count de jure title rule assertions passed."

$empire = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/Empire.cs') -Raw
$kingdom = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$actor = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/ActorExtension.cs') -Raw
$plots = Get-Content -LiteralPath (Join-Path $root 'Scripts/AI/EmpireCraftPlotsAddition.cs') -Raw
$war = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/WarPatch.cs') -Raw
$ui = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/EmpireBeaurauWindow.cs') -Raw
$holyLand = Get-Content -LiteralPath (Join-Path $root 'Scripts/Regimes/TemporaryFactions/Claims/TempFac_恢复圣地.cs') -Raw

if (!$empire.Contains('if (GetLandedLegalTitleKingdom(title) != null) continue;')) {
    throw 'Landed title can still receive an unlanded successor'
}
if (!$empire.Contains('duplicateData.virtual_enfeoff = false;')) {
    throw 'Duplicate unlanded holder is not cleared'
}
if (!$empire.Contains('data.legal_peerage_kingdoms[title.id] = kingdom.id;')) {
    throw 'Landed title is not persisted against its kingdom'
}
if (!$kingdom.Contains('kingdom.GetEmpire()?.SynchronizeLandedLegalTitles(kingdom);')) {
    throw 'Main-title reconciliation is not synchronized'
}
if (!$kingdom.Contains('empire.Mandate <= DeJureTitleBindingRules.WeakMandateThreshold')) {
    throw 'Weak mandate does not bypass the administrative diplomacy lock for title acquisition'
}
if (!$actor.Contains('claimant.kingdom.IsDeJureTitleAcquisitionBlocked(empire)')) {
    throw 'Takeable-title filtering does not enforce the mandate rule'
}
if (!$plots.Contains('if (kingdom.IsDeJureTitleAcquisitionBlocked()) return false;')) {
    throw 'Title creation plots bypass the mandate rule'
}
if (!$war.Contains('kingdom.GetEmpire()?.SynchronizeLandedLegalTitles(kingdom);')) {
    throw 'War title transfer is not synchronized'
}
if (!$ui.Contains('LM.Get("label_landed_fief")') -or !$ui.Contains('LM.Get("label_unlanded_fief")')) {
    throw 'Peerage cards do not display landed status'
}
if (!$holyLand.Contains('empire.SynchronizeLandedLegalTitles(kingdom);')) {
    throw 'Holy-land restoration bypasses landed title synchronization'
}
Write-Output '10 de jure title wiring assertions passed.'
