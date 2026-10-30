param([string]$OutIco, [string]$OutPreview)
# Draws DeskPicker's icon, a cabinet on a round badge with the middle drawer pulled out,
# and writes it as a multi-size .ico (plus an optional 256 px PNG preview).
#   powershell -File tools\make-deskpicker-icon.ps1 deskpicker.ico preview.png
Add-Type -AssemblyName System.Drawing

function Rgb([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function RoundRect($g, $brush, [double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min(2 * $r, [Math]::Min($w, $h))
    if ($d -le 0.5) { $g.FillRectangle($brush, [single]$x, [single]$y, [single]$w, [single]$h); return }
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    $g.FillPath($brush, $p)
    $p.Dispose()
}

# Drawn fresh at each size (on a 200-unit grid, like the design canvas) so the small
# frames stay crisp; details that would only blur are left out of the smallest ones.
function New-DrawerBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $S / 200.0
    $b = { param($hex) New-Object System.Drawing.SolidBrush (Rgb $hex) }

    $badge = & $b '#1b2433'; $cabinet = & $b '#9aa3b2'; $slot = & $b '#2f3744'
    $top = & $b '#5b7393'; $bottom = & $b '#7a6a8f'; $pulled = & $b '#4fc06a'; $grip = & $b '#06150b'

    $g.FillEllipse($badge, [single](4*$k), [single](4*$k), [single](192*$k), [single](192*$k))
    RoundRect $g $cabinet (52*$k) (42*$k) (96*$k) (118*$k) (8*$k)
    if ($S -ge 48) {
        RoundRect $g $cabinet (58*$k) (158*$k) (10*$k) (11*$k) (2*$k)
        RoundRect $g $cabinet (132*$k) (158*$k) (10*$k) (11*$k) (2*$k)
    }
    # closed drawers, muted, with recessed handles
    RoundRect $g $top (58*$k) (50*$k) (84*$k) (26*$k) (4*$k)
    RoundRect $g $bottom (58*$k) (128*$k) (84*$k) (26*$k) (4*$k)
    if ($S -ge 24) {
        $hh = [Math]::Max(1.5, 6*$k)
        RoundRect $g $badge (88*$k) (63*$k - $hh/2) (24*$k) $hh ($hh/2)
        RoundRect $g $badge (88*$k) (141*$k - $hh/2) (24*$k) $hh ($hh/2)
    }
    # the pulled-out drawer: the picked profile
    RoundRect $g $slot (58*$k) (84*$k) (84*$k) (30*$k) (3*$k)
    RoundRect $g $pulled (44*$k) (90*$k) (112*$k) (32*$k) (5*$k)
    $gh = [Math]::Max(1.5, 6*$k)
    RoundRect $g $grip (86*$k) (106*$k - $gh/2) (28*$k) $gh ($gh/2)

    $g.Dispose()
    return $bmp
}

$frames = @()
foreach ($s in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
    $bm = New-DrawerBitmap $s
    if ($s -eq 256 -and $OutPreview) { $bm.Save($OutPreview, [System.Drawing.Imaging.ImageFormat]::Png) }
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 128) {
        $bm.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # classic DIB frame (32bpp BGRA bottom-up + 1bpp AND mask): loads everywhere, incl. .NET's Icon class
        $w = New-Object System.IO.BinaryWriter($ms)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]0); $w.Write([int]($s * $s * 4 + $maskStride * $s)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) {
            $c = $bm.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
        for ($y = $s - 1; $y -ge 0; $y--) {
            $row = New-Object byte[] $maskStride
            for ($x = 0; $x -lt $s; $x++) { if ($bm.GetPixel($x, $y).A -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) } }
            $w.Write($row)
        }
        $w.Flush()
    }
    $frames += ,@($s, $ms.ToArray())
    $bm.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"wrote $OutIco ($($out.Length) bytes, $($frames.Count) sizes)"
