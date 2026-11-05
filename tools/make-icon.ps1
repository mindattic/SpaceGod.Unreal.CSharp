<#
.SYNOPSIS
    Builds assets/icon.png and assets/icon.ico (US-T75): assets/logo.png (the ship) on a transparent background, the
    engine's logo in the lower left and the language ("C++" / "C#") in the lower right. The output is committed; this is an
    optional one-time tool (it reads the engine logo from the installed Unreal Engine 5.8 or Unity editor).
.PARAMETER Engine   Unreal | Unity
.PARAMETER Lang     The lower-right text, e.g. "C++" or "C#".
.PARAMETER Logo     The source image (default assets/logo.png beside this script's repo).
.PARAMETER OutDir   Where icon.png / icon.ico go (default: the logo's folder).
.PARAMETER EngineImage  An image to use as the engine logo instead of extracting it from the installed engine.
#>
param(
    [Parameter(Mandatory)][ValidateSet('Unreal', 'Unity')][string]$Engine,
    [Parameter(Mandatory)][string]$Lang,
    [string]$Logo,
    [string]$OutDir,
    [string]$EngineImage
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('IconX.Native' -as [type])) {
Add-Type -Namespace IconX -Name Native -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint n, uint flags);
[DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
'@
}

$repo = Split-Path -Parent $PSScriptRoot
if (-not $Logo) { $Logo = @('assets\logo.png', 'Assets\logo.png') | % { Join-Path $repo $_ } | ? { Test-Path $_ } | Select -First 1 }
if (-not $OutDir) { $OutDir = Split-Path -Parent $Logo }

function Get-ExeIcon([string]$file, [int]$size) {
    $h = New-Object IntPtr[] 1; $ids = New-Object uint32[] 1
    $n = [IconX.Native]::PrivateExtractIcons($file, 0, $size, $size, $h, $ids, 1, 0)
    if ($n -lt 1 -or $h[0] -eq [IntPtr]::Zero) { throw "No icon in $file" }
    $bmp = [Drawing.Icon]::FromHandle($h[0]).ToBitmap(); [void][IconX.Native]::DestroyIcon($h[0]); $bmp
}

function Get-EngineLogo {
    if ($EngineImage) { return [Drawing.Bitmap]::new($EngineImage) }
    if ($Engine -eq 'Unreal') {
        $roots = @($env:UE_ROOT) + (Get-PSDrive -PSProvider FileSystem | % { "$($_.Root)Games\UE_5.8" }) + 'C:\Program Files\Epic Games\UE_5.8'
        $ico = $roots | ? { $_ } | % { Join-Path $_ 'Engine\Build\Windows\Resources\Default.ico' } | ? { Test-Path $_ } | Select -First 1
        if (-not $ico) { throw 'Unreal Engine 5.8 not found (Default.ico); pass -EngineImage.' }
        return Get-ExeIcon $ico 256
    }
    $unity = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe' -ErrorAction SilentlyContinue | Sort FullName -Desc | Select -First 1
    if (-not $unity) { throw 'Unity editor not found; pass -EngineImage.' }
    Get-ExeIcon $unity.FullName 256
}

# The ship, its outer white made transparent: flood fill from the border over near-white pixels.
function Get-Ship([string]$path) {
    $src = [Drawing.Bitmap]::new($path)
    $w = $src.Width; $h = $src.Height
    $bmp = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp); $g.DrawImage($src, 0, 0, $w, $h); $g.Dispose(); $src.Dispose()
    $rect = New-Object Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, 'ReadWrite', [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object int[] ($w * $h)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
    $seen = New-Object bool[] ($w * $h)
    $q = New-Object 'System.Collections.Generic.Queue[int]'
    for ($x = 0; $x -lt $w; $x++) { $q.Enqueue($x); $q.Enqueue(($h - 1) * $w + $x) }
    for ($y = 0; $y -lt $h; $y++) { $q.Enqueue($y * $w); $q.Enqueue($y * $w + $w - 1) }
    while ($q.Count) {
        $i = $q.Dequeue(); if ($seen[$i]) { continue }; $seen[$i] = $true
        $c = $px[$i]; $r = ($c -shr 16) -band 255; $gg = ($c -shr 8) -band 255; $b = $c -band 255
        $lum = [Math]::Min($r, [Math]::Min($gg, $b))
        if ($lum -lt 140) { continue }                       # the outline: stop
        # Near-white -> clear; the anti-aliased edge keeps an alpha from its darkness.
        $a = [int][Math]::Round((255 - $lum) * 255 / 115.0); if ($a -lt 0) { $a = 0 }; if ($a -gt 255) { $a = 255 }
        $px[$i] = ($a -shl 24) -bor 0x000000
        if ($lum -lt 235) { continue }                        # an edge pixel: don't spread past it
        $x = $i % $w; $y = [int][Math]::Floor($i / $w)
        if ($x -gt 0) { $q.Enqueue($i - 1) }; if ($x -lt $w - 1) { $q.Enqueue($i + 1) }
        if ($y -gt 0) { $q.Enqueue($i - $w) }; if ($y -lt $h - 1) { $q.Enqueue($i + $w) }
    }
    [Runtime.InteropServices.Marshal]::Copy($px, 0, $data.Scan0, $px.Length)
    $bmp.UnlockBits($data); $bmp
}

function New-Canvas([int]$s) {
    $b = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([Drawing.Color]::Transparent)
    $b; $g
}

$ship = Get-Ship $Logo
$engineLogo = Get-EngineLogo

# The 256 px master.
$S = 256
$b, $g = New-Canvas $S
$g.DrawImage($ship, 8, 0, 240, 240)
# Engine badge, lower left: a white disc so it reads over the ship, the logo inside.
$bd = 104; $bx = 0; $by = $S - $bd
$g.FillEllipse([Drawing.Brushes]::White, $bx, $by, $bd, $bd)
$g.DrawImage($engineLogo, $bx + 6, $by + 6, $bd - 12, $bd - 12)
# Language, lower right: bold black text with a white outline.
$path = New-Object Drawing.Drawing2D.GraphicsPath
$fam = New-Object Drawing.FontFamily 'Segoe UI'
$em = 92
$fmt = [Drawing.StringFormat]::GenericTypographic
$path.AddString($Lang, $fam, [int][Drawing.FontStyle]::Bold, $em, (New-Object Drawing.PointF 0, 0), $fmt)
$bounds = $path.GetBounds()
$maxW = 140.0; $scale = [Math]::Min(1.0, $maxW / $bounds.Width)
$m = New-Object Drawing.Drawing2D.Matrix
$m.Translate($S - 4 - $bounds.Width * $scale, $S - 4 - $bounds.Height * $scale)
$m.Scale($scale, $scale); $m.Translate(-$bounds.X, -$bounds.Y)
$path.Transform($m)
$pen = New-Object Drawing.Pen ([Drawing.Color]::White), 14; $pen.LineJoin = 'Round'
$g.DrawPath($pen, $path)
$g.FillPath([Drawing.Brushes]::Black, $path)
$g.Dispose()

New-Item -ItemType Directory -Force $OutDir | Out-Null
$pngPath = Join-Path $OutDir 'icon.png'
$b.Save($pngPath, [Drawing.Imaging.ImageFormat]::Png)

# icon.ico: 256 as a PNG frame, the smaller sizes as 32-bit DIBs (what every icon loader reads).
$sizes = 256, 128, 64, 48, 32, 24, 16
$frames = foreach ($s in $sizes) {
    $fb, $fg = New-Canvas $s
    $fg.DrawImage($b, 0, 0, $s, $s); $fg.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($s -ge 256) {
        $fb.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    } else {
        $bw = New-Object IO.BinaryWriter $ms
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]0); $bw.Write([uint32]($s * $s * 4 + $maskRow * $s)); $bw.Write([int32]0); $bw.Write([int32]0)
        $bw.Write([uint32]0); $bw.Write([uint32]0)
        $data = $fb.LockBits((New-Object Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $row = New-Object byte[] ($s * 4)
        for ($y = $s - 1; $y -ge 0; $y--) {               # bottom-up BGRA
            [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
            $bw.Write($row)
        }
        $fb.UnlockBits($data)
        $bw.Write((New-Object byte[] ($maskRow * $s)))   # AND mask: all zero, alpha decides
        $bw.Flush()
    }
    $fb.Dispose()
    , $ms.ToArray()
}
$icoPath = Join-Path $OutDir 'icon.ico'
$fs = [IO.File]::Create($icoPath); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $frames[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$d.Length); $w.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $frames) { $w.Write($d) }
$w.Dispose()
$b.Dispose(); $ship.Dispose(); $engineLogo.Dispose()
Write-Host "  $pngPath"
Write-Host "  $icoPath"
