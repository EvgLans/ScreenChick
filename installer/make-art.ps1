# Draws the duckling and writes the app icon, the logos and the installer's pictures.
# Run it only when the art changes: its results are committed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot
$yellow = [Drawing.Color]::FromArgb(0xFF, 0xC4, 0x00)
$shade = [Drawing.Color]::FromArgb(0xF2, 0xA9, 0x00)
$edge = [Drawing.Color]::FromArgb(0xD9, 0x8A, 0x00)
$beak = [Drawing.Color]::FromArgb(0xF4, 0x51, 0x1E)
$ink = [Drawing.Color]::FromArgb(0x20, 0x21, 0x24)

# Everything is drawn on a 100x100 grid. Small icons drop the corners and the wing, which turn to mush.
function Draw-Chick([Drawing.Graphics]$g, [double]$size, [bool]$detailed) {
    $state = $g.Save()
    $g.ScaleTransform($size / 100, $size / 100)

    if ($detailed) {
        # The corners of a screenshot selection, framing the duckling.
        $corner = New-Object Drawing.Pen $beak, 5
        $corner.StartCap = $corner.EndCap = [Drawing.Drawing2D.LineCap]::Round
        foreach ($c in @(@(5, 5, 1, 1), @(95, 5, -1, 1), @(5, 95, 1, -1), @(95, 95, -1, -1))) {
            $g.DrawLine($corner, $c[0], $c[1], $c[0] + 16 * $c[2], $c[1])
            $g.DrawLine($corner, $c[0], $c[1], $c[0], $c[1] + 16 * $c[3])
        }
        $corner.Dispose()
        $g.TranslateTransform(14, 15)
        $g.ScaleTransform(0.72, 0.72)
    }

    $body = New-Object Drawing.Drawing2D.GraphicsPath
    $body.AddEllipse(6, 48, 80, 46)
    $body.AddEllipse(34, 8, 48, 48)
    $tail = [Drawing.PointF[]]@(
        (New-Object Drawing.PointF 2, 50), (New-Object Drawing.PointF 26, 56), (New-Object Drawing.PointF 14, 72))
    $body.AddPolygon($tail)
    $tuft = [Drawing.PointF[]]@(
        (New-Object Drawing.PointF 50, 12), (New-Object Drawing.PointF 54, 0), (New-Object Drawing.PointF 60, 10),
        (New-Object Drawing.PointF 67, 1), (New-Object Drawing.PointF 68, 13))
    $body.AddPolygon($tuft)
    $body.FillMode = [Drawing.Drawing2D.FillMode]::Winding

    # Stroke first and fill over it, so only the outer edge of the merged shapes stays visible.
    $outline = New-Object Drawing.Pen $edge, 5
    $outline.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($outline, $body)
    $outline.Dispose()
    $fill = New-Object Drawing.SolidBrush $yellow
    $g.FillPath($fill, $body)
    $fill.Dispose()

    if ($detailed) {
        $wing = New-Object Drawing.SolidBrush $shade
        $g.FillEllipse($wing, 22, 58, 40, 24)
        $wing.Dispose()
    }

    $orange = New-Object Drawing.SolidBrush $beak
    $g.FillEllipse($orange, 72, 28, 26, 11)
    $g.FillEllipse($orange, 74, 35, 19, 9)
    $orange.Dispose()

    $black = New-Object Drawing.SolidBrush $ink
    $g.FillEllipse($black, 60, 20, 11, 11)
    $black.Dispose()
    if ($detailed) {
        $g.FillEllipse([Drawing.Brushes]::White, 66, 22, 3.5, 3.5)
    }
    $body.Dispose()
    $g.Restore($state)
}

# Drawn at four times the size and scaled down, for clean edges at every icon size.
function New-Chick([int]$size, [bool]$detailed) {
    $big = New-Object Drawing.Bitmap ($size * 4), ($size * 4), ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    Draw-Chick $g ($size * 4) $detailed
    $g.Dispose()

    $small = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($big, 0, 0, $size, $size)
    $g.Dispose()
    $big.Dispose()
    $small
}

function Get-PngBytes([Drawing.Bitmap]$image) {
    $stream = New-Object IO.MemoryStream
    $image.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $stream.ToArray()
}

# The classic icon entry: a bottom-up 32-bit bitmap, then an all-zero mask because alpha does the work.
function Get-DibBytes([Drawing.Bitmap]$image) {
    $size = $image.Width
    $rect = New-Object Drawing.Rectangle 0, 0, $size, $size
    $data = $image.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $image.UnlockBits($data)

    $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $stream = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $stream
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($pixels.Length + $maskStride * $size))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $size * 4, $size * 4) }
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    $stream.ToArray()
}

