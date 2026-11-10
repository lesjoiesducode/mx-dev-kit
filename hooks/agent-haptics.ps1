# Vibrates the MX Master 4 when a coding agent needs you, through the Haptic Web Plugin.
# Windows version of agent-haptics.sh, same arguments and environment variables:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File agent-haptics.ps1 claude Stop
#
# Codex's older `notify` program passes its JSON as the last argument instead of stdin.
#
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param(
    [Parameter(Position = 0)][string]$Agent,
    [Parameter(Position = 1)][string]$HookEvent,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# Nothing redirected means a run by hand: do not wait for input that never comes.
$payload = if ([Console]::IsInputRedirected) { [Console]::In.ReadToEnd() } else { '' }
$state = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }

function Finish {
    if ($Agent -eq 'gemini') { Write-Output '{}' }
    exit 0
}

if (Test-Path (Join-Path $state 'haptics.off')) { Finish }

# The quotes of the notify JSON may not survive the command line: look for the type itself.
if ($Agent -eq 'codex' -and "$HookEvent".TrimStart().StartsWith('{')) {
    $HookEvent = if ("$HookEvent $Rest" -match 'agent-turn-complete') { 'notify:agent-turn-complete' } else { 'notify' }
}

$wave = $null
switch ("${Agent}:${HookEvent}") {
    { $_ -in 'claude:Stop', 'gemini:AfterAgent', 'codex:Stop', 'codex:notify:agent-turn-complete' } {
        $wave = if ($env:MX_WAVE_DONE) { $env:MX_WAVE_DONE } else { 'completed' }
    }
    { $_ -in 'claude:Notification', 'gemini:Notification' } {
        $type = $null
        try { $type = ($payload | ConvertFrom-Json).notification_type } catch { }
        if ($type -eq 'auth_success') { Finish }
        if ($type -eq 'idle_prompt' -and $env:MX_IDLE_REMINDER -ne '1') { Finish }
        $wave = if ($env:MX_WAVE_ATTENTION) { $env:MX_WAVE_ATTENTION } else { 'knock' }
    }
    'codex:PermissionRequest' {
        $wave = if ($env:MX_WAVE_ATTENTION) { $env:MX_WAVE_ATTENTION } else { 'knock' }
    }
    'copilot:agentStop' {
        $wave = if ($env:MX_WAVE_DONE) { $env:MX_WAVE_DONE } else { 'completed' }
    }
    'copilot:notification' {
        # Only what waits for you: a permission prompt on screen, or a question.
        $type = $null
        try { $type = ($payload | ConvertFrom-Json).notification_type } catch { }
        if ($type -notin 'permission_prompt', 'elicitation_dialog') { Finish }
        $wave = if ($env:MX_WAVE_ATTENTION) { $env:MX_WAVE_ATTENTION } else { 'knock' }
    }
    'copilot:errorOccurred' {
        # Copilot retries the errors it can recover from: only the others end the turn.
        $recoverable = $false
        try { $recoverable = [bool]($payload | ConvertFrom-Json).recoverable } catch { }
        if ($recoverable) { Finish }
        $wave = if ($env:MX_WAVE_ERROR) { $env:MX_WAVE_ERROR } else { 'angry_alert' }
    }
    'claude:StopFailure' {
        $wave = if ($env:MX_WAVE_ERROR) { $env:MX_WAVE_ERROR } else { 'angry_alert' }
    }
}

if (-not $wave) { Finish }

# Headless sessions (claude -p, scripts using the Agent SDK) have no one waiting for them.
if ($Agent -eq 'claude' -and $env:MX_HAPTICS_ALL_SESSIONS -ne '1' -and "$env:CLAUDE_CODE_ENTRYPOINT" -like 'sdk-*') { Finish }

if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

$url = if ($env:MX_HAPTIC_URL) { $env:MX_HAPTIC_URL.TrimEnd('/') } else { 'https://local.jmw.nz:41443/haptic' }
try {
    Invoke-RestMethod -Method Post -Uri "$url/$wave" -Body '' -ContentType 'text/plain' -TimeoutSec 2 | Out-Null
} catch { }
Finish
