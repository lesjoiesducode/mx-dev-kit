# Shared by the Pester tests of the PowerShell scripts. Every script runs in a child process, with
# its own APPDATA, LOCALAPPDATA, Documents and agent folders, in each PowerShell installed:
# Windows PowerShell 5.1 and PowerShell 7 on Windows, PowerShell 7 elsewhere (a quicker check).
# Vibrations go to a local TCP listener that stands in for the Haptic Web Plugin.

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$OnWindows = $env:OS -eq 'Windows_NT'

function Get-Editions {
    $editions = @()
    if ($OnWindows) { $editions += 'powershell' }
    if (Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue) { $editions += 'pwsh' }
    return $editions
}

# A fresh home for one test: what the scripts read from the environment.
function New-TestHome {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('mx-pester-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    $h = [ordered]@{
        Dir = $dir
        AppData = Join-Path (Join-Path $dir 'AppData') 'Roaming'
        LocalAppData = Join-Path (Join-Path $dir 'AppData') 'Local'
        Documents = Join-Path $dir 'Documents'
        Claude = Join-Path $dir '.claude'
        Codex = Join-Path $dir '.codex'
        Copilot = Join-Path $dir '.copilot'
    }
    foreach ($k in 'AppData', 'LocalAppData', 'Documents') { New-Item -ItemType Directory -Force -Path $h[$k] | Out-Null }
    $h.State = Join-Path $h.AppData 'mx-dev-kit'
    return [pscustomobject]$h
}

function Get-TestEnvironment($TestHome, [hashtable]$Extra = @{}) {
    $vars = @{
        APPDATA = $TestHome.AppData
        LOCALAPPDATA = $TestHome.LocalAppData
        MX_DEV_KIT_DOCUMENTS = $TestHome.Documents
        CLAUDE_CONFIG_DIR = $TestHome.Claude
        CODEX_HOME = $TestHome.Codex
        COPILOT_HOME = $TestHome.Copilot
        # Gemini CLI has no other way to move ~/.gemini: never the real one in a test.
        GEMINI_CLI_HOME = $TestHome.Dir
        CLAUDE_CODE_ENTRYPOINT = $null
        MX_HAPTIC_URL = $null
        MX_TURN_SECONDS = $null
        WT_SESSION = $null
        TERM_PROGRAM = $null
        MX_DEV_KIT_INSTALL_WAIT = '0'
    }
    foreach ($k in $Extra.Keys) { $vars[$k] = $Extra[$k] }
    return $vars
}

# Starts a PowerShell child. -Command for a script block as text, -File for a script and its arguments.
function Start-Ps([string]$Edition, $TestHome, [string]$File, [string[]]$Arguments = @(), [string]$Command, [hashtable]$Variables = @{}, [string]$Stdin, [string]$WorkingDirectory) {
    $exe = if ($Edition -eq 'powershell') { Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe' } else { (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source }
    $psi = New-Object Diagnostics.ProcessStartInfo $exe
    $list = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass')
    if ($Command) { $list += '-Command', $Command } else { $list += @('-File', $File) + $Arguments }
    foreach ($a in $list) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.WorkingDirectory = if ($WorkingDirectory) { $WorkingDirectory } else { $TestHome.Dir }
    $vars = Get-TestEnvironment $TestHome $Variables
    # Windows PowerShell started from PowerShell 7 would inherit its module path and load its modules.
    if ($Edition -eq 'powershell' -and -not $Variables.ContainsKey('PSModulePath')) { $vars['PSModulePath'] = $null }
    foreach ($k in $vars.Keys) {
        if ($null -eq $vars[$k]) { [void]$psi.Environment.Remove($k) } else { $psi.Environment[$k] = $vars[$k] }
    }
    $p = [Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    if ($Stdin) { $p.StandardInput.Write($Stdin) }
    $p.StandardInput.Close()
    return [pscustomobject]@{ Process = $p; Out = $out; Err = $err; Started = [DateTime]::UtcNow }
}

function Wait-Ps($Run, [int]$TimeoutSeconds = 120) {
    if (-not $Run.Process.WaitForExit($TimeoutSeconds * 1000)) { $Run.Process.Kill(); throw 'timed out' }
    $Run.Process.WaitForExit()
    return [pscustomobject]@{
        ExitCode = $Run.Process.ExitCode
        Out = $Run.Out.Result
        Err = $Run.Err.Result
        Seconds = ([DateTime]::UtcNow - $Run.Started).TotalSeconds
    }
}

function Invoke-Ps([string]$Edition, $TestHome, [string]$File, [string[]]$Arguments = @(), [string]$Command, [hashtable]$Variables = @{}, [string]$Stdin) {
    return Wait-Ps (Start-Ps -Edition $Edition -TestHome $TestHome -File $File -Arguments $Arguments -Command $Command -Variables $Variables -Stdin $Stdin)
}

# A stand-in for the Haptic Web Plugin on 127.0.0.1, on a free port.
function Start-Haptic {
    $listener = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    return [pscustomobject]@{ Listener = $listener; Url = "http://127.0.0.1:$($listener.LocalEndpoint.Port)/haptic" }
}

# The path of the next request (/haptic/completed), or $null when none comes in time. -NoAnswer
# keeps the connection open without answering, as a plugin that hangs.
function Receive-Haptic($Haptic, [int]$TimeoutMs = 4000, [switch]$NoAnswer) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while (-not $Haptic.Listener.Pending()) {
        if ([DateTime]::UtcNow -gt $deadline) { return $null }
        Start-Sleep -Milliseconds 20
    }
    $client = $Haptic.Listener.AcceptTcpClient()
    $stream = $client.GetStream()
    $stream.ReadTimeout = 3000
    $reader = New-Object IO.StreamReader($stream)
    $line = $reader.ReadLine()
    if ($NoAnswer) { $Haptic | Add-Member -Force -NotePropertyName Held -NotePropertyValue $client; return ($line -split ' ')[1] }
    while (($h = $reader.ReadLine()) -and $h -ne '') { }
    $reply = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
    $stream.Write($reply, 0, $reply.Length)
    $client.Close()
    return ($line -split ' ')[1]
}

function Stop-Haptic($Haptic) {
    if ($Haptic.PSObject.Properties['Held']) { $Haptic.Held.Close() }
    $Haptic.Listener.Stop()
}

function Get-Text([string]$Path) { return [IO.File]::ReadAllText($Path) }

function Get-MarkerCount([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    return ([regex]::Matches((Get-Text $Path), [regex]::Escape('# >>> mx-dev-kit >>>'))).Count
}
