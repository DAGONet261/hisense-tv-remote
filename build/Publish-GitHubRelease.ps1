<#
.SYNOPSIS
    Publie une Release sur GitHub : commit de version.txt, push, tag v<version> et installeur joint.

.DESCRIPTION
    Appelé automatiquement à la fin d'une compilation Release (src\Directory.Build.targets), après la
    création de l'installeur. Étapes :
      1. vérifications : git et gh présents, gh connecté, branche main, aucune autre modification
         non commitée (le tag doit correspondre exactement au code compilé) ;
      2. commit de version.txt (« Release <version> ») et push de main ;
      3. Release GitHub « v<version> » avec l'installeur et la liste des changements depuis la
         Release précédente ; si elle existe déjà (recompilation de la même version), l'installeur
         est remplacé.
    Aucune étape ne fait échouer la compilation : en cas de problème, un avertissement explique
    pourquoi la Release n'a pas été publiée (l'installeur reste disponible dans dist\).

.PARAMETER Version
    Version publiée (ex. 1.0.0.2).

.PARAMETER Setup
    Chemin de l'installeur à joindre.

.PARAMETER DryRun
    Affiche ce qui serait fait, sans commit, push ni Release.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Setup,
    [switch] $DryRun
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"
$utf8 = New-Object System.Text.UTF8Encoding($false)
# Messages en UTF-8 : lus tels quels par MSBuild (StdOutEncoding=utf-8) dans la fenêtre Sortie de Visual Studio.
try { [Console]::OutputEncoding = $utf8 } catch { }

# Format reconnu par MSBuild/Visual Studio comme un avertissement (liste d'erreurs).
function Write-Warn([string] $message) {
    Write-Host "Publish-GitHubRelease : warning HTR001: Release GitHub $tag non publiée : $message"
}

# Lance git ou gh en lisant leur sortie en UTF-8 (accents des messages de commit intacts).
function Invoke-Tool([string] $exe, [string[]] $arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = ($arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
    $psi.WorkingDirectory = $root
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $utf8
    $psi.StandardErrorEncoding = $utf8
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    $p.WaitForExit()
    [pscustomobject]@{ Code = $p.ExitCode; Out = $out.Result.Trim(); Err = $err.Result.Trim() }
}

# --- 1. Vérifications ----------------------------------------------------------------------------
$git = (Get-Command git -ErrorAction SilentlyContinue).Source
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh -and (Test-Path "$env:ProgramFiles\GitHub CLI\gh.exe")) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }
if (-not $git) { Write-Warn "git est introuvable."; exit 0 }
if (-not $gh) { Write-Warn "GitHub CLI (gh) est introuvable (winget install GitHub.cli)."; exit 0 }
if (-not (Test-Path $Setup)) { Write-Warn "installeur introuvable ($Setup)."; exit 0 }

$branch = (Invoke-Tool $git @('rev-parse', '--abbrev-ref', 'HEAD')).Out
if ($branch -ne 'main') {
    Write-Warn "les Releases se publient depuis main (branche actuelle : $branch)."; exit 0
}
$dirty = (Invoke-Tool $git @('status', '--porcelain', '--untracked-files=no')).Out -split "`n" |
    Where-Object { $_ -and $_ -notmatch 'version\.txt$' }
if ($dirty) {
    Write-Warn ("des modifications ne sont pas commitées ; commitez-les puis relancez la Release. " +
        "Fichiers : " + (($dirty | ForEach-Object { $_.Substring(3) }) -join ', '))
    exit 0
}
if ((Invoke-Tool $gh @('auth', 'status')).Code -ne 0) {
    Write-Warn "GitHub CLI n'est pas connecté (gh auth login)."; exit 0
}

# --- 2. Commit de version.txt et push ------------------------------------------------------------
$versionChanged = (Invoke-Tool $git @('status', '--porcelain', '--', 'version.txt')).Out
if ($DryRun) {
    Write-Host "[essai] commit de version.txt : $([bool]$versionChanged) ; push de main ; Release $tag avec $Setup"
    exit 0
}
if ($versionChanged) {
    $msgFile = Join-Path $env:TEMP "htr-release-$Version.txt"
    [System.IO.File]::WriteAllText($msgFile, "Release $Version`n", $utf8)
    $r = Invoke-Tool $git @('commit', '-q', '-F', $msgFile, '--', 'version.txt')
    Remove-Item $msgFile -ErrorAction SilentlyContinue
    if ($r.Code -ne 0) { Write-Warn "échec du commit de version.txt : $($r.Err)"; exit 0 }
    Write-Host "Commit « Release $Version »."
}
$r = Invoke-Tool $git @('push', '-q', 'origin', 'main')
if ($r.Code -ne 0) {
    Write-Warn "échec du push de main (faites git pull, puis relancez la Release) : $($r.Err)"; exit 0
}

# --- 3. Release GitHub ---------------------------------------------------------------------------
$head = (Invoke-Tool $git @('rev-parse', 'HEAD')).Out
$prev = Invoke-Tool $git @('describe', '--tags', '--abbrev=0', '--match', 'v*', 'HEAD^')
$range = if ($prev.Code -eq 0 -and $prev.Out) { @("$($prev.Out)..HEAD") } else { @('-n', '30', 'HEAD') }
$log = (Invoke-Tool $git (@('log', '--no-merges', '--pretty=format:- %s') + $range)).Out -split "`n" |
    Where-Object { $_ -and $_ -notmatch '^- (Release \d|Réservation de zone)' }
$changes = if ($log) { $log -join "`n" } else { '- Recompilation, sans changement de code.' }
$since = if ($prev.Code -eq 0 -and $prev.Out) { " (depuis $($prev.Out))" } else { '' }
$setupName = Split-Path $Setup -Leaf
$notes = @"
Installeur Windows 64 bits de **VIDAA - Hisense $Version**.

Téléchargez ``$setupName`` ci-dessous et lancez-le : une version déjà installée est mise à jour, vos réglages et téléviseurs appairés sont conservés. Le certificat client VIDAA n'est pas inclus : il s'importe au premier lancement (« TV et réglages »).

## Changements$since

$changes
"@
$notesFile = Join-Path $env:TEMP "htr-notes-$Version.md"
[System.IO.File]::WriteAllText($notesFile, $notes, $utf8)

if ((Invoke-Tool $gh @('release', 'view', $tag)).Code -eq 0) {
    $r = Invoke-Tool $gh @('release', 'upload', $tag, $Setup, '--clobber')
    $what = "installeur remplacé dans la Release $tag existante"
} else {
    $r = Invoke-Tool $gh @('release', 'create', $tag, $Setup, '--target', $head,
        '--title', "VIDAA - Hisense $Version", '--notes-file', $notesFile)
    $what = "Release $tag publiée"
}
Remove-Item $notesFile -ErrorAction SilentlyContinue
if ($r.Code -ne 0) { Write-Warn "échec de GitHub CLI : $($r.Err)"; exit 0 }
[void](Invoke-Tool $git @('fetch', '-q', '--tags', 'origin'))
Write-Host "GitHub : $what. $($r.Out)"
