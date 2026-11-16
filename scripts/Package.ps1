[CmdletBinding()]
param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Probably Stolen',
    [switch]$TargetedUiValidated,
    [switch]$UserRuntimeValidated
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Get-GameDirectory $GameDir
& (Join-Path $PSScriptRoot 'Build.ps1') -GameDir $game
[xml]$manifest = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot 'src\DeepSpaceQoL\manifest.xml') -Raw
$version = [string]$manifest.Manifest.ModVersion
$validationDoc = "docs/runtime-validation-v$version.md"
if ($TargetedUiValidated -and $UserRuntimeValidated) { throw 'Choose one runtime validation source.' }
if (($TargetedUiValidated -or $UserRuntimeValidated) -and -not (Test-Path -LiteralPath (Join-Path $script:RepositoryRoot $validationDoc) -PathType Leaf)) {
    throw "Provide the version-specific runtime validation record before asserting in-game tests: $validationDoc"
}
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$output = Join-Path $script:RepositoryRoot "artifacts\$version\$stamp\deep_space_qol"
$null = New-Item -ItemType Directory -Path $output
foreach ($name in @('DeepSpaceQoL.dll', 'manifest.xml')) {
    Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot "src\DeepSpaceQoL\bin\Release\netstandard2.1\$name") -Destination $output
}
foreach ($name in @('README.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot $name) -Destination $output
}
$info = [ordered]@{
    modId = $script:ModId
    version = $version
    builtAt = (Get-Date).ToString('o')
    gmIncluded = $false
    targetFramework = 'netstandard2.1'
    gameAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $game 'Probably Stolen_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    tests = @('StoragePolicy', 'GridPacking', 'GridPacking.IncrementalTests', 'StockPlanner', 'LiquidPolicy', 'BuyerPolicy.NativeTests', 'StorageCatalog.NativeTests', 'StowIncremental.NativeTests', 'BottlingAccess.NativeTests', 'Appraisal.NativeTests', 'BerryQuality.NativeTests', 'SecurityHighlights.NativeTests', 'CustomerHints.NativeTests', 'Authenticity.NativeTests', 'DisplayFixtures.NativeTests', 'DailyVisitors.NativeTests', 'GunModding.NativeTests', 'ServiceCooldowns.NativeTests', 'WaterJugPrinter.NativeTests', 'ChemicalBatch.NativeTests', 'ChemicalMix.NativeTests')
    inGameTested = $TargetedUiValidated.IsPresent -or $UserRuntimeValidated.IsPresent
    inGameTestScope = if ($TargetedUiValidated) { "Targeted UI on an isolated save copy; controlled customers/items. See $validationDoc in source." }
        elseif ($UserRuntimeValidated) { "User-reported play and supplied screenshots for version $version. Exact evidence, confirmed scope and exclusions are recorded in $validationDoc in source. No agent-controlled full playthrough or complete overnight/save validation asserted." }
        else { 'Not asserted by this package invocation.' }
    fullPlaythroughTested = $false
}
$info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'BUILD-INFO.json') -Encoding utf8
Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tools\NativeAudit\NativeAudit.csproj'), '-c', 'Release', "--property:GameDir=$game", '--', (Join-Path $game 'Probably Stolen_Data\Managed'), $output, '--require-no-gm')
$sums = foreach ($file in (Get-ChildItem -LiteralPath $output -File | Sort-Object Name)) {
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.Name
}
$sums | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Assert-Package $output
Write-Host "Validated package: $output"
return $output
