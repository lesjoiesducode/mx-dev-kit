# Vibrates the MX Master 4 when a coding agent needs you, through the Haptic Web Plugin.
# Windows version of agent-haptics.sh, same arguments and environment variables:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File agent-haptics.ps1 claude Stop
#
# Codex's older `notify` program passes its JSON as the last argument instead of stdin.
#
# The end of a turn vibrates only when the turn lasted $env:MX_TURN_SECONDS (30) or more, as on
# macOS: UserPromptSubmit (Claude Code, Codex), BeforeAgent (Gemini CLI) and userPromptSubmitted
# (Copilot CLI) note when it starts. Waiting for you and failures always vibrate.
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

$json = $null
if ($payload.Trim()) { try { $json = $payload | ConvertFrom-Json } catch { } }

# One file per session, holding the time its turn started.
function Get-TurnFile {
    $id = $null
    if ($json) {
        $id = "$($json.session_id)"
        if (-not $id) { $id = "$($json.sessionId)" }
        if (-not $id -and $json.cwd) {
            $sha = New-Object Security.Cryptography.SHA1Managed
            $id = 'cwd-' + ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("$($json.cwd)"))) -replace '-', '').Substring(0, 12)
        }
    }
    if (-not $id) { $id = 'default' }
    return Join-Path (Join-Path $state 'turns') ("$Agent-" + ($id -replace '[^A-Za-z0-9_.-]', '_'))
}

function Get-Now { return [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() }

# True when the turn that just ended was shorter than MX_TURN_SECONDS.
function Test-ShortTurn {
    $min = 30
    if ("$env:MX_TURN_SECONDS" -match '^\d+$') { $min = [int]$env:MX_TURN_SECONDS }
    if ($min -le 0) { return $false }
    $file = Get-TurnFile
    if (-not (Test-Path -LiteralPath $file)) { return $false }
    $start = "$(Get-Content -LiteralPath $file -ErrorAction SilentlyContinue)".Trim()
    if ($start -notmatch '^\d+$') { return $false }
    return ((Get-Now) - [long]$start) -lt $min
}

# The quotes of the notify JSON may not survive the command line: look for the type itself.
if ($Agent -eq 'codex' -and "$HookEvent".TrimStart().StartsWith('{')) {
    $HookEvent = if ("$HookEvent $Rest" -match 'agent-turn-complete') { 'notify:agent-turn-complete' } else { 'notify' }
}

$wave = $null
switch ("${Agent}:${HookEvent}") {
    { $_ -in 'claude:UserPromptSubmit', 'gemini:BeforeAgent', 'codex:UserPromptSubmit', 'copilot:userPromptSubmitted' } {
        try {
            $turns = Join-Path $state 'turns'
            New-Item -ItemType Directory -Force -Path $turns | Out-Null
            [IO.File]::WriteAllText((Get-TurnFile), "$(Get-Now)")
            # Sessions that ended long ago.
            Get-ChildItem $turns -File | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-1) } | Remove-Item -ErrorAction SilentlyContinue
        } catch { }
        Finish
    }
    { $_ -in 'claude:Stop', 'gemini:AfterAgent', 'codex:Stop', 'codex:notify:agent-turn-complete' } {
        if (Test-ShortTurn) { Finish }
        $wave = if ($env:MX_WAVE_DONE) { $env:MX_WAVE_DONE } else { 'completed' }
    }
    { $_ -in 'claude:Notification', 'gemini:Notification' } {
        $type = $null
        if ($json) { $type = $json.notification_type }
        if ($type -eq 'auth_success') { Finish }
        if ($type -eq 'idle_prompt' -and $env:MX_IDLE_REMINDER -ne '1') { Finish }
        $wave = if ($env:MX_WAVE_ATTENTION) { $env:MX_WAVE_ATTENTION } else { 'knock' }
    }
    'codex:PermissionRequest' {
        $wave = if ($env:MX_WAVE_ATTENTION) { $env:MX_WAVE_ATTENTION } else { 'knock' }
    }
    'copilot:agentStop' {
        if (Test-ShortTurn) { Finish }
        $wave = if ($env:MX_WAVE_DONE) { $env:MX_WAVE_DONE } else { 'completed' }
    }
    'copilot:notification' {
        # Only what waits for you: a permission prompt on screen, or a question.
        $type = $null
        if ($json) { $type = $json.notification_type }
        if ($type -notin 'permission_prompt', 'elicitation_dialog') { Finish }
        $wave = if ($env:MX_WAVE_ATTENTION) { $env:MX_WAVE_ATTENTION } else { 'knock' }
    }
    'copilot:errorOccurred' {
        # Copilot retries the errors it can recover from: only the others end the turn.
        $recoverable = $false
        if ($json) { $recoverable = [bool]$json.recoverable }
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

# local.jmw.nz is a public name for 127.0.0.1: curl.exe (built into Windows 10 and 11) resolves it
# locally, so it works offline and behind resolvers that block such names. Started without waiting.
$url = if ($env:MX_HAPTIC_URL) { $env:MX_HAPTIC_URL.TrimEnd('/') } else { 'https://local.jmw.nz:41443/haptic' }
$curl = if ($env:SystemRoot -and (Test-Path (Join-Path $env:SystemRoot 'System32\curl.exe'))) { Join-Path $env:SystemRoot 'System32\curl.exe' } else { 'curl' }
try {
    $psi = New-Object Diagnostics.ProcessStartInfo $curl
    $sink = if ($env:OS -eq 'Windows_NT') { 'NUL' } else { '/dev/null' }
    $psi.Arguments = "-s -m 2 -o $sink -X POST -d `"`" --resolve local.jmw.nz:41443:127.0.0.1 `"$url/$wave`""
    if ($env:OS -eq 'Windows_NT') {
        # Through ShellExecute, curl.exe inherits none of our handles (nothing waits for it) and shows no window.
        $psi.UseShellExecute = $true
        $psi.WindowStyle = 'Hidden'
    } else {
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
    }
    ([Diagnostics.Process]::Start($psi)).Dispose()
} catch { }
Finish
