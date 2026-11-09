[CmdletBinding()]
param(
    [string]$StockingImage,
    [string]$LabelsImage,
    [string]$BeforeImage,
    [string]$AfterImage,
    [string]$BottlingBeforeImage,
    [string]$BottlingAfterImage,
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'release\media\gallery')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$images = @()
if ($StockingImage -or $LabelsImage -or $BeforeImage -or $AfterImage) {
    if (-not ($StockingImage -and $LabelsImage -and $BeforeImage -and $AfterImage)) { throw 'Provide all four original gallery images together.' }
    $images += @(
        @{ Path = $StockingImage; Name = '01-budget-stocking.jpg'; Title = '按买家需求与预算，一键上货'; Version = '0.2.1' },
        @{ Path = $LabelsImage; Name = '02-storage-labels.jpg'; Title = '原生标签打印机：分类多选与页／箱锁'; Version = '0.2.1' },
        @{ Path = $BeforeImage; Name = '03-before-arrange.jpg'; Title = '同一储物页：整理前'; Version = '0.2.1' },
        @{ Path = $AfterImage; Name = '04-after-arrange.jpg'; Title = '同一储物页：整理后'; Version = '0.2.1' }
    )
}
if ($BottlingBeforeImage -or $BottlingAfterImage) {
    if (-not ($BottlingBeforeImage -and $BottlingAfterImage)) { throw 'Provide both bottling screenshots together.' }
    $images += @(
        @{ Path = $BottlingBeforeImage; Name = '05-before-bottling.jpg'; Title = '固定水源右键装瓶：操作前'; Version = '0.2.2' },
        @{ Path = $BottlingAfterImage; Name = '06-after-bottling.jpg'; Title = '交易台与打印机成品瓶：装瓶后（4 瓶）'; Version = '0.2.2' }
    )
}
if ($images.Count -eq 0) { throw 'Provide a gallery image set or a bottling image pair.' }
$encoder = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -EQ 'image/jpeg'
foreach ($entry in $images) {
    $source = [Drawing.Image]::FromFile((Resolve-Path -LiteralPath $entry.Path).Path)
    $canvas = [Drawing.Bitmap]::new(1600, 980)
    $graphics = [Drawing.Graphics]::FromImage($canvas)
    $titleFont = [Drawing.Font]::new('Microsoft YaHei UI', 30, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $versionFont = [Drawing.Font]::new('Microsoft YaHei UI', 18, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $muted = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#AFC0D0'))
    $parameters = [Drawing.Imaging.EncoderParameters]::new(1)
    $parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]92)
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#101D2E'))
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.DrawString($entry.Title, $titleFont, $white, [single]26, [single]9)
        $graphics.DrawString(('深空管家 / Deep Space QoL · 用户实机截图 · v' + $entry.Version), $versionFont, $muted, [single]28, [single]49)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        # Preserve the full original screenshot; the caption sits outside the game image.
        $ratio = [Math]::Min(1600.0 / $source.Width, 900.0 / $source.Height)
        $width = [int]($source.Width * $ratio); $height = [int]($source.Height * $ratio)
        $graphics.DrawImage($source, [int]((1600 - $width) / 2), [int](80 + (900 - $height) / 2), $width, $height)
        $path = Join-Path $OutputDirectory $entry.Name
        $canvas.Save([IO.Path]::GetFullPath($path), $encoder, $parameters)
        if ((Get-Item -LiteralPath $path).Length -ge 1MB) { throw "Gallery image exceeds 1 MiB: $path" }
        Write-Output $path
    } finally {
        $parameters.Dispose(); $white.Dispose(); $muted.Dispose()
        $titleFont.Dispose(); $versionFont.Dispose(); $graphics.Dispose(); $canvas.Dispose(); $source.Dispose()
    }
}