function Write-Icon([string]$path) {
    $entries = foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
        $image = New-Chick $size ($size -ge 40)
        $bytes = if ($size -ge 256) { Get-PngBytes $image } else { Get-DibBytes $image }
        $image.Dispose()
        , @($size, $bytes)
    }

    $stream = [IO.File]::Create($path)
    $w = New-Object IO.BinaryWriter $stream
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dimension = if ($e[0] -ge 256) { 0 } else { $e[0] }
        $w.Write([byte]$dimension); $w.Write([byte]$dimension)
        $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]$e[1].Length); $w.Write([int]$offset)
        $offset += $e[1].Length
    }
    foreach ($e in $entries) { $w.Write([byte[]]$e[1]) }
    $w.Dispose()
}

function Write-Png([string]$path, [int]$size, [bool]$detailed) {
    New-Item (Split-Path $path) -ItemType Directory -Force | Out-Null
    $image = New-Chick $size $detailed
    $image.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $image.Dispose()
}

# Inno Setup wants 24-bit bitmaps, so the duckling goes onto a solid background.
function Write-WizardBitmap([string]$path, [int]$width, [int]$height, [Drawing.Color]$background, [int]$chick) {
    $image = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($image)
    $g.Clear($background)
    $drawn = New-Chick $chick $true
    $g.DrawImage($drawn, [int](($width - $chick) / 2), [int](($height - $chick) / 2), $chick, $chick)
    $g.Dispose()
    $drawn.Dispose()
    $image.Save($path, [Drawing.Imaging.ImageFormat]::Bmp)
    $image.Dispose()
}

# The README's banner. The word "Chick" is marked up the way the app marks a screenshot.
function Write-Banner([string]$path) {
    $width = 1600; $height = 400
    $image = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($image)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $card = New-RoundedRect 0 0 $width $height 28
    $night = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0x1B, 0x1C, 0x20))
    $g.FillPath($night, $card)
    $night.Dispose(); $card.Dispose()

    $chick = New-Chick 300 $true
    $g.DrawImage($chick, 110, 50, 300, 300)
    $chick.Dispose()

    $family = New-Object Drawing.FontFamily 'Segoe UI'
    $bold = [int][Drawing.FontStyle]::Bold
    $first = New-Object Drawing.Drawing2D.GraphicsPath
    $first.AddString('Screen', $family, $bold, 150, (New-Object Drawing.PointF 470, 105), [Drawing.StringFormat]::GenericTypographic)
    $second = New-Object Drawing.Drawing2D.GraphicsPath
    $left = $first.GetBounds().Right + 34
    $second.AddString('Chick', $family, $bold, 150, (New-Object Drawing.PointF $left, 105), [Drawing.StringFormat]::GenericTypographic)
    $g.FillPath([Drawing.Brushes]::White, $first)
    $fill = New-Object Drawing.SolidBrush $yellow
    $g.FillPath($fill, $second)

    $word = $second.GetBounds()
    $frame = New-RoundedRect ($word.X - 18) ($word.Y - 16) ($word.Width + 36) ($word.Height + 32) 12
    $pen = New-Object Drawing.Pen $beak, 6
    $g.DrawPath($pen, $frame)
    $pen.Dispose(); $frame.Dispose()

    $badgeX = $word.X - 21; $badgeY = $word.Y - 16 - 52
    $badge = New-RoundedRect $badgeX $badgeY 92 50 10
    $g.FillPath($fill, $badge)
    $edgePen = New-Object Drawing.Pen $beak, 3
    $g.DrawPath($edgePen, $badge)
    $edgePen.Dispose(); $badge.Dispose()
    $label = New-Object Drawing.Drawing2D.GraphicsPath
    $label.AddString('A1', $family, $bold, 34, (New-Object Drawing.PointF ($badgeX + 22), ($badgeY + 7)), [Drawing.StringFormat]::GenericTypographic)
    $dark = New-Object Drawing.SolidBrush $ink
    $g.FillPath($dark, $label)
    $dark.Dispose(); $label.Dispose(); $fill.Dispose(); $first.Dispose(); $second.Dispose()

    $g.Dispose()
    $image.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $image.Dispose()
}

function New-RoundedRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

Write-Banner (Join-Path $root 'docs\banner.png')
Write-Icon (Join-Path $root 'src\ScreenChick\app.ico')
Write-Png (Join-Path $root 'src\ScreenChick\Assets\logo.png') 64 $false
Write-Png (Join-Path $root 'docs\logo.png') 256 $true
Write-WizardBitmap (Join-Path $PSScriptRoot 'wizard-small.bmp') 58 58 ([Drawing.Color]::White) 54
Write-WizardBitmap (Join-Path $PSScriptRoot 'wizard-large.bmp') 164 314 ([Drawing.Color]::FromArgb(0xFF, 0xF3, 0xC4)) 140
