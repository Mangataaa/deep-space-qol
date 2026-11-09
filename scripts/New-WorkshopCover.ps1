[CmdletBinding()]
param([string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'release\media\preview.png'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$output = [IO.Path]::GetFullPath($OutputPath)
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force
$bitmap = [Drawing.Bitmap]::new(1024, 1024)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$resources = [Collections.Generic.List[IDisposable]]::new()
function Brush([string]$Hex) {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Hex))
    $resources.Add($brush)
    return $brush
}
function Font([single]$Size, [bool]$Bold = $false) {
    $style = if ($Bold) { [Drawing.FontStyle]::Bold } else { [Drawing.FontStyle]::Regular }
    $font = [Drawing.Font]::new('Microsoft YaHei UI', $Size, $style, [Drawing.GraphicsUnit]::Pixel)
    $resources.Add($font)
    return $font
}
function Text([string]$Value, [single]$Size, [string]$Color, [single]$X, [single]$Y, [bool]$Bold = $false) {
    $graphics.DrawString($Value, (Font $Size $Bold), (Brush $Color), [single]$X, [single]$Y)
}
function Tile([single]$X, [single]$Y, [string]$Number, [string]$Title, [string]$Detail) {
    $graphics.FillRectangle((Brush '#192D42'), $X, $Y, [single]420, [single]154)
    $graphics.FillRectangle((Brush '#55DCC5'), $X, $Y, [single]4, [single]154)
    Text $Number 20 '#55DCC5' ($X + 22) ($Y + 16) $true
    Text $Title 34 '#F3F7FB' ($X + 22) ($Y + 46) $true
    Text $Detail 22 '#AFC0D0' ($X + 22) ($Y + 106)
}
try {
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#101D2E'))
    $graphics.FillRectangle((Brush '#55DCC5'), 72, 78, 6, 218)
    Text 'PROBABLY STOLEN  /  MOD' 23 '#55DCC5' 100 76 $true
    Text '深空管家' 102 '#F3F7FB' 92 120 $true
    Text 'DEEP SPACE QoL' 43 '#B8CCDF' 100 248
    $graphics.FillRectangle((Brush '#30485E'), 76, 340, 872, 2)
    Text '四项操作，放回游戏原有界面' 33 '#D3DFEB' 76 375
    Tile 76 460 '01' '按预算上货' '兴趣 / 多样性 / 治安部规则'
    Tile 524 460 '02' '标签与锁定' '分类多选 · 页锁 · 箱锁'
    Tile 76 640 '03' '一键收纳整理' '同类归位 · 当前页整理'
    Tile 524 640 '04' '同品质装瓶' '固定水罐 · 台面 · 打印机'
    $graphics.FillRectangle((Brush '#244B4A'), 76, 876, 260, 55)
    Text 'v0.2.1 测试版' 29 '#A4F4DE' 92 884 $true
    Text '原生 Mod API · 无需 BepInEx' 25 '#AFC0D0' 372 888
    $bitmap.Save($output, [Drawing.Imaging.ImageFormat]::Png)
    Write-Output $output
} finally {
    foreach ($resource in $resources) { $resource.Dispose() }
    $graphics.Dispose()
    $bitmap.Dispose()
}
