<#
.SYNOPSIS
    Génère l'icône de l'application (.ico multi-tailles) et les images de l'installeur (.bmp)
    à partir de l'icône officielle VIDAA et du logo vectoriel de l'app VIDAA.

.DESCRIPTION
    L'icône officielle (app_icon.png, 180 px) est trop petite pour un .ico net en 256 px : on en
    reprend seulement la couleur de fond et la place du logo, puis on redessine le logo
    (vidaa_logo.png, blanc, 284 px de large) en noir à chaque taille. Les deux images viennent de
    l'APK de l'app VIDAA (dossier sources/, jamais versionné) ; les fichiers générés, eux, le sont.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\New-AppIcon.ps1 `
        -IconPng sources\VIDAA-split\icone\app_icon.png -Wordmark sources\VIDAA\res\mipmap-xhdpi-v4\vidaa_logo.png
#>
param(
    [Parameter(Mandatory)] [string] $IconPng,
    [Parameter(Mandatory)] [string] $Wordmark,
    [string] $Root
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }      # Windows PowerShell 5.1 : pas de $PSScriptRoot dans param()
Add-Type -AssemblyName PresentationCore, WindowsBase

function Read-Bitmap([string] $path) {
    $bmp = New-Object System.Windows.Media.Imaging.BitmapImage
    $bmp.BeginInit()
    $bmp.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $bmp.UriSource = New-Object System.Uri((Resolve-Path $path).Path)
    $bmp.EndInit()
    $bmp.Freeze()
    New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($bmp, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
}

function Get-Pixels($bmp) {
    $stride = $bmp.PixelWidth * 4
    $px = New-Object byte[] ($stride * $bmp.PixelHeight)
    $bmp.CopyPixels($px, $stride, 0)
    , $px
}

# --- 1. Couleur de fond et place du logo dans l'icône officielle -------------------------------
$icon = Read-Bitmap $IconPng
$px = Get-Pixels $icon
$w = $icon.PixelWidth; $h = $icon.PixelHeight
$bg = [System.Windows.Media.Color]::FromRgb($px[2], $px[1], $px[0])           # pixel (0,0)
$minX = $w; $maxX = -1; $minY = $h; $maxY = -1
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $i = ($y * $w + $x) * 4
        if (($px[$i] + $px[$i + 1] + $px[$i + 2]) -lt 200) {                     # pixel sombre = logo
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$logoWidthRatio = ($maxX - $minX + 1) / $w
$logoCenterY = (($minY + $maxY + 1) / 2) / $h
Write-Host ("Fond #{0:X2}{1:X2}{2:X2}, logo {3:P0} de la largeur, centré à {4:P0} de la hauteur" -f `
    $bg.R, $bg.G, $bg.B, $logoWidthRatio, $logoCenterY)

# --- 2. Logo : seul son canal alpha sert (on le peint en noir) --------------------------------
$mark = Read-Bitmap $Wordmark
$markPx = Get-Pixels $mark
$mx0 = $mark.PixelWidth; $mx1 = -1; $my0 = $mark.PixelHeight; $my1 = -1
for ($y = 0; $y -lt $mark.PixelHeight; $y++) {
    for ($x = 0; $x -lt $mark.PixelWidth; $x++) {
        if ($markPx[($y * $mark.PixelWidth + $x) * 4 + 3] -gt 16) {
            if ($x -lt $mx0) { $mx0 = $x }; if ($x -gt $mx1) { $mx1 = $x }
            if ($y -lt $my0) { $my0 = $y }; if ($y -gt $my1) { $my1 = $y }
        }
    }
}
$markCrop = New-Object System.Windows.Media.Imaging.CroppedBitmap($mark,
    (New-Object System.Windows.Int32Rect($mx0, $my0, ($mx1 - $mx0 + 1), ($my1 - $my0 + 1))))
$markCrop.Freeze()
$markAspect = $markCrop.PixelHeight / $markCrop.PixelWidth

