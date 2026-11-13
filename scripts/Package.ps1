[CmdletBinding()]
param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Probably Stolen',
    [switch]$TargetedUiValidated
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Get-GameDirectory $GameDir
& (Join-Path $PSScriptRoot 'Build.ps1') -GameDir $game
[xml]$manifest = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot 'src\DeepSpaceQoL\manifest.xml') -Raw
$version = [string]$manifest.Manifest.ModVersion
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
    targetFramework = 'netstandard2.1'
    gameAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $game 'Probably Stolen_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    tests = @('StoragePolicy', 'GridPacking', 'StockPlanner', 'LiquidPolicy', 'BuyerPolicy.NativeTests', 'StorageCatalog.NativeTests', 'BottlingAccess.NativeTests', 'Appraisal.NativeTests', 'BerryQuality.NativeTests', 'SecurityHighlights.NativeTests', 'CustomerHints.NativeTests')
    inGameTested = $TargetedUiValidated.IsPresent
    inGameTestScope = if ($TargetedUiValidated) { 'Targeted UI on an isolated save copy; controlled customers/items. See docs/runtime-validation-v0.3.0.md in source.' } else { 'Not asserted by this package invocation.' }
    fullPlaythroughTested = $false
}
$info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'BUILD-INFO.json') -Encoding utf8
Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tools\NativeAudit\NativeAudit.csproj'), '-c', 'Release', "--property:GameDir=$game", '--', (Join-Path $game 'Probably Stolen_Data\Managed'), $output)
$sums = foreach ($file in (Get-ChildItem -LiteralPath $output -File | Sort-Object Name)) {
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.Name
}
$sums | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Assert-Package $output
Write-Host "Validated package: $output"
return $output
