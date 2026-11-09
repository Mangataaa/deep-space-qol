Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$script:ModId = 'deep_space_qol'

function Get-GameDirectory([string]$Path) {
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'Probably Stolen.exe') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $resolved 'Probably Stolen_Data\Managed\Assembly-CSharp.dll') -PathType Leaf)) {
        throw "Not a Probably Stolen installation: $resolved"
    }
    return $resolved
}

function Assert-GameStopped {
    if (@(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Please close Probably Stolen before installing or uninstalling. No process was stopped.'
    }
}

function Assert-PlainPath([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing a junction/symlink: $Path"
        }
    }
}

function Assert-ModFolder([string]$Path) {
    Assert-PlainPath $Path
    $manifestPath = Join-Path $Path 'manifest.xml'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "No mod manifest: $Path" }
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    if ($manifest.Manifest.ID -cne $script:ModId) { throw "Refusing an unrelated mod: $Path" }
    return $manifest
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

function Assert-Package([string]$Path) {
    $manifest = Assert-ModFolder $Path
    $allowed = @('DeepSpaceQoL.dll', 'manifest.xml', 'README.md', 'LICENSE', 'BUILD-INFO.json', 'SHA256SUMS.txt')
    $children = @(Get-ChildItem -LiteralPath $Path -Force)
    if ($children.Count -ne $allowed.Count) { throw 'Unexpected package file count.' }
    foreach ($item in $children) {
        Assert-PlainPath $item.FullName
        if ($item.PSIsContainer -or $item.Name -cnotin $allowed) { throw "Unexpected package member: $($item.Name)" }
    }
    if ([string]$manifest.Manifest.Assemblies.Assembly -cne 'DeepSpaceQoL.dll') { throw 'Unexpected assembly declaration.' }
    $seen = @{}
    foreach ($line in (Get-Content -LiteralPath (Join-Path $Path 'SHA256SUMS.txt'))) {
        if ($line -notmatch '^([0-9A-Fa-f]{64})  ([A-Za-z0-9_.-]+)$') { throw 'Invalid checksum record.' }
        $expected = $Matches[1]; $name = $Matches[2]
        if ($name -ceq 'SHA256SUMS.txt' -or $name -cnotin $allowed -or $seen.ContainsKey($name)) { throw 'Unexpected checksum member.' }
        $seen[$name] = $true
        if ((Get-FileHash -LiteralPath (Join-Path $Path $name) -Algorithm SHA256).Hash -ine $expected) {
            throw "Package checksum mismatch: $name"
        }
    }
    if ($seen.Count -ne ($allowed.Count - 1)) { throw 'Incomplete package checksums.' }
}

function Move-ModToBackup([string]$Game, [string]$ModPath) {
    $expected = [IO.Path]::GetFullPath((Join-Path $Game "Mods\$script:ModId"))
    if ([IO.Path]::GetFullPath($ModPath) -ine $expected) { throw "Unexpected move target: $ModPath" }
    $null = Assert-ModFolder $ModPath
    $backupRoot = Join-Path $Game 'ModBackups'
    Assert-PlainPath $backupRoot
    $null = New-Item -ItemType Directory -Path $backupRoot -Force
    $backup = Join-Path $backupRoot ($script:ModId + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    if ((Split-Path -Parent ([IO.Path]::GetFullPath($backup))) -ine $backupRoot) { throw 'Backup escaped its directory.' }
    Move-Item -LiteralPath $ModPath -Destination $backup
    return $backup
}
