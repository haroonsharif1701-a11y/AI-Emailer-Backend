param([string]$FrontendRoot = 'D:\AIEmailer\Ai-Emailer-Frontend\Ai-Emailer-Frontend\app')
$ErrorActionPreference = 'Stop'
$targetRoot = (Resolve-Path -LiteralPath $FrontendRoot).Path
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'original-hashes.json') -Raw | ConvertFrom-Json
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
$sessionDirectory = Join-Path $targetRoot 'SessionLog'
New-Item -ItemType Directory -Path $sessionDirectory -Force | Out-Null
@'
# Gmail mailbox integration
Applied provider-controlled login, OAuth code exchange, and live Gmail inbox UI from the backend's reviewed frontend integration files.
Original frontend files were backed up before copying. Live Google consent and SQL integration must be verified using GMAIL_IMPLEMENTATION.md in the backend repository.
'@ | Set-Content -LiteralPath (Join-Path $sessionDirectory ('gmail-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.md'))
Write-Output 'Frontend integration applied. Start the API and frontend, sign in, then connect Gmail.'
