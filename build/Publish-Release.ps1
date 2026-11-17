<#
.SYNOPSIS
    Compile « VIDAA - Hisense » en Release et produit l'installeur, comme le fait Visual Studio.

.DESCRIPTION
    Équivalent en ligne de commande d'une compilation Release dans Visual Studio 2026 :
    version suivante (version.txt), compilation, publication autonome dans dist\publish\,
    puis installeur Inno Setup dans dist\, à la même version. Voir contexte/publication.md.

.PARAMETER NoBump
    Garde la version actuelle de version.txt (recompile la même version).

.PARAMETER SetVersion
    Fixe la version (ex. 1.1.0.0), puis compile cette version telle quelle.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\Publish-Release.ps1
    powershell -ExecutionPolicy Bypass -File build\Publish-Release.ps1 -NoBump
    powershell -ExecutionPolicy Bypass -File build\Publish-Release.ps1 -SetVersion 1.1.0.0
#>
param(
    [switch] $NoBump,
    [string] $SetVersion
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $root 'version.txt'

if ($SetVersion) {
    [void][Version]::Parse($SetVersion)                                     # refuse une version mal formée
    [System.IO.File]::WriteAllText($versionFile, "$SetVersion`r`n")
    $NoBump = $true
}

$bump = if ($NoBump) { 'false' } else { 'true' }
& dotnet build (Join-Path $root 'src\HisenseTvRemote\HisenseTvRemote.csproj') -c Release -nologo -v minimal "-p:HtrBump=$bump"
if ($LASTEXITCODE -ne 0) { throw "La compilation Release a échoué (code $LASTEXITCODE)." }

$version = (Get-Content $versionFile -TotalCount 1).Trim()
$setup = Join-Path $root "dist\VIDAA-Hisense-Setup-$version.exe"
if (Test-Path $setup) { Write-Host "Installeur prêt : $setup" -ForegroundColor Green }
else { Write-Warning "Application publiée dans dist\publish, mais installeur absent (Inno Setup installé ?)." }
