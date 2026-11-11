# Watches Docker containers and makes the MX Master 4 vibrate when one dies (angry_alert), restarts
# in a loop (angry_alert, once) or becomes healthy again (happy_alert), with a notification that says
# which one and why.
#
#   docker-watch.ps1                containers of the compose project of this folder, or all of them
#   docker-watch.ps1 -All           every container
#   docker-watch.ps1 -Project NAME  the containers of one compose project
#   docker-watch.ps1 -Quiet         only print what vibrates
#
# Same behaviour as bin/docker-watch, from the `docker events` stream of Docker Desktop.
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param([switch]$All, [string]$Project, [switch]$Quiet)

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error 'docker-watch: docker not found (Docker Desktop, or any Docker engine with its CLI)'
    exit 1
}

if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

$state = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }
$url = if ($env:MX_HAPTIC_URL) { $env:MX_HAPTIC_URL.TrimEnd('/') } else { 'https://local.jmw.nz:41443/haptic' }
$notify = Join-Path $PSScriptRoot 'mx-notify.ps1'

function Send-Haptic([string]$Waveform) {
    if (Test-Path (Join-Path $state 'haptics.off')) { return }
    try { Invoke-RestMethod -Method Post -Uri "$url/$Waveform" -Body '' -ContentType 'text/plain' -TimeoutSec 2 | Out-Null } catch { }
}

function Get-Norm([string]$Path) { if ($Path) { $Path.ToLowerInvariant().Replace('\', '/').TrimEnd('/') } else { '' } }

# The compose project of this folder: the first folder with a compose file, up to the git root.
$dirs = @()
if (-not $All -and -not $Project) {
    $top = git --no-optional-locks rev-parse --show-toplevel 2>$null
    $top = if ($top) { Get-Norm $top } else { '' }
    $d = (Get-Location).ProviderPath
    while ($d) {
        $found = 'compose.yaml', 'compose.yml', 'docker-compose.yaml', 'docker-compose.yml' | Where-Object { Test-Path (Join-Path $d $_) }
        if ($found) { $dirs = @(@($d, (Get-Item $d).FullName) | Select-Object -Unique); break }
        if ((Get-Norm $d) -eq $top) { break }
        $parent = Split-Path -Parent $d
        if (-not $parent -or $parent -eq $d) { break }
        $d = $parent
    }
}
$scopeDirs = @($dirs | ForEach-Object { Get-Norm $_ })

$scope = if ($Project) { "the containers of the compose project $Project" }
         elseif ($dirs) { "the containers of the compose project in $($dirs[0]) (-All for every container)" }
         else { 'every container' }

function Test-InScope($Attributes) {
    if ($Project) { return $Attributes.'com.docker.compose.project' -eq $Project }
    if ($scopeDirs.Count -eq 0) { return $true }
    return $scopeDirs -contains (Get-Norm $Attributes.'com.docker.compose.project.working_dir')
}

function Get-Where($Attributes) {
    $service = $Attributes.'com.docker.compose.service'
    if ($service) { ", service $service of $($Attributes.'com.docker.compose.project')" } else { '' }
}

function Say([string]$Text) { if (-not $Quiet) { Write-Host "$(Get-Date -Format 'HH:mm:ss')  $Text" } }

function Alert([string]$Waveform, [string]$Title, [string]$Text, [string]$Line) {
    Write-Host "$(Get-Date -Format 'HH:mm:ss')  $Line"
    Send-Haptic $Waveform
    if (Test-Path $notify) { & $notify $Title $Text | Out-Null }
}

$intended = @{}
$oom = @{}
$health = @{}
$deaths = @{}
$quietUntil = @{}

function Invoke-Event([string]$Json) {
    try { $e = $Json | ConvertFrom-Json } catch { return }
    $a = $e.Actor.Attributes
    $name = $a.name
    if (-not $name -or -not (Test-InScope $a)) { return }
    $id = $e.Actor.ID
    $t = [double]$e.timeNano / 1e9
    $action = [string]$e.Action

    if ($action -eq 'kill' -or $action -eq 'stop') { $intended[$id] = $t; return }
    if ($action -eq 'oom') { $oom[$id] = $t; return }

    if ($action -like 'health_status*') {
        $status = ($action -replace '^health_status:\s*', '')
        if ($status -eq 'unhealthy' -and $health[$name] -ne 'unhealthy') { Say "unhealthy $name" }
        if ($status -eq 'healthy' -and $health[$name] -eq 'unhealthy') {
            Alert 'happy_alert' "$name is healthy again" "Its healthcheck passes again$(Get-Where $a)." "HEALTHY  $name (healthy again)"
        }
        if ($status -eq 'healthy' -or $status -eq 'unhealthy') { $health[$name] = $status }
        return
    }

    if ($action -ne 'die') { return }
    $out = $oom.ContainsKey($id) -and ($t - $oom[$id]) -le 5
    $oom.Remove($id)
    if (-not $out -and $intended.ContainsKey($id) -and ($t - $intended[$id]) -le 5) {
        $intended.Remove($id)
        Say "stopped  $name (on purpose)"
        return
    }
    $code = [string]$a.exitCode
    if (-not $out -and $code -eq '0') { Say "exited   $name (exit 0)"; return }
    $reason = if ($out) { 'out of memory' } else { "exit code $code" }

    if ($quietUntil.ContainsKey($name) -and $t -lt $quietUntil[$name]) { Say "died     $name ($reason, restarting in a loop)"; return }

    # Deaths of the last 60 s.
    $recent = @(@($deaths[$name]) | Where-Object { $_ -ne $null -and ($t - $_) -le 60 }) + $t
    $deaths[$name] = $recent
    $count = $recent.Count

    if ($count -ge 3) {
        $deaths[$name] = @()
        $quietUntil[$name] = $t + 600
        Alert 'angry_alert' "$name restarts in a loop" "Died $count times in a minute (last: $reason)$(Get-Where $a). Quiet about it for 10 minutes." "LOOP     $name (died $count times in 60 s, quiet for 10 min)"
    } elseif ($count -eq 1) {
        $title = if ($out) { "$name ran out of memory" } else { "$name died" }
        $label = if ($out) { 'OOM      ' } else { 'DIED     ' }
        $text = $reason.Substring(0, 1).ToUpperInvariant() + $reason.Substring(1)
        Alert 'angry_alert' $title "$text$(Get-Where $a)." "$label$name ($reason)"
    } else {
        Say "died     $name ($reason, again)"
    }
}

Write-Host "Watching $scope. Ctrl-C to stop."
$waiting = $false
while ($true) {
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        if (-not $waiting) { Write-Host "$(Get-Date -Format 'HH:mm:ss')  Docker is not running: waiting for it..." }
        $waiting = $true
        if ($env:MX_DOCKER_EVENTS_ONCE -eq '1') { exit 1 }
        Start-Sleep -Seconds 10
        continue
    }
    $waiting = $false

    foreach ($n in @(& docker ps --filter health=unhealthy --format '{{.Names}}' 2>$null)) { if ($n) { $health[$n] = 'unhealthy' } }
    & docker events --filter type=container --filter event=die --filter event=oom --filter event=kill `
        --filter event=stop --filter event=health_status --format '{{json .}}' 2>$null | ForEach-Object { Invoke-Event $_ }

    # MX_DOCKER_EVENTS_ONCE=1: stop when the stream ends instead of reconnecting (tests).
    if ($env:MX_DOCKER_EVENTS_ONCE -eq '1') { exit 0 }
    Write-Host "$(Get-Date -Format 'HH:mm:ss')  Lost the Docker events stream, reconnecting..."
    Start-Sleep -Seconds 5
}
