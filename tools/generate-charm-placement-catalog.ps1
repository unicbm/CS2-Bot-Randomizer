param(
    [Parameter(Mandatory = $true)]
    [string[]] $EvidencePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $CosmeticCatalogPath = (Join-Path $PSScriptRoot '..\cosmetic_catalog.json')
)

$ErrorActionPreference = 'Stop'

$cosmetics = Get-Content -Raw -LiteralPath $CosmeticCatalogPath | ConvertFrom-Json

$allPlacements = foreach ($path in $EvidencePath) {
    $evidence = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if ($evidence.demos_parsed -le 0 -or $evidence.failures.Count -ne 0 -or $evidence.placements.Count -eq 0) {
        throw "Evidence report '$path' must contain successful demo parses and no failures."
    }
    $evidence.placements
}

$placements = $allPlacements | Group-Object {
    '{0}|{1}|{2}|{3}|{4:R}|{5:R}|{6:R}' -f `
        $_.demo_sha256, $_.steam_id, $_.weapon_def_index, $_.charm_id, `
        [single] $_.offset_x, [single] $_.offset_y, [single] $_.offset_z
} | ForEach-Object { $_.Group[0] }

$knownWeaponDefIndexes = [System.Collections.Generic.HashSet[int]]::new()
foreach ($weapon in $cosmetics.weapons) {
    [void] $knownWeaponDefIndexes.Add([int] $weapon.defIndex)
}

$document = [ordered] @{}
foreach ($weaponGroup in ($placements | Group-Object weapon_def_index | Sort-Object { [int] $_.Name })) {
    $defIndex = [int] $weaponGroup.Name
    if (-not $knownWeaponDefIndexes.Contains($defIndex)) {
        throw "Evidence contains unknown weapon definition $defIndex."
    }

    $positions = foreach ($positionGroup in ($weaponGroup.Group | Group-Object {
        '{0:R}|{1:R}|{2:R}' -f [single] $_.offset_x, [single] $_.offset_y, [single] $_.offset_z
    })) {
        $first = $positionGroup.Group[0]
        [pscustomobject] [ordered] @{
            x = [single] $first.offset_x
            y = [single] $first.offset_y
            z = [single] $first.offset_z
        }
    }

    $vectors = [System.Collections.Generic.List[object]]::new()
    foreach ($position in ($positions | Sort-Object x, y, z)) {
        $vectors.Add([object[]] @($position.x, $position.y, $position.z))
    }

    $document[[string] $defIndex] = $vectors.ToArray()
}

$json = $document | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
