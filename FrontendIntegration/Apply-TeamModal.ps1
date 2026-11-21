param(
    [string]$FrontendRoot = 'D:\AIEmailer\Ai-Emailer-Frontend\Ai-Emailer-Frontend\app',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$targetRoot = (Resolve-Path -LiteralPath $FrontendRoot).Path
$packageRoot = Join-Path $PSScriptRoot 'TeamModal'
# Assign first: Windows PowerShell 5.1 emits the JSON array as one pipeline
# object, so wrapping that pipeline in @() would create a nested array.
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'hashes.json') -Raw | ConvertFrom-Json
$manifest = @($manifest)
$allowed = @('js/settings.js', 'js/permissions.js')
if ($manifest.Count -ne 2 -or @($manifest.Path | Select-Object -Unique).Count -ne 2) { throw 'Invalid team patch manifest.' }
$pending = @()
foreach ($entry in $manifest) {
    if ($entry.Path -notin $allowed) { throw 'Unexpected patch file.' }
    $target = Join-Path $targetRoot $entry.Path
    $source = Join-Path (Join-Path $packageRoot 'app') $entry.Path
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.PatchedHash) { throw "Patch integrity check failed: $($entry.Path)" }
    $current = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($current -eq $entry.PatchedHash) { continue }
    if ($current -ne $entry.OriginalHash) { throw "Frontend changed since review: $($entry.Path). No files changed. Review before applying." }
    $pending += $entry
}
if ($CheckOnly) { Write-Output "Checks passed. $($pending.Count) file(s) need updating. No files changed."; return }
if (!$pending.Count) { Write-Output 'Team modal fix is already applied.'; return }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$backup = Join-Path $PSScriptRoot "..\backups\team-modal-$stamp"
foreach ($entry in $pending) {
    $saved = Join-Path $backup $entry.Path
    New-Item -ItemType Directory -Path (Split-Path $saved) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $targetRoot $entry.Path) -Destination $saved
}
$attempted = @()
try {
    foreach ($entry in $pending) {
        $target = Join-Path $targetRoot $entry.Path
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.OriginalHash) { throw "File changed during application: $($entry.Path)" }
        $attempted += $entry
        Copy-Item -LiteralPath (Join-Path (Join-Path $packageRoot 'app') $entry.Path) -Destination $target
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.PatchedHash) { throw "Post-copy verification failed: $($entry.Path)" }
    }
} catch {
    foreach ($entry in $attempted) {
        Copy-Item -LiteralPath (Join-Path $backup $entry.Path) -Destination (Join-Path $targetRoot $entry.Path)
    }
    throw
}
$logRoot = Join-Path $targetRoot 'SessionLog'
try {
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    @"
# Phase 1 - Team modal
Applied: $(Get-Date -Format o)
Updated js/settings.js and js/permissions.js.
Restored tab switching, independent member selection, load/save guards, stale-response protection, and read-only controls.
Original files: $backup
Source regression checks: node tests/team-modal.cjs in AiEmailerBackend.
Live browser/database verification remains required after application.
"@ | Set-Content -LiteralPath (Join-Path $logRoot "team-modal-$stamp.md") -Encoding UTF8
} catch { Write-Warning "Patch applied, but the session log could not be written: $($_.Exception.Message)" }
Write-Output "Team modal fix applied and hashes verified. Backup: $backup"
Write-Output 'Refresh Settings with Ctrl+F5. Test Teams > Manage members before deploying.'
