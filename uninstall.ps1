# Removes what install.ps1 added. Your config.json stays unless you pass -Purge.
# install.ps1 leaves a copy of this script in %APPDATA%\mx-dev-kit:
#
#   powershell -ExecutionPolicy Bypass -File "$env:APPDATA\mx-dev-kit\uninstall.ps1" [-Purge]
#
# The Haptic Web Plugin stays: other tools may use it. Uninstall it from Logi Options+.
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param([switch]$Purge)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hooks/settings-json.ps1')
. (Join-Path $PSScriptRoot 'shell/profiles.ps1')
$root = $PSScriptRoot
$state = Join-Path $env:APPDATA 'mx-dev-kit'
$plugins = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Plugins'

Write-Host '== Plugin'
$link = Join-Path $plugins 'MxDevKitPlugin.link'
if (Test-Path $link) {
    Remove-Item $link
    Write-Host 'Removed the development link. Restart Logi Plugin Service (Logi Options+ settings) to unload it.'
}
if (Test-Path (Join-Path $plugins 'MxDevKit')) {
    # Contributors have LogiPluginTool; everyone else goes through Options+.
    $done = $false
    if (Get-Command logiplugintool -ErrorAction SilentlyContinue) {
        logiplugintool uninstall MxDevKit
        $done = $LASTEXITCODE -eq 0
    }
    if (-not $done) {
        # The folder goes, as an uninstall from Options+ does. Windows keeps a loaded DLL locked.
        try {
            Remove-Item (Join-Path $plugins 'MxDevKit') -Recurse -Force
            try { Start-Process 'loupedeck://plugin/MxDevKit/reload' } catch { }
            Write-Host "Removed $(Join-Path $plugins 'MxDevKit'). If its actions still show in Logi Options+, restart Logi Plugin Service (Options+ settings)."
        } catch {
            Write-Host "Logi Plugin Service is using it: quit Logi Plugin Service, then delete $(Join-Path $plugins 'MxDevKit')."
        }
    }
}
if (Test-Path (Join-Path $plugins 'HapticWeb')) {
    Write-Host 'The Haptic Web Plugin stays installed: other tools may use it. Uninstall it from Logi Options+ if you do not need it.'
}

Write-Host '== PowerShell'
Uninstall-MxProfiles

Write-Host '== Hooks'
& (Join-Path $root 'hooks\uninstall.ps1')

Write-Host '== Files'
foreach ($item in 'bin', 'shell', 'hooks', 'turns', 'preview', 'plugin.new', 'plugin.old', 'config.example.json', 'current-dir', 'current-terminal', 'trigger', 'MxDevKit.lplug4', 'HapticWeb.lplug4.part', 'haptic-web.new', 'uninstall.ps1') {
    Remove-Item (Join-Path $state $item) -Recurse -Force -ErrorAction SilentlyContinue
}
if ($Purge) {
    Remove-Item (Join-Path $state 'config.json'), (Join-Path $state 'haptics.off'), (Join-Path $state 'notifications.off') -ErrorAction SilentlyContinue
    if (-not (Get-ChildItem $state -ErrorAction SilentlyContinue)) { Remove-Item $state -ErrorAction SilentlyContinue }
} else {
    Write-Host "Kept $state\config.json (-Purge removes it)."
}
