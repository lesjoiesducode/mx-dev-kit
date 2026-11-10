# Removes what install.ps1 added. Your config.json stays unless you pass -Purge.
# install.ps1 leaves a copy of this script in %APPDATA%\mx-dev-kit:
#
#   powershell -ExecutionPolicy Bypass -File "$env:APPDATA\mx-dev-kit\uninstall.ps1" [-Purge]
#
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param([switch]$Purge)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hooks/settings-json.ps1')
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

Write-Host '== PowerShell'
if ((Test-Path $PROFILE) -and (Select-String -Path $PROFILE -SimpleMatch '>>> mx-dev-kit >>>' -Quiet)) {
    $backup = Get-BackupName $PROFILE
    Copy-Item $PROFILE $backup
    # Same encoding as before: the byte order mark, or the one PowerShell reads a file without it.
    $reader = New-Object IO.StreamReader($backup, [Text.Encoding]::Default, $true)
    try { $text = $reader.ReadToEnd(); $encoding = $reader.CurrentEncoding } finally { $reader.Dispose() }
    $kept = New-Object Collections.Generic.List[string]
    $inside = $false
    $blank = $false
    foreach ($line in ($text -split "`r?`n")) {
        if ($inside) { if ($line -eq '# <<< mx-dev-kit <<<') { $inside = $false }; continue }
        # The block, and the empty line install.ps1 put before it.
        if ($line -eq '# >>> mx-dev-kit >>>') { $inside = $true; $blank = $false; continue }
        if ($blank) { $kept.Add(''); $blank = $false }
        if ($line -eq '') { $blank = $true; continue }
        $kept.Add($line)
    }
    $newline = if ($text -match "`r`n") { "`r`n" } else { "`n" }
    $result = ($kept -join $newline)
    if ($kept.Count -gt 0 -and $text.EndsWith("`n")) { $result += $newline }
    [IO.File]::WriteAllText((Resolve-Path -LiteralPath $PROFILE).ProviderPath, $result, $encoding)
    Write-Host "Removed from $PROFILE (backup: $backup)"
}

Write-Host '== Hooks'
& (Join-Path $root 'hooks\uninstall.ps1')

Write-Host '== Files'
foreach ($item in 'bin', 'shell', 'hooks', 'preview', 'plugin.new', 'plugin.old', 'config.example.json', 'current-dir', 'trigger', 'MxDevKit.lplug4', 'uninstall.ps1') {
    Remove-Item (Join-Path $state $item) -Recurse -Force -ErrorAction SilentlyContinue
}
if ($Purge) {
    Remove-Item (Join-Path $state 'config.json'), (Join-Path $state 'haptics.off'), (Join-Path $state 'notifications.off') -ErrorAction SilentlyContinue
    if (-not (Get-ChildItem $state -ErrorAction SilentlyContinue)) { Remove-Item $state -ErrorAction SilentlyContinue }
} else {
    Write-Host "Kept $state\config.json (-Purge removes it)."
}
