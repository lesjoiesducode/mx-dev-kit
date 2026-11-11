# Watches websites, health endpoints and local ports, and makes the MX Master 4 vibrate
# when one goes down (angry_alert) and when it comes back (happy_alert).
#
#   uptime-watch.ps1 https://example.com http://localhost:3000/health localhost:5432
#
# Same targets and options as bin/uptime-watch: -Interval 60 -Timeout 10 -Failures 2 -Quiet
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param(
    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)][string[]]$Targets,
    [int]$Interval = 60,
    [int]$Timeout = 10,
    [int]$Failures = 2,
    [switch]$Quiet
)

if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

$state = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }
$url = if ($env:MX_HAPTIC_URL) { $env:MX_HAPTIC_URL.TrimEnd('/') } else { 'https://local.jmw.nz:41443/haptic' }

# Without waiting, and without DNS: curl.exe (built into Windows 10 and 11) resolves local.jmw.nz,
# the public name of 127.0.0.1, locally.
function Send-Haptic([string]$Waveform) {
    if (Test-Path (Join-Path $state 'haptics.off')) { return }
    $curl = if ($env:SystemRoot -and (Test-Path (Join-Path $env:SystemRoot 'System32\curl.exe'))) { Join-Path $env:SystemRoot 'System32\curl.exe' } else { 'curl' }
    try {
        $psi = New-Object Diagnostics.ProcessStartInfo $curl
        $sink = if ($env:OS -eq 'Windows_NT') { 'NUL' } else { '/dev/null' }
        $psi.Arguments = "-s -m 2 -o $sink -X POST -d `"`" --resolve local.jmw.nz:41443:127.0.0.1 `"$url/$Waveform`""
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
}

# Returns @(up, reason).
function Test-Target([string]$Target) {
    if ($Target -match '^https?://') {
        try {
            $response = Invoke-WebRequest -Uri $Target -TimeoutSec $Timeout -UseBasicParsing -MaximumRedirection 5
            return @($true, "HTTP $([int]$response.StatusCode)")
        } catch {
            $code = $null
            if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
            if ($code) { return @(($code -lt 400), "HTTP $code") }
            return @($false, 'no answer')
        }
    }

    if ($Target -match '^(.+):(\d+)$') {
        $client = New-Object Net.Sockets.TcpClient
        try {
            $connect = $client.BeginConnect($Matches[1], [int]$Matches[2], $null, $null)
            if ($connect.AsyncWaitHandle.WaitOne($Timeout * 1000) -and $client.Connected) {
                $client.EndConnect($connect)
                return @($true, 'port open')
            }
            return @($false, 'connection refused or timed out')
        } catch {
            return @($false, 'connection refused or timed out')
        } finally {
            $client.Close()
        }
    }

    return @($false, 'not a URL or host:port')
}

$status = @{}
$fails = @{}
foreach ($t in $Targets) { $status[$t] = 'up'; $fails[$t] = 0 }

Write-Host "Watching $($Targets.Count) target(s) every ${Interval}s. Ctrl-C to stop."
while ($true) {
    foreach ($t in $Targets) {
        $up, $reason = Test-Target $t
        $now = Get-Date -Format 'HH:mm:ss'
        if ($up) {
            $fails[$t] = 0
            if ($status[$t] -eq 'down') {
                $status[$t] = 'up'
                Write-Host "$now  UP    $t ($reason)"
                Send-Haptic 'happy_alert'
            } elseif (-not $Quiet) {
                Write-Host "$now  ok    $t ($reason)"
            }
        } else {
            $fails[$t]++
            if ($status[$t] -eq 'up' -and $fails[$t] -ge $Failures) {
                $status[$t] = 'down'
                Write-Host "$now  DOWN  $t ($reason)"
                Send-Haptic 'angry_alert'
            } elseif (-not $Quiet) {
                $suffix = if ($status[$t] -eq 'down') { '' } else { ", $($fails[$t])/$Failures" }
                Write-Host "$now  $(if ($status[$t] -eq 'down') { 'down' } else { 'fail' })  $t ($reason$suffix)"
            }
        }
    }
    Start-Sleep -Seconds $Interval
}
