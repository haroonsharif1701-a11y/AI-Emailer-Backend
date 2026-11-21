param([string]$FrontendRoot = 'D:\AIEmailer\Ai-Emailer-Frontend\Ai-Emailer-Frontend\app')
$ErrorActionPreference = 'Stop'
$targetRoot = (Resolve-Path -LiteralPath $FrontendRoot).Path
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'inbox-ui-hashes.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $entry.Path))
    if (-not $target.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid target path.' }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Hash) { throw "Frontend changed since preparation: $($entry.Path). Review before applying." }
}
$backup = Join-Path $PSScriptRoot ('..\backups\frontend-apply-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($entry in $manifest) {
    $saved = Join-Path $backup $entry.Path
    New-Item -ItemType Directory -Path (Split-Path $saved) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $targetRoot $entry.Path) -Destination $saved
}
foreach ($entry in $manifest) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('app\' + $entry.Path)) -Destination (Join-Path $targetRoot $entry.Path)
}
Write-Output 'Inbox styling applied. Refresh the inbox with Ctrl+F5.'
