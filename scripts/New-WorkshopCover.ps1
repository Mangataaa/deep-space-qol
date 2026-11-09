[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'release\media\preview.png'),
    [string]$Python = 'python'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$generator = Join-Path $PSScriptRoot 'new_workshop_cover.py'
& $Python $generator --output ([IO.Path]::GetFullPath($OutputPath))
if ($LASTEXITCODE -ne 0) { throw 'Cover generation failed. Install Pillow for the selected Python runtime and retry.' }
