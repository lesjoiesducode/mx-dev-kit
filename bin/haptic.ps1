# Plays a waveform on the MX Master 4 through the Haptic Web Plugin.
#
#   haptic.ps1 completed      play one waveform
#   haptic.ps1 -List          waveforms the plugin knows
#
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param(
    [Parameter(Position = 0)][string]$Waveform,
    [switch]$List
)

$url = if ($env:MX_HAPTIC_URL) { $env:MX_HAPTIC_URL.TrimEnd('/') } else { 'https://local.jmw.nz:41443/haptic' }
$state = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }

# Windows PowerShell 5.1 may still default to TLS 1.0.
if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

if ($List) {
    (Invoke-RestMethod -Uri (($url -replace '/haptic$', '') + '/waveforms') -TimeoutSec 3).waveforms
    return
}

if (-not $Waveform) {
    Write-Host 'Usage: haptic.ps1 <waveform> | -List'
    exit 2
}

if (Test-Path (Join-Path $state 'haptics.off')) { exit 0 }

try {
    Invoke-RestMethod -Method Post -Uri "$url/$Waveform" -Body '' -ContentType 'text/plain' -TimeoutSec 3 | Out-Null
} catch {
    Write-Error "haptic: no answer from the Haptic Web Plugin at $url ($($_.Exception.Message))"
    exit 1
}
