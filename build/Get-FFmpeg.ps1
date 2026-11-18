<#
.SYNOPSIS
    Récupère FFmpeg (LGPL) pour l'application : conversion des vidéos TS, AVI… en MP4 avant le
    partage vers la TV. Copié dans third_party\ffmpeg\ (non versionné), puis à côté de l'exe.

.DESCRIPTION
    Version figée, empreinte vérifiée. Source : la Release « outils-ffmpeg-9.0.2 » du dépôt (copie de la
    build BtbN, dont les builds datées disparaissent au bout de quelques semaines) via GitHub CLI ; à
    défaut, l'adresse d'origine chez BtbN. Seuls ffmpeg.exe, ffprobe.exe, leurs DLL et la licence sont
    gardés (pas ffplay.exe). Ne fait rien si la bonne version est déjà là.
    Appelé par la compilation Release (si absent) et par le workflow GitHub.
#>
param(
    [string] $Destination
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path $root 'third_party\ffmpeg' }

$version = 'n9.0.2-57-gd838f9f9ae-lgpl-shared'
$zipName = 'ffmpeg-9.0.2-lgpl-shared-win64.zip'
$sha256 = '8388f846a8b56a9316ad8f24d9eedf186097a59555816b7b222de392611b0279'
$mirrorTag = 'outils-ffmpeg-9.0.2'
$originUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-11-17-12-40/ffmpeg-n9.0.2-57-gd838f9f9ae-win64-lgpl-shared-9.0.zip'

$stamp = Join-Path $Destination 'VERSION.txt'
if ((Test-Path (Join-Path $Destination 'ffmpeg.exe')) -and (Test-Path $stamp) -and
    ((Get-Content $stamp -TotalCount 1) -eq $version)) {
    Write-Host "FFmpeg $version déjà présent : $Destination"
    exit 0
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("htr-ffmpeg-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $zip = Join-Path $work $zipName
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        Push-Location $root
        try { & $gh.Source release download $mirrorTag --pattern $zipName --dir $work --clobber 2>$null } finally { Pop-Location }
    }
    if (-not (Test-Path $zip)) {
        Write-Host "Copie du dépôt indisponible : téléchargement depuis BtbN…"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest $originUrl -OutFile $zip -UseBasicParsing
    }
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $sha256) { throw "Empreinte SHA-256 inattendue pour FFmpeg : $hash" }

    Expand-Archive $zip -DestinationPath $work -Force
    $bin = Get-ChildItem $work -Directory -Recurse -Filter bin | Select-Object -First 1
    if (-not $bin) { throw "Archive FFmpeg inattendue (dossier bin absent)." }
    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    New-Item -ItemType Directory -Force $Destination | Out-Null
    Get-ChildItem $bin.FullName -File | Where-Object { $_.Name -ne 'ffplay.exe' } |
        Copy-Item -Destination $Destination
    $license = Get-ChildItem $work -File -Recurse -Filter LICENSE.txt | Select-Object -First 1
    if ($license) { Copy-Item $license.FullName (Join-Path $Destination 'LICENSE.txt') }
    Set-Content -Path $stamp -Value $version -Encoding ASCII
    $mb = [Math]::Round(((Get-ChildItem $Destination -File | Measure-Object Length -Sum).Sum) / 1MB)
    Write-Host "FFmpeg $version installé : $Destination ($mb Mo)"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
