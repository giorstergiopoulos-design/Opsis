# Generates the Inno Setup wizard images of Opsis (large Welcome/Finish image and small header image, at 100/125/150/200 % DPI) from assets\Opsis.ico.
# No text is drawn into the images (the installer is translated into 14 languages). Run: powershell -File installer\make-wizard-images.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot 'wizard'
New-Item -ItemType Directory -Force $out | Out-Null
# the largest frame of the .ico (a PNG frame, which System.Drawing.Icon cannot render, so it is cut out by hand)
$bytes = [System.IO.File]::ReadAllBytes((Join-Path $root 'assets\Opsis.ico'))
$count = [BitConverter]::ToUInt16($bytes, 4); $best = $null; $bestW = 0
for ($i = 0; $i -lt $count; $i++) {
    $o = 6 + 16 * $i; $fw = $bytes[$o]; if ($fw -eq 0) { $fw = 256 }
    if ($fw -gt $bestW) { $bestW = $fw; $best = @([BitConverter]::ToUInt32($bytes, $o + 12), [BitConverter]::ToUInt32($bytes, $o + 8)) }
}
$frame = New-Object byte[] $best[1]
[Array]::Copy($bytes, $best[0], $frame, 0, $best[1])
$logo = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream (,$frame)))

function New-Large([int]$w, [int]$h, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(10, 22, 40)), ([System.Drawing.Color]::FromArgb(14, 110, 130)), 90
    $g.FillRectangle($grad, $rect)
    # soft glow behind the logo
    $d = [int]($w * 0.78)
    $cx = [int](($w - $d) / 2); $cy = [int]($h * 0.16)
    $path2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path2.AddEllipse($cx - $d / 4, $cy - $d / 4, $d * 1.5, $d * 1.5)
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $path2
    $pg.CenterColor = [System.Drawing.Color]::FromArgb(90, 0, 200, 255)
    $pg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 0, 200, 255))
    $g.FillPath($pg, $path2)
    $g.DrawImage($logo, $cx, $cy, $d, $d)
    # decorative bars (no text)
    $bar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 255, 255, 255))
    $y = [int]($h * 0.52); $bh = [Math]::Max(3, [int]($h * 0.012)); $gap = [int]($h * 0.04)
    foreach ($k in @(0.84, 0.66, 0.78, 0.5, 0.72)) {
        $bw = [int](($w - 2 * ($w * 0.1)) * $k)
        $g.FillRectangle($bar, [int]($w * 0.1), $y, $bw, $bh); $y += $gap
    }
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp); $bmp.Dispose()
}

function New-Small([int]$s, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::FromArgb(12, 26, 46))
    $m = [int]($s * 0.08)
    $g.DrawImage($logo, $m, $m, $s - 2 * $m, $s - 2 * $m)
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp); $bmp.Dispose()
}

$scales = @{ '100' = 1.0; '125' = 1.25; '150' = 1.5; '200' = 2.0 }
foreach ($k in $scales.Keys) {
    $f = $scales[$k]
    New-Large ([int][Math]::Round(164 * $f)) ([int][Math]::Round(314 * $f)) (Join-Path $out "large-$k.bmp")
    New-Small ([int][Math]::Round(55 * $f)) (Join-Path $out "small-$k.bmp")
}
$logo.Dispose()
Get-ChildItem $out | Select-Object Name, Length
