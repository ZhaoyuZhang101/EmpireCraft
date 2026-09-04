# Ancient Warfare 3 Compatibility

Optional bridge for the locally inspected Ancient Warfare 3 / Spring and Autumn
1.2.7f API. No dependency on its DLL, directory name, or private database.

- Activation requires its loaded `XiaizationService.GetLevel(Kingdom)` API and
  registered `Xia` actor asset. Without AW, EC's normal branches remain active.
- Native Xia realms and realms with an AW-reported Xiaization level above zero
  use AW/vanilla instead of EC country, city, character AI and political patches.
  Live `aw_xiaization_level` changes are read immediately. Old saves use AW's
  getter with a short, object-scoped cache, not a cross-world kingdom-ID cache.
- Xia maps to Huaxia only if the player has not configured a species mapping.
  Naming is now owned by EC for all races, independently of political isolation.
- EC callbacks fall back to their pre-EC implementation for AW-owned objects.
  Country patches check their own subject directly in their C# entry point.
  Do not install Harmony patches on EC's Harmony prefixes/postfixes: Mono can
  fail to emit these nested wrappers, disabling the entire mod at load time.
  Replaced AI behaviours fall back to the original game behaviours, retaining
  patches applied by AW. EC-only plots/bonuses do not run for AW-owned actors.
- EC diplomatic incorporation and EC manual powers do not take over AW realms.
  Simplified EC labels exclude them and call the game's kingdom label renderer
  instead. Existing EC empire records are retained rather than deleted on Xiaization.
- An unconverted subject whose AW overlord (direct or indirect) is Xia/Xiaized
  cannot form an EC empire. This uses `aw_vassal_suzerain_id`, which AW uses for
  its subject contracts, not the separate loose-tributary relation. Eligibility,
  forced plot start, continuation and the final creation gate all check it.
  Once independent, that extra subject restriction no longer applies.

`AncientWarfareCompatibility.Owns`, `OwnsObject`, and `BlocksEmpireFormation`
are the shared guards for future integrations. Keep new country-specific entry
points behind these guards rather than testing names or deleting saved EC data.

Run `pwsh -NoProfile -File tests/AncientWarfareCompatibility.Tests.ps1` for
bridge/callback and formation-gate regression checks. These use fake game objects;
they do not replace in-game joint-load testing (including both load orders,
existing saves, mid-game Xiaization, and an active formation plot becoming a vassal).

Run the same script with `-WithoutAw` in a fresh process to check operation without
the AW service assembly, including old Xia markers and EC nameplate eligibility.
No AW reference or dependency is added to EmpireCraft's project or mod manifest.
Native monkey-policy realms are not excluded merely because AW treats their
native policy as level 5; an explicit Xiaization marker still takes precedence.

## Naming Ownership

`AncientWarfareNaming` disables AW naming for all countries, including native Xia
and Xiaized realms. EC naming hooks no longer skip these actors/cultures. Saved
culture templates are prepared lazily using EC settings without adding political
traits to AW-owned cultures. Mid-game Xiaization does not restore AW naming.

Pure AW naming callbacks are unregistered from their original game methods, with
a throttled rescan for late registration. Mixed birth/clan/culture callbacks stay
installed: their naming services are suppressed instead, preserving newborn
affiliation, heirs, banners and Xia culture integration. Localized name projection,
periodic lineage renaming, clan/family naming, ruler appellations and nameplate
title overrides no longer write or substitute names. Mixed political restoration,
vassal creation and Western royal-clan binding retain their non-naming operations.

Only optional service methods are patched, never AW's Harmony callbacks. No AW
source files, localization database, or saved names are rewritten. Existing names
are not randomly regenerated on load. Without AW, these guards are not installed.
Run `pwsh -NoProfile -File tests/AncientWarfareNaming.Tests.ps1` for routing tests.
Run `pwsh -NoProfile -File tests/AncientWarfareNaming.Harmony.Tests.ps1` on Windows
for a smoke test using the installed NML Harmony binary and .NET Framework.
It checks both generator patch orders, foreign struct returns, and the selective
name-write transpiler without opening a game/save. This does not replace a joint
in-game test. NML may execute later result-writing prefixes even after EC skips
the original generator, so a per-call postfix preserves EC's generated result.

## Nameplate Routing

AW's late `Zones.getCurrentMapBorderMode` prefix does not recognize EC's custom
Empire/KingdomTitle modes and can replace them with `None`. The vanilla nameplate
manager then selects city labels. `AncientWarfareNameplates` restores EC's own
selection only for a None/city fallback, leaving real AW/other modes alone.
Kingdom, empire and legal nameplates follow `World.getCachedMapMetaAsset()`, the
layer actually drawn. The hide-nameplates setting remains respected.

AW's hierarchical renderer considers saved option flags as well as the cached
mode, and suppresses the shared nameplate canvas while active. Its service result
is restricted while an EC/kingdom layer is displayed, allowing AW's own cleanup
to restore that canvas. Actual AW layers keep their renderer. Country ownership,
territory colors and label styling are not changed by the nameplate-routing fix.

`AncientWarfareNameplates.Tests.ps1` tests the actual EC mode selector and routing
callbacks. `AncientWarfareNameplates.Harmony.Tests.ps1` checks NML's actual Harmony
binary with both registration orders, including switching back from the AW view.
These use simulated UI state, not a live Unity canvas or saved world.
