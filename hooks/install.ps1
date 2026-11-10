# Adds the haptic hooks to Claude Code, Gemini CLI, Codex CLI and/or GitHub Copilot CLI on Windows.
#
#   .\hooks\install.ps1                 every agent found (~\.claude, ~\.gemini, ~\.codex, ~\.copilot)
#   .\hooks\install.ps1 -Claude         Claude Code only
#   .\hooks\install.ps1 -Gemini         Gemini CLI only
#   .\hooks\install.ps1 -Codex          Codex CLI only
#   .\hooks\install.ps1 -Copilot        Copilot CLI only (a file of its own, ~\.copilot\hooks\mx-dev-kit.json)
#   .\hooks\install.ps1 -DryRun         show the resulting settings without writing them
#
# An existing settings.json (hooks.json for Codex) is backed up next to itself before any change.
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param([switch]$Claude, [switch]$Gemini, [switch]$Codex, [switch]$Copilot, [switch]$DryRun)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'settings-json.ps1')

$state = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }
$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $HOME '.claude' }
$geminiDir = Join-Path $HOME '.gemini'
$codexDir = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $HOME '.codex' }
$copilotDir = if ($env:COPILOT_HOME) { $env:COPILOT_HOME } else { Join-Path $HOME '.copilot' }

if (-not $Claude -and -not $Gemini -and -not $Codex -and -not $Copilot) {
    $Claude = (Test-Path $claudeDir) -or [bool](Get-Command claude -ErrorAction SilentlyContinue)
    $Gemini = (Test-Path $geminiDir) -or [bool](Get-Command gemini -ErrorAction SilentlyContinue)
    $Codex = (Test-Path $codexDir) -or [bool](Get-Command codex -ErrorAction SilentlyContinue)
    $Copilot = (Test-Path $copilotDir) -or [bool](Get-Command copilot -ErrorAction SilentlyContinue)
}

if (-not $Claude -and -not $Gemini -and -not $Codex -and -not $Copilot) {
    Write-Host 'No coding agent found (looked for Claude Code, Gemini CLI, Codex CLI and Copilot CLI). Use -Claude, -Gemini, -Codex or -Copilot to force.'
    return
}

$hook = Join-Path $state 'bin/agent-haptics.ps1'
if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path (Split-Path $hook) | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'agent-haptics.ps1') $hook -Force
}

# Forward slashes survive both cmd.exe and the bash that some agents use to run hooks.
$command = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$($hook -replace '\\', '/')`""

$targets = @()
if ($Claude) {
    $targets += @{ Name = 'Claude Code'; Path = (Join-Path $claudeDir 'settings.json'); Events = [ordered]@{
        Stop = [ordered]@{ type = 'command'; command = "$command claude Stop"; timeout = 5 }
        Notification = [ordered]@{ type = 'command'; command = "$command claude Notification"; timeout = 5 }
        StopFailure = [ordered]@{ type = 'command'; command = "$command claude StopFailure"; timeout = 5 }
    } }
}
if ($Gemini) {
    $targets += @{ Name = 'Gemini CLI'; Path = (Join-Path $geminiDir 'settings.json'); Events = [ordered]@{
        AfterAgent = [ordered]@{ name = 'mx-dev-kit-haptics'; type = 'command'; command = "$command gemini AfterAgent"; timeout = 5000 }
        Notification = [ordered]@{ name = 'mx-dev-kit-haptics'; type = 'command'; command = "$command gemini Notification"; timeout = 5000 }
    } }
}
if ($Codex) {
    $targets += @{ Name = 'Codex CLI'; Path = (Join-Path $codexDir 'hooks.json'); Events = [ordered]@{
        Stop = [ordered]@{ type = 'command'; command = "$command codex Stop"; timeout = 5 }
        PermissionRequest = [ordered]@{ type = 'command'; command = "$command codex PermissionRequest"; timeout = 5 }
    } }
}

# Copilot CLI loads every file of ~\.copilot\hooks: the kit's hooks get one of their own.
# Notification fires for more than the prompts that wait for you; the script keeps those.
if ($Copilot) {
    $file = Join-Path $copilotDir 'hooks\mx-dev-kit.json'
    $events = [ordered]@{}
    foreach ($name in 'agentStop', 'notification', 'errorOccurred') {
        $events[$name] = @([ordered]@{ type = 'command'; powershell = "$command copilot $name"; timeoutSec = 5 })
    }
    $content = ConvertTo-Json ([ordered]@{ version = 1; hooks = $events }) -Depth 8
    if ($DryRun) {
        Write-Host "== Copilot CLI: $file would be"
        Write-Host $content
    } elseif ((Test-Path $file) -and ((Get-Content -Raw -Path $file -Encoding UTF8).Trim() -eq $content.Trim())) {
        Write-Host "Copilot CLI: hooks already up to date in $file"
    } else {
        New-Item -ItemType Directory -Force -Path (Split-Path $file) | Out-Null
        if (Test-Path $file) {
            $backup = "$file.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Copy-Item $file $backup
            Write-Host "Copilot CLI: backup saved to $backup"
        }
        [IO.File]::WriteAllText($file, $content + "`n", (New-Object Text.UTF8Encoding($false)))
        Write-Host "Copilot CLI: haptic hooks added to $file (Copilot reads it when it starts)"
    }
}

foreach ($t in $targets) {
    try {
        $settings = Read-SettingsJson $t.Path
    } catch {
        Write-Warning "$($t.Name): $($t.Path) is not valid JSON, left untouched. Fix it, then run this again."
        continue
    }

    foreach ($name in $t.Events.Keys) {
        $hooks = Remove-OurHooks (Get-EventHooks $settings $name)
        $hooks += [pscustomobject]@{ hooks = @([pscustomobject]$t.Events[$name]) }
        Set-EventHooks $settings $name $hooks
    }

    if ($DryRun) {
        Write-Host "== $($t.Name): $($t.Path) would become"
        Write-Host (ConvertTo-Json $settings -Depth 64)
        continue
    }

    $existed = Test-Path $t.Path
    if (Save-SettingsJson $settings $t.Path $t.Name) {
        if (-not $existed) {
            # Created here: hooks\uninstall.ps1 deletes it again if nothing else ends up in it.
            $created = Join-Path $state 'hooks-created'
            if (-not ((Test-Path $created) -and (Select-String -Path $created -SimpleMatch $t.Path -Quiet))) {
                Add-Content -Path $created -Value $t.Path -Encoding UTF8
            }
        }
        Write-Host "$($t.Name): haptic hooks added to $($t.Path)"
        if ($t.Name -eq 'Codex CLI') {
            Write-Host 'Codex CLI: Codex skips new hooks until you trust them: open Codex and type /hooks once.'
            # Same layer in hooks.json and config.toml: Codex runs both but warns at startup.
            $toml = Join-Path $codexDir 'config.toml'
            if ((Test-Path $toml) -and (Select-String -Path $toml -Pattern '^\s*\[\[?hooks[\].]' -Quiet)) {
                Write-Host "Codex CLI: $toml also has [hooks] tables. Codex merges both files and warns at startup; move them to hooks.json to silence it."
            }
        }
    } else {
        Write-Host "$($t.Name): hooks already up to date in $($t.Path)"
    }
}

if (-not $DryRun) {
    Write-Host "Hook script: $hook (install the Haptic Web Plugin from the Logi Marketplace if not done yet)"
}
