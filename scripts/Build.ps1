[CmdletBinding()]
param([string]$GameDir = 'D:\SteamLibrary\steamapps\common\Probably Stolen')
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Get-GameDirectory $GameDir
$property = "--property:GameDir=$game"
Invoke-DotNet -Arguments @('build', (Join-Path $script:RepositoryRoot 'src\DeepSpaceQoL\DeepSpaceQoL.csproj'), '-c', 'Release', $property)
foreach ($project in @('StoragePolicy', 'GridPacking', 'StockPlanner', 'LiquidPolicy')) {
    Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot "tests\$project.Tests.csproj"), '-c', 'Release', $property)
}
Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tests\GridPacking.IncrementalTests.csproj'), '-c', 'Release', $property)
Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tests\BuyerPolicy.NativeTests.csproj'), '-c', 'Release', $property, '--', (Join-Path $game 'Probably Stolen_Data\Managed'))
Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tests\StorageCatalog.NativeTests.csproj'), '-c', 'Release', $property)
Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tests\StowIncremental.NativeTests.csproj'), '-c', 'Release', $property, '--', (Join-Path $game 'Probably Stolen_Data\Managed'))
Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot 'tests\BottlingAccess.NativeTests.csproj'), '-c', 'Release', $property, '--', (Join-Path $game 'Probably Stolen_Data\Managed'))
foreach ($project in @('Appraisal', 'BerryQuality', 'SecurityHighlights', 'CustomerHints', 'Authenticity', 'DisplayFixtures', 'DailyVisitors', 'GunModding')) {
    Invoke-DotNet -Arguments @('run', '--project', (Join-Path $script:RepositoryRoot "tests\$project.NativeTests.csproj"), '-c', 'Release', $property, '--', (Join-Path $game 'Probably Stolen_Data\Managed'))
}
Write-Host 'Build and all algorithm tests passed.'
