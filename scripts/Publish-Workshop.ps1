[CmdletBinding()]
param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Probably Stolen',
    [string]$PackagePath,
    [string]$PreviewPath,
    [string]$GalleryPath,
    [string]$StatePath,
    [string]$ChangeNote,
    [ValidateSet('public', 'private')][string]$Visibility = 'public',
    [switch]$Status,
    [switch]$ValidateOnly
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Get-GameDirectory $GameDir
$project = Join-Path $script:RepositoryRoot 'tools\WorkshopPublisher\WorkshopPublisher.csproj'
$arguments = @('run', '--project', $project, '-c', 'Release', "--property:GameDir=$game", '--')
if ($Status) {
    $arguments += @('--status', '--game-dir', $game)
} else {
    if ([string]::IsNullOrWhiteSpace($PackagePath) -or [string]::IsNullOrWhiteSpace($PreviewPath)) {
        throw 'Provide -PackagePath and -PreviewPath. No upload was attempted.'
    }
    $package = (Resolve-Path -LiteralPath $PackagePath).Path
    Assert-Package $package
    if ([string]::IsNullOrWhiteSpace($StatePath)) { $StatePath = Join-Path $script:RepositoryRoot 'release\workshop-state.local.json' }
    if ([string]::IsNullOrWhiteSpace($GalleryPath)) { $GalleryPath = Join-Path $script:RepositoryRoot 'release\media\gallery' }
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.xml') -Raw
    if ([string]::IsNullOrWhiteSpace($ChangeNote)) { $ChangeNote = "Deep Space QoL $($manifest.Manifest.ModVersion)" }
    $operation = if ($ValidateOnly) { '--validate' } else { '--publish' }
    $arguments += @($operation, '--game-dir', $game, '--content', $package,
        '--preview', (Resolve-Path -LiteralPath $PreviewPath).Path,
        '--description', (Join-Path $script:RepositoryRoot 'release\workshop-description.zh-CN.txt'),
        '--state', [IO.Path]::GetFullPath($StatePath), '--visibility', $Visibility,
        '--notes', $ChangeNote,
        '--gallery-dir', [IO.Path]::GetFullPath($GalleryPath))
}
& dotnet @arguments
if ($LASTEXITCODE -eq 4) { throw 'Steam requires the account owner to accept the Workshop legal agreement. Follow the official item URL above, then rerun the same command; its ID is preserved.' }
if ($LASTEXITCODE -ne 0) { throw "Workshop tool failed with exit code $LASTEXITCODE. Existing publish state is preserved." }
