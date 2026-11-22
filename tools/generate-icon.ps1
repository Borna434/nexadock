# Generates assets/NexaDock.ico (multi-resolution, PNG-compressed entries) with System.Drawing.
# Usage: powershell -ExecutionPolicy Bypass -File tools/generate-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\NexaDock.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [Math]::Max(1, $s * 0.04)
    $body = New-RoundedPath $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.24)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 34, 92, 196)), ([System.Drawing.Color]::FromArgb(255, 112, 52, 196))
    $g.FillPath($grad, $body)

    # Glass sheen on the upper half.
    $sheen = New-RoundedPath ($m + $s * 0.06) ($m + $s * 0.05) ($s - 2 * $m - $s * 0.12) ($s * 0.38) ($s * 0.18)
    $sheenBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, ($s * 0.45)), ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($sheenBrush, $sheen)

    # Dock pill.
    $pw = $s * 0.64; $ph = $s * 0.2
    $px = ($s - $pw) / 2; $py = $s * 0.62
    $pill = New-RoundedPath $px $py $pw $ph ($ph / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))), $pill)

    # Three app dots.
    $dot = $ph * 0.5
    $colors = @([System.Drawing.Color]::FromArgb(255, 47, 124, 246), [System.Drawing.Color]::FromArgb(255, 94, 210, 190), [System.Drawing.Color]::FromArgb(255, 255, 150, 90))
    for ($i = 0; $i -lt 3; $i++) {
        $cx = $px + $pw * (0.25 + 0.25 * $i)
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $colors[$i]), [float]($cx - $dot / 2), [float]($py + ($ph - $dot) / 2), [float]$dot, [float]$dot)
    }

    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([Math]::Max(1, $s / 64))
        $g.DrawPath($pen, $body)
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (New-IconPng $s) }

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush(); $fs.Close()
Write-Host "Wrote $out"
