[CmdletBinding()]
param([string]$GameDir = 'D:\SteamLibrary\steamapps\common\Probably Stolen')
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Get-GameDirectory $GameDir
Assert-GameStopped
$mods = Join-Path $game 'Mods'
Assert-PlainPath $mods
$destination = Join-Path $mods $script:ModId
if (-not (Test-Path -LiteralPath $destination)) { Write-Host 'Deep Space QoL is not installed.'; return }
$backup = Move-ModToBackup $game $destination
Write-Host "Uninstalled; recoverable files retained at: $backup"
Write-Host 'Save files and storage tags were preserved. Category filters and automation locks require the mod to be enabled.'
