param(
    [Parameter(Mandatory = $true)]
    [string[]] $EvidencePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $CosmeticCatalogPath = (Join-Path $PSScriptRoot '..\cosmetic_catalog.json')
)

$ErrorActionPreference = 'Stop'

$cosmetics = Get-Content -Raw -LiteralPath $CosmeticCatalogPath | ConvertFrom-Json

$parsedDemoCount = 0
$allPlacements = foreach ($path in $EvidencePath) {
    $evidence = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if ($evidence.demos_parsed -le 0 -or $evidence.failures.Count -ne 0 -or $evidence.placements.Count -eq 0) {
        throw "Evidence report '$path' must contain successful demo parses and no failures."
    }
    $parsedDemoCount += [int] $evidence.demos_parsed
    $evidence.placements
}

$placements = $allPlacements | Group-Object {
    '{0}|{1}|{2}|{3}|{4:R}|{5:R}|{6:R}' -f `
        $_.demo_sha256, $_.steam_id, $_.weapon_def_index, $_.charm_id, `
        [single] $_.offset_x, [single] $_.offset_y, [single] $_.offset_z
} | ForEach-Object { $_.Group[0] }
$demoHashes = @($placements.demo_sha256 | Sort-Object -Unique)

$weaponsByDefIndex = @{}
foreach ($weapon in $cosmetics.weapons) {
    $weaponsByDefIndex[[int] $weapon.defIndex] = [string] $weapon.designerName
}

$weaponDocuments = foreach ($weaponGroup in ($placements | Group-Object weapon_def_index | Sort-Object { [int] $_.Name })) {
    $defIndex = [int] $weaponGroup.Name
    if (-not $weaponsByDefIndex.ContainsKey($defIndex)) {
        throw "Evidence contains unknown weapon definition $defIndex."
    }

    $positionDocuments = foreach ($positionGroup in ($weaponGroup.Group | Group-Object {
        '{0:R}|{1:R}|{2:R}' -f [single] $_.offset_x, [single] $_.offset_y, [single] $_.offset_z
    })) {
        $first = $positionGroup.Group[0]
        [pscustomobject] [ordered] @{
            x = [single] $first.offset_x
            y = [single] $first.offset_y
            z = [single] $first.offset_z
            observations = $positionGroup.Count
            demoCount = @($positionGroup.Group.demo_sha256 | Sort-Object -Unique).Count
        }
    }

    [pscustomobject] [ordered] @{
        defIndex = $defIndex
        designerName = $weaponsByDefIndex[$defIndex]
        placements = @($positionDocuments | Sort-Object x, y, z)
    }
}

$document = [ordered] @{
    schemaVersion = 1
    source = [ordered] @{
        kind = 'demo-observed'
        parser = 'cs2-demo-botmimic/cs2-demotracer'
        demosParsed = $parsedDemoCount
        demoSha256 = $demoHashes
    }
    weapons = @($weaponDocuments)
}

$json = $document | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
