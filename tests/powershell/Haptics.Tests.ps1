# agent-haptics.ps1, haptic.ps1 and mx.ps1: what vibrates, without DNS, and never waiting for the mouse.

BeforeDiscovery {
    . (Join-Path $PSScriptRoot 'Helpers.ps1')
    $editions = @(Get-Editions | ForEach-Object { @{ Edition = $_ } })
}

BeforeAll {
    . (Join-Path $PSScriptRoot 'Helpers.ps1')
    $hook = Join-Path (Join-Path $Root 'hooks') 'agent-haptics.ps1'
    $haptic = Join-Path (Join-Path $Root 'bin') 'haptic.ps1'
    $mx = Join-Path (Join-Path $Root 'shell') 'mx.ps1'
}

Describe 'Every PowerShell vibration' {
    It '<_> resolves local.jmw.nz itself and goes through curl.exe' -ForEach @('hooks/agent-haptics.ps1', 'bin/haptic.ps1', 'shell/mx.ps1', 'bin/uptime-watch.ps1', 'bin/docker-watch.ps1') {
        $text = Get-Text (Join-Path $Root $_)
        $text | Should -Match ([regex]::Escape('local.jmw.nz:41443:127.0.0.1'))
        $text | Should -Match 'System32\\curl\.exe'
        $text | Should -Not -Match 'Invoke-RestMethod -Method Post'
    }
}

