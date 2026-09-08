$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$window = Get-Content -LiteralPath (Join-Path $root 'Scripts/UI/Windows/KingdomTitleWindow.cs') -Raw
$title = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/KingdomTitle.cs') -Raw
$data = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/KingdomTitleData.cs') -Raw
$kingdom = Get-Content -LiteralPath (Join-Path $root 'Scripts/GameClassExtensions/KingdomExtension.cs') -Raw
$kingdomPatch = Get-Content -LiteralPath (Join-Path $root 'Scripts/GamePatches/KingdomPatch.cs') -Raw
$relations = Get-Content -LiteralPath (Join-Path $root 'Scripts/Layer/KingdomTitleRelationResolver.cs') -Raw
$locales = @{
    cz = Get-Content -LiteralPath (Join-Path $root 'Locales/cz.json') -Raw | ConvertFrom-Json -AsHashtable
    ch = Get-Content -LiteralPath (Join-Path $root 'Locales/ch.json') -Raw | ConvertFrom-Json -AsHashtable
    en = Get-Content -LiteralPath (Join-Path $root 'Locales/en.json') -Raw | ConvertFrom-Json -AsHashtable
}

if (!$window.Contains('BuildCurrentRelations(administrations);')) { throw 'Current holder/administration section missing' }
if (!$window.Contains('ResolveCurrentHolders()')) { throw 'Multiple title holders are not resolved' }
if (!$relations.Contains('foreach (Empire empire in EmpireCoreManager.GetEmpires(core)')) {
    throw 'Unlanded title holders are not collected per empire core'
}
if ($relations.Contains('if (holder != null && !holder.isRekt()) break;')) {
    throw 'Title holder lookup still stops after the first empire'
}
if (!$relations.Contains('empire.GetLegalPeerageHolder(title)')) {
    throw 'Each empire does not resolve its own legal peerage holder'
}
if (!$window.Contains('KingdomTitleRelationResolver.ResolveCurrentHolders(title)') -or
    !$window.Contains('KingdomTitleRelationResolver.FindCurrentAdministrations(title)')) {
    throw 'Title window does not consume the shared current-relation resolver'
}
if (!$window.Contains('return $"{empireName} · {peerageName}";') -or
    !$window.Contains('string empireName = empire?.GetEmpireName() ?? "";')) {
    throw 'Peerage cards do not prefix the suffix-free empire name'
}
if (!$relations.Contains('GetAdministrativeTitle() == title')) { throw 'Delegated administration lookup missing' }
if (!$window.Contains('BuildJurisdictionHistory(administrations);')) { throw 'Jurisdiction history section missing' }
if (!$window.Contains('BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount')) {
    throw 'City list is not rendered as a four-column grid'
}
if (!$window.Contains('HoverMarqueeText.Attach')) { throw 'Long title labels do not use the shared marquee component' }
if (!$window.Contains('MetaType.Kingdom.getAsset().selectAndInspect')) { throw 'Kingdom cards are not interactive' }
if (!$window.Contains('LM.Get("kingdom_title_details")')) { throw 'Kingdom details button uses an unscoped locale key' }
if ($locales.cz.kingdom_title_details -ne '详情' -or $locales.ch.kingdom_title_details -ne '詳情' -or
    $locales.en.kingdom_title_details -ne 'Details') {
    throw 'Kingdom details button is not localized in every supported language'
}
if (!$window.Contains('MetaType.City.getAsset().selectAndInspect')) { throw 'City cards are not interactive' }
if (([regex]::Matches($window, 'layout\.padding = new RectOffset\(3, 3, 80, 3\);')).Count -lt 2) {
    throw 'Scrollable title content does not reserve the fixed identity-card height'
}
if (!$window.Contains('private AutoVertLayoutGroup _topPart;') -or
    !$window.Contains('_topPart.gameObject.AdjustTopPart(transform.parent.transform, new Vector2(0, 1));')) {
    throw 'Title identity card is not mounted as a fixed top part'
}
if ($window.IndexOf('BuildIdentityCard();') -gt $window.IndexOf('_content = this.BeginVertGroup')) {
    throw 'Fixed identity card is created inside the scrolling content'
}
if (([regex]::Matches($window, 'AddAvatarSlot\(body,')).Count -lt 2) {
    throw 'Current relation cards do not share the fixed avatar slot'
}
if (!$window.Contains('parent.BeginVertGroup(new Vector2(30, 40)') -or
    !$window.Contains('avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);')) {
    throw 'Current relation avatar dimensions are not constrained'
}
if (!$data.Contains('List<KingdomTitleJurisdictionRecord> jurisdiction_history')) {
    throw 'Jurisdiction history is not persisted'
}
if (!$title.Contains('public void RecordJurisdiction(') -or !$title.Contains('public void EndJurisdiction(')) {
    throw 'Jurisdiction lifecycle is incomplete'
}
if (!$kingdom.Contains('current.EndJurisdiction(k, KingdomTitle.JurisdictionHolder);')) {
    throw 'Replacing a main title leaves its history record active'
}
if (!$kingdom.Contains('title.RecordJurisdiction(k, KingdomTitle.JurisdictionHolder);')) {
    throw 'Landed title assignment is not recorded'
}
if (!$kingdom.Contains('title.RecordJurisdiction(kingdom, KingdomTitle.JurisdictionAdministration);')) {
    throw 'Administrative delegation is not recorded'
}
if (!$kingdomPatch.Contains('administrativeTitle?.EndJurisdiction(__instance, KingdomTitle.JurisdictionAdministration);')) {
    throw 'Destroyed administrations leave active history records'
}

Write-Output '26 kingdom title window assertions passed.'
