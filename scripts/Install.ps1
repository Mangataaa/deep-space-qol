[CmdletBinding()]
param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Probably Stolen',
    [string]$PackagePath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Get-GameDirectory $GameDir
Assert-GameStopped
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = & (Join-Path $PSScriptRoot 'Package.ps1') -GameDir $game
}
$package = (Resolve-Path -LiteralPath $PackagePath).Path
Assert-Package $package
$mods = Join-Path $game 'Mods'
Assert-PlainPath $mods
$null = New-Item -ItemType Directory -Path $mods -Force
$destination = Join-Path $mods $script:ModId
if ([IO.Path]::GetFullPath($package) -ieq [IO.Path]::GetFullPath($destination)) { throw 'Package is already the installed folder.' }
# Stage outside Mods so the loader cannot discover an incomplete installation.
$backupRoot = Join-Path $game 'ModBackups'
Assert-PlainPath $backupRoot
$null = New-Item -ItemType Directory -Path $backupRoot -Force
$stage = Join-Path $backupRoot ('install-stage-' + [guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $package -Destination $stage -Recurse
Assert-Package $stage
Assert-GameStopped
$backup = $null
if (Test-Path -LiteralPath $destination) { $backup = Move-ModToBackup $game $destination }
try {
    Move-Item -LiteralPath $stage -Destination $destination
    Assert-Package $destination
} catch {
    # Preserve a failed new copy for diagnostics, then restore the old version.
    if (Test-Path -LiteralPath $destination) {
        $failed = Move-ModToBackup $game $destination
        Write-Warning "New installation retained at: $failed"
    }
    if ($backup -and -not (Test-Path -LiteralPath $destination)) { Move-Item -LiteralPath $backup -Destination $destination }
    throw
}
if ($backup) { Write-Host "Previous version preserved at: $backup" }
Write-Host "Installed and checksum-verified: $destination"
Write-Host 'Enable Deep Space QoL in the game Mods menu and restart. Use native storage buttons, label printer and water-source context menu; F6 is optional advanced settings.'