Describe 'agent-haptics.ps1 in <Edition>' -ForEach $editions {
    BeforeEach {
        $h = New-TestHome
        $plugin = Start-Haptic
        $vars = @{ MX_HAPTIC_URL = $plugin.Url }
        $turns = Join-Path $h.State 'turns'
        function Hook([string]$Agent, [string]$HookEvent, [string]$Payload, [hashtable]$More = @{}) {
            $v = $vars.Clone()
            foreach ($k in $More.Keys) { $v[$k] = $More[$k] }
            return Invoke-Ps $Edition $h -File $hook -Arguments $Agent, $HookEvent -Stdin $Payload -Variables $v
        }
    }
    AfterEach {
        Stop-Haptic $plugin
        Remove-Item $h.Dir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'notes the start of a turn and stays silent' {
        $r = Hook claude UserPromptSubmit '{"session_id":"s-1","prompt":"hi"}'
        $r.ExitCode | Should -Be 0
        $r.Out | Should -BeNullOrEmpty
        Test-Path (Join-Path $turns 'claude-s-1') | Should -BeTrue
        Receive-Haptic $plugin 800 | Should -BeNullOrEmpty
    }

    It 'stays silent at the end of a short turn' {
        Hook claude UserPromptSubmit '{"session_id":"s-1"}' | Out-Null
        Hook claude Stop '{"session_id":"s-1"}' | Out-Null
        Receive-Haptic $plugin 1500 | Should -BeNullOrEmpty
    }

    It 'vibrates at the end of a turn of 30 seconds or more' {
        New-Item -ItemType Directory -Force -Path $turns | Out-Null
        [IO.File]::WriteAllText((Join-Path $turns 'claude-s-1'), "$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - 45)")
        Hook claude Stop '{"session_id":"s-1"}' | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/completed'
    }

    It 'vibrates when the start of the turn is unknown' {
        Hook claude Stop '{"session_id":"never-seen"}' | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/completed'
    }

    It 'vibrates at every turn with MX_TURN_SECONDS=0' {
        Hook claude UserPromptSubmit '{"session_id":"s-1"}' | Out-Null
        Hook claude Stop '{"session_id":"s-1"}' @{ MX_TURN_SECONDS = '0' } | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/completed'
    }

    It 'always knocks for a permission, even right after the start' {
        Hook claude UserPromptSubmit '{"session_id":"s-1"}' | Out-Null
        Hook claude Notification '{"session_id":"s-1","notification_type":"permission_prompt"}' | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/knock'
    }

    It 'always plays angry_alert for a failed turn' {
        Hook claude UserPromptSubmit '{"session_id":"s-1"}' | Out-Null
        Hook claude StopFailure '{"session_id":"s-1"}' | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/angry_alert'
    }

    It 'follows Copilot turns by their folder' {
        Hook copilot userPromptSubmitted '{"timestamp":1,"cwd":"C:\\work\\api","prompt":"x"}' | Out-Null
        Hook copilot agentStop '{"timestamp":2,"cwd":"C:\\work\\api"}' | Out-Null
        Receive-Haptic $plugin 1500 | Should -BeNullOrEmpty
        Hook copilot agentStop '{"timestamp":2,"cwd":"C:\\work\\web"}' | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/completed'
    }

    It 'gives Gemini CLI its JSON object, at the start and the end of a turn' {
        (Hook gemini BeforeAgent '{"session_id":"g"}').Out.Trim() | Should -Be '{}'
        (Hook gemini AfterAgent '{"session_id":"g"}').Out.Trim() | Should -Be '{}'
    }

    It 'stays silent for headless Claude Code runs and with haptics.off' {
        Hook claude Stop '{"session_id":"x"}' @{ CLAUDE_CODE_ENTRYPOINT = 'sdk-cli' } | Out-Null
        Receive-Haptic $plugin 1000 | Should -BeNullOrEmpty
        New-Item -ItemType File -Force -Path (Join-Path $h.State 'haptics.off') | Out-Null
        Hook claude Notification '{"notification_type":"permission_prompt"}' | Out-Null
        Receive-Haptic $plugin 1000 | Should -BeNullOrEmpty
    }

    It 'returns before the Haptic Web Plugin answers' {
        $run = Start-Ps -Edition $Edition -TestHome $h -File $hook -Arguments 'claude', 'StopFailure' -Stdin '{}' -Variables $vars
        Receive-Haptic $plugin -NoAnswer | Should -Be '/haptic/angry_alert'
        $r = Wait-Ps $run 10
        $r.ExitCode | Should -Be 0
        # curl waits 2 seconds for an answer that never comes; the hook does not.
        $r.Seconds | Should -BeLessThan 4
    }
}

Describe 'haptic.ps1 in <Edition>' -ForEach $editions {
    BeforeEach {
        $h = New-TestHome
        $plugin = Start-Haptic
    }
    AfterEach {
        Stop-Haptic $plugin
        Remove-Item $h.Dir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'plays a waveform and says nothing' {
        $run = Start-Ps -Edition $Edition -TestHome $h -File $haptic -Arguments 'completed' -Variables @{ MX_HAPTIC_URL = $plugin.Url }
        Receive-Haptic $plugin | Should -Be '/haptic/completed'
        $r = Wait-Ps $run
        $r.ExitCode | Should -Be 0
        $r.Err | Should -BeNullOrEmpty
    }

    It 'says when the Haptic Web Plugin does not answer' {
        $port = $plugin.Listener.LocalEndpoint.Port
        Stop-Haptic $plugin
        $r = Invoke-Ps $Edition $h -File $haptic -Arguments 'completed' -Variables @{ MX_HAPTIC_URL = "http://127.0.0.1:$port/haptic" }
        $r.ExitCode | Should -Be 1
        $r.Err | Should -Match 'unreachable'
        $plugin = Start-Haptic
    }

    It 'shows its usage without a waveform' {
        (Invoke-Ps $Edition $h -File $haptic).ExitCode | Should -Be 2
    }

    It 'is silent with haptics.off' {
        New-Item -ItemType Directory -Force -Path $h.State | Out-Null
        New-Item -ItemType File -Path (Join-Path $h.State 'haptics.off') | Out-Null
        (Invoke-Ps $Edition $h -File $haptic -Arguments 'completed' -Variables @{ MX_HAPTIC_URL = $plugin.Url }).ExitCode | Should -Be 0
        Receive-Haptic $plugin 800 | Should -BeNullOrEmpty
    }
}

Describe 'mx.ps1 in <Edition>' -ForEach $editions {
    BeforeEach {
        $h = New-TestHome
        $plugin = Start-Haptic
        $vars = @{ MX_HAPTIC_URL = $plugin.Url }
        # A session that loads the integration as a profile does, then runs these lines.
        function Session([string]$Lines, [hashtable]$More = @{}) {
            $v = $vars.Clone()
            foreach ($k in $More.Keys) { $v[$k] = $More[$k] }
            return Invoke-Ps $Edition $h -Command ". '$mx'; $Lines" -Variables $v
        }
        # The last command of the session, as Get-History gives it to the prompt.
        function New-HistoryLine([string]$CommandLine, [int]$Seconds) {
            return "function global:Get-History { [pscustomobject]@{ Id = 7; CommandLine = '$CommandLine'; StartExecutionTime = (Get-Date).AddSeconds(-$Seconds); EndExecutionTime = (Get-Date) } }"
        }
    }
    AfterEach {
        Stop-Haptic $plugin
        Remove-Item $h.Dir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'writes the folder and the terminal at each prompt' {
        $r = Session 'prompt | Out-Null'
        $r.ExitCode | Should -Be 0
        # The folder of the session (on macOS, /var is a link to /private/var).
        Get-Text (Join-Path $h.State 'current-dir') | Should -Match ([regex]::Escape((Split-Path -Leaf $h.Dir)) + '$')
        $shell = if ($Edition -eq 'pwsh') { 'pwsh' } else { 'powershell' }
        Get-Text (Join-Path $h.State 'current-terminal') | Should -Be "console`t$shell"
        Session 'prompt | Out-Null' @{ WT_SESSION = 'abc' } | Out-Null
        Get-Text (Join-Path $h.State 'current-terminal') | Should -Be "WindowsTerminal`t$shell"
        Session 'prompt | Out-Null' @{ WT_SESSION = 'abc'; TERM_PROGRAM = 'vscode' } | Out-Null
        Get-Text (Join-Path $h.State 'current-terminal') | Should -Be "vscode`t$shell"
    }

    It 'vibrates when a long command ends' {
        $r = Session "$(New-HistoryLine 'npm run build' 40); prompt | Out-Null"
        Receive-Haptic $plugin | Should -Be '/haptic/completed' -Because ($r.Out + $r.Err)
    }

    It 'stays silent for a short command and an ignored one' {
        Session "$(New-HistoryLine 'npm test' 3); prompt | Out-Null" | Out-Null
        Receive-Haptic $plugin 1000 | Should -BeNullOrEmpty
        Session "$(New-HistoryLine 'vim notes.md' 300); prompt | Out-Null" | Out-Null
        Receive-Haptic $plugin 1000 | Should -BeNullOrEmpty
    }

    It 'buzz vibrates with the result of the command' {
        $ok = if ($OnWindows) { 'cmd /c exit 0' } else { 'sh -c "exit 0"' }
        $ko = if ($OnWindows) { 'cmd /c exit 3' } else { 'sh -c "exit 3"' }
        Session "buzz $ok" | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/completed'
        Session "buzz $ko" | Out-Null
        Receive-Haptic $plugin | Should -Be '/haptic/angry_alert'
    }

    It 'never makes the prompt wait for the Haptic Web Plugin' {
        $run = Start-Ps -Edition $Edition -TestHome $h -Command ". '$mx'; `$t = [Diagnostics.Stopwatch]::StartNew(); Invoke-MxHaptic completed; [int]`$t.ElapsedMilliseconds" -Variables $vars
        Receive-Haptic $plugin -NoAnswer | Should -Be '/haptic/completed'
        $r = Wait-Ps $run 10
        [int]($r.Out.Trim() -split "`n")[-1] | Should -BeLessThan 1000
    }
}
