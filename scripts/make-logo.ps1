# Draws Pluto's logo, its face in dots on a soft black squircle, as PNGs and a multi-size .ico:
#   powershell -File scripts\make-logo.ps1
# Small sizes get fewer, bigger dots so the face stays crisp at 16 px.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\Pluto\Assets\Logo'
New-Item -ItemType Directory -Force $out | Out-Null

function Draw-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Squircle: a rounded square, softly lit from the top.
    $inset = [Math]::Max(0.5, $size * 0.02)
    $s = $size - 2 * $inset
    $r = $s * 0.27
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($inset + $s - 2 * $r, $inset, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($inset + $s - 2 * $r, $inset + $s - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($inset, $inset + $s - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, $inset)), ([System.Drawing.PointF]::new(0, $inset + $s)), ([System.Drawing.Color]::FromArgb(255, 40, 40, 44)), ([System.Drawing.Color]::FromArgb(255, 6, 6, 8))
    $g.FillPath($fill, $path)
    if ($size -ge 48) {
        $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(46, 255, 255, 255)), ([Math]::Max(1, $size / 128))
        $g.DrawPath($edge, $path)
    }

    # The face, in the same proportions as the pet's: eyes up top, a smile below.
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 250, 250, 250))
    if ($size -ge 48) { $cols = 2; $rows = 3; $smile = 5 } elseif ($size -ge 32) { $cols = 2; $rows = 2; $smile = 3 } else { $cols = 1; $rows = 2; $smile = 3 }
    $pitch = $size * ($(if ($size -ge 48) { 0.085 } elseif ($size -ge 32) { 0.11 } else { 0.15 }))
    $dot = $pitch * 0.74
    $gap = $size * 0.30
    $eyeY = $size * 0.40
    foreach ($side in -1, 1) {
        $cx = $size / 2 + $side * $gap / 2
        for ($rI = 0; $rI -lt $rows; $rI++) {
            for ($cI = 0; $cI -lt $cols; $cI++) {
                $x = $cx + ($cI - ($cols - 1) / 2) * $pitch
                $y = $eyeY + ($rI - ($rows - 1) / 2) * $pitch
                $g.FillEllipse($white, $x - $dot / 2, $y - $dot / 2, $dot, $dot)
            }
        }
    }
    $lift = if ($smile -eq 5) { @(0, 0.6, 0.85, 0.6, 0) } else { @(0, 0.7, 0) }
    $smileY = $size * 0.665
    $sdot = $dot * 0.82
    for ($i = 0; $i -lt $smile; $i++) {
        $x = $size / 2 + ($i - ($smile - 1) / 2) * $pitch * 1.05
        $y = $smileY + $lift[$i] * $pitch * 0.62
        $g.FillEllipse($white, $x - $sdot / 2, $y - $sdot / 2, $sdot, $sdot)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = Draw-Logo $size
    $file = Join-Path $out "pluto-$size.png"
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , [IO.File]::ReadAllBytes($file)
}
(Draw-Logo 1024).Save((Join-Path $out 'pluto-1024.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# An .ico of PNG frames (Windows Vista and later read PNG frames at every size).
$ico = Join-Path $out 'pluto.ico'
$stream = [IO.File]::Create($ico)
$w = New-Object IO.BinaryWriter $stream
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = $(if ($s -ge 256) { 0 } else { $s })
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Dispose()
Write-Host "Logo: $out"
