# Removes the haptic hooks added by hooks\install.ps1 (Claude Code, Gemini CLI, Codex CLI, Copilot CLI).
# Other hooks are kept.
# Each settings.json (hooks.json for Codex) is backed up before it changes.
# Untested on Windows: please test before relying on it, issues and PRs welcome.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'settings-json.ps1')

$state = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }
$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $HOME '.claude' }
$codexDir = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $HOME '.codex' }
$copilotDir = if ($env:COPILOT_HOME) { $env:COPILOT_HOME } else { Join-Path $HOME '.copilot' }

foreach ($path in @((Join-Path $claudeDir 'settings.json'), (Join-Path $(if ($env:GEMINI_CLI_HOME) { $env:GEMINI_CLI_HOME } else { $HOME }) '.gemini/settings.json'), (Join-Path $codexDir 'hooks.json'))) {
    if (-not (Test-Path $path) -or -not (Select-String -Path $path -Pattern 'agent-haptics' -Quiet)) { continue }
    try {
        $settings = Read-SettingsJson $path
    } catch {
        Write-Warning "$path is not valid JSON, left untouched."
        continue
    }

    foreach ($name in @($settings.hooks.PSObject.Properties.Name)) {
        Set-EventHooks $settings $name (Remove-OurHooks (Get-EventHooks $settings $name))
    }
    if (@($settings.hooks.PSObject.Properties).Count -eq 0) {
        $settings.PSObject.Properties.Remove('hooks')
    }

    $created = Join-Path $state 'hooks-created'
    if (@($settings.PSObject.Properties).Count -eq 0 -and (Test-Path $created) -and
        (@(Get-Content -Path $created -Encoding UTF8) -contains $path)) {
        Remove-Item $path
        Write-Host "Removed $path (hooks\install.ps1 had created it for the hooks alone)"
        continue
    }

    Save-SettingsJson $settings $path $path | Out-Null
    Write-Host "Hooks removed from $path"
}

# Copilot CLI: the file is the kit's own, nothing else lives in it.
$copilotFile = Join-Path $copilotDir 'hooks\mx-dev-kit.json'
if ((Test-Path $copilotFile) -and (Select-String -Path $copilotFile -Pattern 'agent-haptics' -Quiet)) {
    Remove-Item $copilotFile
    Write-Host "Removed $copilotFile"
}

Remove-Item (Join-Path $state 'bin/agent-haptics.ps1'), (Join-Path $state 'hooks-created') -ErrorAction SilentlyContinue