$bgBrush = New-Object System.Windows.Media.SolidColorBrush($bg); $bgBrush.Freeze()
$inkBrush = [System.Windows.Media.Brushes]::Black

function New-Canvas([int] $width, [int] $height, [double] $logoWidth, [double] $logoCenterY, [string] $caption) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRectangle($bgBrush, $null, (New-Object System.Windows.Rect(0, 0, $width, $height)))
    $lw = $logoWidth; $lh = $lw * $markAspect
    $rect = New-Object System.Windows.Rect((($width - $lw) / 2), ($logoCenterY - $lh / 2), $lw, $lh)
    $mask = New-Object System.Windows.Media.ImageBrush($markCrop)
    $dc.PushOpacityMask($mask)
    $dc.DrawRectangle($inkBrush, $null, $rect)
    $dc.Pop()
    if ($caption) {
        $tf = New-Object System.Windows.Media.Typeface('Segoe UI Semibold')
        $ft = New-Object System.Windows.Media.FormattedText($caption,
            [System.Globalization.CultureInfo]::GetCultureInfo('fr-FR'),
            [System.Windows.FlowDirection]::LeftToRight, $tf, ($width * 0.085), $inkBrush, 1.0)
        $ft.TextAlignment = [System.Windows.TextAlignment]::Center
        $dc.DrawText($ft, (New-Object System.Windows.Point(($width / 2), ($logoCenterY + $lh / 2 + $width * 0.06))))
    }
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($width, $height, 96, 96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $rtb
}

function Save-Image($bitmap, [string] $path, [string] $kind) {
    $enc = if ($kind -eq 'bmp') { New-Object System.Windows.Media.Imaging.BmpBitmapEncoder }
           else { New-Object System.Windows.Media.Imaging.PngBitmapEncoder }
    $src = $bitmap
    if ($kind -eq 'bmp') {                                                        # BMP 24 bits, sans alpha
        $src = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($bitmap,
            [System.Windows.Media.PixelFormats]::Bgr24, $null, 0)
    }
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($src))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    if ($path) { [System.IO.File]::WriteAllBytes($path, $ms.ToArray()) }
    , $ms.ToArray()
}

# --- 3. Icône .ico (entrées PNG, 16 à 256 px) -------------------------------------------------
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$entries = foreach ($s in $sizes) {
    # Aux petites tailles, le logo occupe un peu plus de place pour rester lisible.
    $ratio = if ($s -le 24) { 0.92 } elseif ($s -le 48) { 0.82 } else { $logoWidthRatio }
    , (Save-Image (New-Canvas $s $s ($s * $ratio) ($s * $logoCenterY) $null) $null 'png')
}
$icoPath = Join-Path $Root 'src\HisenseTvRemote\Resources\VidaaHisense.ico'
New-Item -ItemType Directory -Force (Split-Path $icoPath) | Out-Null
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($k = 0; $k -lt $sizes.Count; $k++) {
    $s = $sizes[$k]; $data = $entries[$k]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $entries) { $bw.Write($data) }
$bw.Close()
Write-Host "Icône : $icoPath"

# --- 4. Images de l'assistant d'installation (100 % à 250 %) ----------------------------------
$imgDir = Join-Path $Root 'installer\images'
New-Item -ItemType Directory -Force $imgDir | Out-Null
foreach ($scale in 100, 125, 150, 200, 250) {
    $s = [int][Math]::Round(55 * $scale / 100)
    [void](Save-Image (New-Canvas $s $s ($s * $logoWidthRatio) ($s * $logoCenterY) $null) `
        (Join-Path $imgDir "WizardSmallImage-$scale.bmp") 'bmp')
    $iw = [int][Math]::Round(164 * $scale / 100); $ih = [int][Math]::Round(314 * $scale / 100)
    [void](Save-Image (New-Canvas $iw $ih ($iw * 0.72) ($ih * 0.42) 'Télécommande') `
        (Join-Path $imgDir "WizardImage-$scale.bmp") 'bmp')
}
Write-Host "Images de l'assistant : $imgDir"
