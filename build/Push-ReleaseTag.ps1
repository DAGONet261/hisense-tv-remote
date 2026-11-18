<#
.SYNOPSIS
    Déclenche la Release GitHub : commit de version.txt, tag v<version>, push de main et du tag.

.DESCRIPTION
    Appelé automatiquement à la fin d'une compilation Release (src\Directory.Build.targets). Le tag
    poussé déclenche le workflow GitHub (.github/workflows/release.yml), qui recompile, teste, crée
    l'installeur et publie la Release. L'installeur local (dist\) ne sert qu'aux essais.
    Étapes :
      1. vérifications : git présent, branche main, aucune autre modification non commitée que
         version.txt (le tag doit correspondre exactement au code compilé) ;
      2. commit de version.txt (« Release <version> ») et tag annoté v<version> ;
      3. push de main, puis du tag.
    Ne fait jamais échouer la compilation : en cas de problème, un avertissement (HTR001) explique
    pourquoi la Release n'a pas été déclenchée.

.PARAMETER Version
    Version de la Release (ex. 1.0.0.2).

.PARAMETER DryRun
    Affiche ce qui serait fait, sans commit, tag ni push.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
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
    Write-Host "Push-ReleaseTag : warning HTR001: Release GitHub $tag non déclenchée : $message"
}

function Invoke-Git([string[]] $arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
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
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Write-Warn "git est introuvable."; exit 0 }
$branch = (Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')).Out
if ($branch -ne 'main') {
    Write-Warn "les Releases partent de main (branche actuelle : $branch) ; fusionnez d'abord."; exit 0
}
$dirty = (Invoke-Git @('status', '--porcelain', '--untracked-files=no')).Out -split "`n" |
    ForEach-Object { $_.TrimEnd("`r") } | Where-Object { $_ -and $_ -notmatch 'version\.txt$' }
if ($dirty) {
    Write-Warn ("des modifications ne sont pas commitées ; commitez-les puis relancez la Release. " +
        "Fichiers : " + (($dirty | ForEach-Object { $_.Substring(3) }) -join ', '))
    exit 0
}
$versionChanged = [bool](Invoke-Git @('status', '--porcelain', '--', 'version.txt')).Out
$localTag = Invoke-Git @('rev-parse', '-q', '--verify', "refs/tags/$tag^{commit}")
if ($DryRun) {
    Write-Host "[essai] commit de version.txt : $versionChanged ; tag $tag existant : $($localTag.Code -eq 0) ; push de main et du tag"
    exit 0
}

# --- 2. Commit de version.txt et tag -------------------------------------------------------------
if ($versionChanged) {
    $msgFile = Join-Path $env:TEMP "htr-release-$Version.txt"
    [System.IO.File]::WriteAllText($msgFile, "Release $Version`n", $utf8)
    $r = Invoke-Git @('commit', '-q', '-F', $msgFile, '--', 'version.txt')
    Remove-Item $msgFile -ErrorAction SilentlyContinue
    if ($r.Code -ne 0) { Write-Warn "échec du commit de version.txt : $($r.Err)"; exit 0 }
    Write-Host "Commit « Release $Version »."
}
$head = (Invoke-Git @('rev-parse', 'HEAD')).Out
if ($localTag.Code -eq 0) {
    if ($localTag.Out -ne $head) {
        Write-Warn "le tag $tag existe déjà sur un autre commit ; prenez une nouvelle version (Release sans -NoBump)."
        exit 0
    }
} else {
    $r = Invoke-Git @('tag', '-a', $tag, '-m', "VIDAA - Hisense $Version")
    if ($r.Code -ne 0) { Write-Warn "échec de la création du tag : $($r.Err)"; exit 0 }
}

# --- 3. Push -------------------------------------------------------------------------------------
$r = Invoke-Git @('push', '-q', 'origin', 'main')
if ($r.Code -ne 0) {
    Write-Warn "échec du push de main (faites git pull, puis relancez la Release) : $($r.Err)"; exit 0
}
$remoteTag = (Invoke-Git @('ls-remote', '--tags', 'origin', "refs/tags/$tag")).Out
$r = Invoke-Git @('push', '-q', 'origin', $tag)
if ($r.Code -ne 0) { Write-Warn "échec du push du tag : $($r.Err)"; exit 0 }

$url = (Invoke-Git @('remote', 'get-url', 'origin')).Out -replace '\.git$', '' -replace '^git@github\.com:', 'https://github.com/'
if ($remoteTag) {
    Write-Host ("Le tag $tag était déjà sur GitHub : pas de nouvelle compilation. Pour reconstruire l'installeur : " +
        "$url/actions/workflows/release.yml → « Run workflow », tag $tag.")
} else {
    Write-Host "Tag $tag envoyé : GitHub compile, teste et publie la Release ($url/actions)."
}
