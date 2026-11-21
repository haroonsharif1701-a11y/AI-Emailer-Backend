param([switch]$Push, [switch]$CheckOnly, [string]$GitPath)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $GitPath) {
    $installedGit = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($installedGit) { $GitPath = $installedGit.Source }
    else {
        $candidates = @(
            "$env:ProgramFiles\Git\cmd\git.exe",
            "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe",
            "$env:ProgramFiles\Microsoft Visual Studio\*\*\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
        )
        $GitPath = Get-Item -Path $candidates -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $GitPath -or -not (Test-Path -LiteralPath $GitPath -PathType Leaf)) {
    throw 'Git was not found. Supply -GitPath with the full path to git.exe.'
}
function Invoke-Git {
    & $GitPath --no-pager @args
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $($args[0]). Stopping without further changes." }
}
Write-Host "Using Git: $GitPath"
Invoke-Git --version
if ((Invoke-Git branch --show-current) -ne 'main') { throw 'Run this on main only.' }
if (@(Invoke-Git ls-files -u).Count) { throw 'Resolve existing merge conflicts first.' }
if ($CheckOnly) {
    Invoke-Git diff --stat
    Invoke-Git diff --cached --stat
    Write-Host 'Git discovery, repository, and pager-free diff checks passed. No changes made.'
    return
}
Invoke-Git fetch origin
Invoke-Git merge-base --is-ancestor origin/main HEAD
$stamp = Get-Date -Format yyyyMMdd-HHmmss
$backup = Join-Path $PSScriptRoot "backups/git-repair-$stamp"
New-Item -ItemType Directory -Path $backup | Out-Null
Invoke-Git bundle create "$backup/history.bundle" --all
Invoke-Git diff --binary "--output=$backup/worktree.patch"
Invoke-Git diff --cached --binary "--output=$backup/index.patch"
Invoke-Git branch "local-backup-$stamp"
# Rebuild only unpublished commits. Soft reset preserves every working file.
Invoke-Git reset --soft origin/main
Invoke-Git add -u
$ignoredTracked = @(Invoke-Git ls-files -ci --exclude-standard)
foreach ($path in $ignoredTracked) { Invoke-Git rm --cached -- $path }
Invoke-Git add -- AiEmailerBackend.slnx Properties/launchSettings.json Database Repair-LocalGit.ps1
# Reject known local credential values in the proposed snapshot without printing them.
$local = Get-Content appsettings.Local.json -Raw | ConvertFrom-Json
$credentials = @($local.ConnectionStrings.Default, $local.Jwt.Key, $local.SystemKey.Key,
    $local.Authentication.Google.ClientId, $local.Authentication.Google.ClientSecret,
    $local.Authentication.EntraID.ClientSecret, $local.Llm.OpenAI.ApiKey) |
    Where-Object { $_ -and $_.Length -ge 8 }
$files = @(Invoke-Git ls-files)
foreach ($file in $files) {
    $content = (Invoke-Git show ":$file") -join "`n"
    foreach ($credential in $credentials) {
        $escaped = (ConvertTo-Json -InputObject $credential -Compress).Trim('"')
        if ($content.Contains($credential) -or $content.Contains($escaped)) {
            throw "A local credential remains staged in $file. Nothing was committed or pushed."
        }
    }
}
Invoke-Git diff --cached --stat
Invoke-Git commit -m 'Restore solution and keep local credentials and build output out of Git'
Write-Host "Original history saved locally in $backup and local-backup-$stamp. Do not push that backup branch."
if ($Push) { Invoke-Git push origin main }
