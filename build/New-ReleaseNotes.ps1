<#
.SYNOPSIS
    Écrit les notes (en français) d'une Release GitHub : mode d'emploi + commits depuis la Release précédente.

.DESCRIPTION
    Utilisé par le workflow GitHub (.github/workflows/release.yml).
    - « Nouveautés » : la section « ## <version> » de CHANGELOG.md (écrite pour les utilisateurs), si
      elle existe.
    - « Détail des changements » : les commits depuis la Release précédente (dernier tag « v* » avant
      HEAD ; sans tag précédent, les 30 derniers), sans réservations de zone ni commits « Release x ».
    Fichier écrit en UTF-8 sans BOM.

.EXAMPLE
    pwsh build/New-ReleaseNotes.ps1 -Version 1.0.0.2 -Out notes.md
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Out
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)

# Sortie de git lue en UTF-8 (accents des messages de commit), quelle que soit la console.
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
    $p = [System.Diagnostics.Process]::Start($psi)
    $text = $p.StandardOutput.ReadToEnd()
    [void]$p.StandardError.ReadToEnd()
    $p.WaitForExit()
    [pscustomobject]@{ Code = $p.ExitCode; Out = $text.Trim() }
}

$prev = Invoke-Git @('describe', '--tags', '--abbrev=0', '--match', 'v*', 'HEAD^')
$hasPrev = $prev.Code -eq 0 -and $prev.Out
$range = if ($hasPrev) { @("$($prev.Out)..HEAD") } else { @('-n', '30', 'HEAD') }
$log = (Invoke-Git (@('log', '--no-merges', '--pretty=format:- %s') + $range)).Out -split "`n" |
    ForEach-Object { $_.TrimEnd("`r") } |
    Where-Object { $_ -and $_ -notmatch '^- (Release \d|Réservation de zone)' }
$changes = if ($log) { $log -join "`n" } else { '- Recompilation, sans changement de code.' }
$since = if ($hasPrev) { " (depuis $($prev.Out))" } else { '' }

# Section « ## <version> » de CHANGELOG.md, jusqu'à la section suivante de même niveau.
$news = $null
$changelog = Join-Path $root 'CHANGELOG.md'
if (Test-Path $changelog) {
    $lines = [System.IO.File]::ReadAllLines($changelog, $utf8)
    $start = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l.Trim() -eq "## $Version" })
    if ($start -ge 0) {
        $section = New-Object System.Collections.Generic.List[string]
        for ($i = $start + 1; $i -lt $lines.Length -and $lines[$i] -notmatch '^## '; $i++) { $section.Add($lines[$i]) }
        $text = ($section -join "`n").Trim()
        if ($text) { $news = $text }
    }
}
$newsBlock = if ($news) { "## Nouveautés`n`n$news`n`n" } else { '' }
$changesTitle = if ($news) { "## Détail des changements$since" } else { "## Changements$since" }

$notes = @"
Installeur Windows 64 bits de **VIDAA - Hisense $Version**.

Téléchargez ``VIDAA-Hisense-Setup-$Version.exe`` ci-dessous et lancez-le : une version déjà installée est mise à jour, vos réglages et téléviseurs appairés sont conservés. Le certificat client VIDAA n'est pas inclus : il s'importe au premier lancement (« TV et réglages »).

$newsBlock$changesTitle

$changes

---
Compilé et testé par GitHub Actions à partir du tag ``v$Version``.
"@
[System.IO.File]::WriteAllText($Out, $notes.Replace("`r`n", "`n"), $utf8)
Write-Host "Notes de la Release : $Out"
