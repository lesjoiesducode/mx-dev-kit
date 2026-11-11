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

# curl.exe, built into Windows 10 and 11. local.jmw.nz is a public name for 127.0.0.1: resolved locally,
# so it works offline and behind resolvers that block such names. A command you run: it waits for
# the answer (3 seconds at most) and says when there is none.
$curl = if ($env:SystemRoot -and (Test-Path (Join-Path $env:SystemRoot 'System32\curl.exe'))) { Join-Path $env:SystemRoot 'System32\curl.exe' } else { 'curl' }
$resolve = '--resolve', 'local.jmw.nz:41443:127.0.0.1'
$sink = if ($env:OS -eq 'Windows_NT') { 'NUL' } else { '/dev/null' }

if ($List) {
    $text = & $curl -sS -m 3 @resolve (($url -replace '/haptic$', '') + '/waveforms')
    if ($LASTEXITCODE -ne 0) { exit 1 }
    try { ($text | ConvertFrom-Json).waveforms } catch { $text }
    return
}

if (-not $Waveform) {
    Write-Host 'Usage: haptic.ps1 <waveform> | -List'
    exit 2
}

if (Test-Path (Join-Path $state 'haptics.off')) { exit 0 }

# An empty POST. (-d "" would not survive: Windows PowerShell 5.1 drops empty arguments to programs.)
$code = & $curl -s -m 3 -o $sink -w '%{http_code}' -X POST -H 'Content-Length: 0' @resolve "$url/$Waveform"
switch -Regex ("$code") {
    '^2\d\d$' { exit 0 }
    '^(000)?$' { [Console]::Error.WriteLine("haptic: Haptic Web Plugin unreachable at $url (is it installed and is Logi Options+ running?)") }
    default { [Console]::Error.WriteLine("haptic: HTTP $code for '$Waveform' (haptic.ps1 -List shows the waveforms)") }
}
exit 1
