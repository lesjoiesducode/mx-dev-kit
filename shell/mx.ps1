# MX Dev Kit: shell integration for PowerShell (Windows PowerShell 5.1 and PowerShell 7).
# Dot-source it from your profile (notepad $PROFILE), after any prompt theme:
#
#   . "$env:APPDATA\mx-dev-kit\shell\mx.ps1"
#
#   - tells the MX Dev Kit plugin which folder you are working in
#   - commands that run longer than $env:MX_LONG_COMMAND_SECONDS vibrate when they end
#   - buzz <command>        vibrates when the command ends, whatever its duration
#   - ci-watch [sha]        waits for the GitHub Actions runs of a commit, vibrates at the end
#
# Untested on Windows: please test before relying on it, issues and PRs welcome.

$global:MxDevKitDir = Split-Path -Parent $PSScriptRoot
$global:MxStateDir = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }
if (-not $env:MX_HAPTIC_URL) { $env:MX_HAPTIC_URL = 'https://local.jmw.nz:41443/haptic' }
if (-not $env:MX_LONG_COMMAND_SECONDS) { $env:MX_LONG_COMMAND_SECONDS = '30' }
if (-not $env:MX_LONG_COMMAND_IGNORE) { $env:MX_LONG_COMMAND_IGNORE = 'vim nvim nano less more man ssh top htop tail claude gemini codex' }

New-Item -ItemType Directory -Force -Path $global:MxStateDir -ErrorAction SilentlyContinue | Out-Null
$ringBin = Join-Path $global:MxDevKitDir 'bin'
if ((Test-Path $ringBin) -and ($env:PATH -split [IO.Path]::PathSeparator) -notcontains $ringBin) {
    $env:PATH = $ringBin + [IO.Path]::PathSeparator + $env:PATH
}

if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

function global:Invoke-MxHaptic([string]$Waveform) {
    if (Test-Path (Join-Path $global:MxStateDir 'haptics.off')) { return }
    try {
        Invoke-RestMethod -Method Post -Uri "$($env:MX_HAPTIC_URL.TrimEnd('/'))/$Waveform" -Body '' -ContentType 'text/plain' -TimeoutSec 2 | Out-Null
    } catch { }
}

function global:buzz {
    if ($args.Count -eq 0) { Write-Host 'Usage: buzz <command> [arguments]'; return }
    $rest = @($args | Select-Object -Skip 1)
    & $args[0] @rest
    $ok = $?
    $global:MxBuzzed = $true
    Invoke-MxHaptic $(if ($ok) { 'completed' } else { 'angry_alert' })
}

function global:ci-watch([string]$Sha) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Write-Error 'ci-watch: the GitHub CLI (gh) is not installed'; return }
    if (-not $Sha) { $Sha = git rev-parse HEAD 2>$null }
    if (-not $Sha) { Write-Error 'ci-watch: not in a git repository'; return }

    Write-Host "Waiting for the workflow runs of $($Sha.Substring(0, 7))..."
    $ids = @()
    for ($i = 0; $i -lt 24 -and $ids.Count -eq 0; $i++) {
        $ids = @(gh run list --commit $Sha --json databaseId -q '.[].databaseId' 2>$null | Where-Object { $_ })
        if ($ids.Count -eq 0) { Start-Sleep -Seconds 5 }
    }
    if ($ids.Count -eq 0) { Write-Error "ci-watch: no workflow run for $($Sha.Substring(0, 7)) after two minutes"; return }

    $failed = $false
    foreach ($id in $ids) {
        gh run watch $id --exit-status
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    }
    $global:MxBuzzed = $true
    Invoke-MxHaptic $(if ($failed) { 'angry_alert' } else { 'happy_alert' })
}

function global:Invoke-MxAfterCommand([bool]$Ok) {
    if ($PWD.Provider.Name -eq 'FileSystem') {
        try { [IO.File]::WriteAllText((Join-Path $global:MxStateDir 'current-dir'), $PWD.ProviderPath) } catch { }
    }

    $buzzed = $global:MxBuzzed
    $global:MxBuzzed = $false
    $last = Get-History -Count 1
    if (-not $last -or $last.Id -eq $global:MxLastHistoryId) { return }
    $global:MxLastHistoryId = $last.Id
    if ($buzzed) { return }

    $threshold = [int]$env:MX_LONG_COMMAND_SECONDS
    $seconds = ($last.EndExecutionTime - $last.StartExecutionTime).TotalSeconds
    if ($threshold -le 0 -or $seconds -lt $threshold) { return }

    $first = ($last.CommandLine.Trim() -split '\s+')[0]
    if (($env:MX_LONG_COMMAND_IGNORE -split '\s+') -contains $first) { return }
    Invoke-MxHaptic $(if ($Ok) { 'completed' } else { 'angry_alert' })
}

if (-not $global:MxOriginalPrompt) {
    $global:MxOriginalPrompt = $function:prompt
    function global:prompt {
        $ok = $?
        try { Invoke-MxAfterCommand $ok } catch { }
        # Give the original prompt (themes often colour it) the status of the last command again.
        if (-not $ok) { Write-Error '' -ErrorAction Ignore }
        & $global:MxOriginalPrompt
    }
}
