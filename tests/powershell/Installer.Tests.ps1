# install.ps1 and uninstall.ps1: the PowerShell profiles, the files, the hooks.

BeforeDiscovery {
    . (Join-Path $PSScriptRoot 'Helpers.ps1')
    $editions = @(Get-Editions | ForEach-Object { @{ Edition = $_ } })
}

BeforeAll {
    . (Join-Path $PSScriptRoot 'Helpers.ps1')
    $install = Join-Path $Root 'install.ps1'
    $line = '. "$env:APPDATA\mx-dev-kit\shell\mx.ps1"'
}

Describe 'install.ps1 run from <Edition>' -ForEach $editions {
    BeforeEach {
        $h = New-TestHome
        $wps = Join-Path (Join-Path $h.Documents 'WindowsPowerShell') 'profile.ps1'
        $ps7 = Join-Path (Join-Path $h.Documents 'PowerShell') 'profile.ps1'
    }
    AfterEach { Remove-Item $h.Dir -Recurse -Force -ErrorAction SilentlyContinue }

    It 'writes profile.ps1 of both editions, once' {
        $r = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb'
        $r.ExitCode | Should -Be 0 -Because ($r.Out + $r.Err)
        Get-MarkerCount $wps | Should -Be 1
        Get-MarkerCount $ps7 | Should -Be 1
        (Get-Text $wps) | Should -Match ([regex]::Escape($line))
        $r.Out | Should -Match 'Windows PowerShell: added to'
        $r.Out | Should -Match 'PowerShell 7: added to'

        $again = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb'
        $again.Out | Should -Match 'already in'
        Get-MarkerCount $wps | Should -Be 1
        Get-MarkerCount $ps7 | Should -Be 1
    }

    It 'moves the block an earlier version wrote to the host profile' {
        $old = Join-Path (Join-Path $h.Documents 'WindowsPowerShell') 'Microsoft.PowerShell_profile.ps1'
        New-Item -ItemType Directory -Force -Path (Split-Path $old) | Out-Null
        [IO.File]::WriteAllText($old, "# mine`r`nSet-Alias g git`r`n`r`n# >>> mx-dev-kit >>>`r`n$line`r`n# <<< mx-dev-kit <<<`r`n")

        $r = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb'
        $r.ExitCode | Should -Be 0 -Because ($r.Out + $r.Err)
        $r.Out | Should -Match 'moved from'
        Get-Text $old | Should -Be "# mine`r`nSet-Alias g git`r`n"
        @(Get-ChildItem (Split-Path $old) -Filter 'Microsoft.PowerShell_profile.ps1.bak-*').Count | Should -Be 1
        Get-MarkerCount $wps | Should -Be 1
    }

    It 'adds nothing where a profile already loads it by hand' {
        $hand = Join-Path (Join-Path $h.Documents 'PowerShell') 'Microsoft.PowerShell_profile.ps1'
        New-Item -ItemType Directory -Force -Path (Split-Path $hand) | Out-Null
        [IO.File]::WriteAllText($hand, ". `"`$env:APPDATA/mx-dev-kit/shell/mx.ps1`"`n")

        $r = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb'
        $r.Out | Should -Match 'already loads it'
        Test-Path $ps7 | Should -BeFalse
        Get-MarkerCount $wps | Should -Be 1
        Get-Text $hand | Should -Be ". `"`$env:APPDATA/mx-dev-kit/shell/mx.ps1`"`n"
    }

    It 'appends to an existing profile.ps1 with a backup, keeping its text and encoding' {
        New-Item -ItemType Directory -Force -Path (Split-Path $wps) | Out-Null
        [IO.File]::WriteAllText($wps, "# café`r`n", (New-Object Text.UTF8Encoding $true))

        $r = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb'
        $r.ExitCode | Should -Be 0 -Because ($r.Out + $r.Err)
        $bytes = [IO.File]::ReadAllBytes($wps)
        $bytes[0..2] | Should -Be @(0xEF, 0xBB, 0xBF)
        [Text.Encoding]::UTF8.GetString($bytes) | Should -Match '# café'
        Get-MarkerCount $wps | Should -Be 1
        @(Get-ChildItem (Split-Path $wps) -Filter 'profile.ps1.bak-*').Count | Should -Be 1
    }

    It 'leaves its uninstaller and what it needs in APPDATA' {
        $r = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb'
        $r.ExitCode | Should -Be 0 -Because ($r.Out + $r.Err)
        foreach ($f in 'uninstall.ps1', 'shell\profiles.ps1', 'shell\mx.ps1', 'hooks\uninstall.ps1', 'hooks\settings-json.ps1', 'bin\haptic.ps1', 'config.json') {
            Test-Path (Join-Path $h.State $f) | Should -BeTrue -Because $f
        }
    }

    It 'is undone by the uninstaller it leaves, profiles as they were' {
        New-Item -ItemType Directory -Force -Path (Split-Path $wps) | Out-Null
        [IO.File]::WriteAllText($wps, "# mine`r`n")
        Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell,hooks', '-NoHapticWeb' | Out-Null

        $r = Invoke-Ps $Edition $h -File (Join-Path $h.State 'uninstall.ps1')
        $r.ExitCode | Should -Be 0 -Because ($r.Out + $r.Err)
        Get-Text $wps | Should -Be "# mine`r`n"
        Get-MarkerCount $ps7 | Should -Be 0
        Test-Path (Join-Path $h.State 'shell') | Should -BeFalse
        Test-Path (Join-Path $h.State 'config.json') | Should -BeTrue

        Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb' | Out-Null
        $purge = Invoke-Ps $Edition $h -File (Join-Path $h.State 'uninstall.ps1') -Arguments '-Purge'
        $purge.ExitCode | Should -Be 0
        Test-Path $h.State | Should -BeFalse
    }

    It 'adds the hooks of every agent found, start of turn included, once' {
        New-Item -ItemType Directory -Force -Path $h.Claude, $h.Codex, $h.Copilot | Out-Null
        [IO.File]::WriteAllText((Join-Path $h.Claude 'settings.json'), '{"model":"opus","hooks":{"Stop":[{"hooks":[{"type":"command","command":"true"}]}]}}')

        $r = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'hooks', '-NoHapticWeb'
        $r.ExitCode | Should -Be 0 -Because ($r.Out + $r.Err)
        $claude = Get-Content -Raw (Join-Path $h.Claude 'settings.json') | ConvertFrom-Json
        $claude.model | Should -Be 'opus'
        @($claude.hooks.Stop).Count | Should -Be 2
        foreach ($e in 'UserPromptSubmit', 'Stop', 'Notification', 'StopFailure') {
            ($claude.hooks.$e.hooks.command -join ' ') | Should -Match "agent-haptics\.ps1`" claude $e" -Because $e
        }
        $codex = Get-Content -Raw (Join-Path $h.Codex 'hooks.json') | ConvertFrom-Json
        @($codex.hooks.PSObject.Properties.Name) | Should -Be @('UserPromptSubmit', 'Stop', 'PermissionRequest')
        $copilot = Get-Content -Raw (Join-Path $h.Copilot 'hooks\mx-dev-kit.json') | ConvertFrom-Json
        @($copilot.hooks.PSObject.Properties.Name) | Should -Be @('userPromptSubmitted', 'agentStop', 'notification', 'errorOccurred')
        $copilot.hooks.agentStop[0].powershell | Should -Match 'copilot agentStop$'

        $again = Invoke-Ps $Edition $h -File $install -Arguments '-Only', 'hooks', '-NoHapticWeb'
        $again.Out | Should -Match 'Claude Code: hooks already up to date'
        $again.Out | Should -Match 'Copilot CLI: hooks already up to date'

        Invoke-Ps $Edition $h -File (Join-Path $h.State 'uninstall.ps1') | Out-Null
        $after = Get-Content -Raw (Join-Path $h.Claude 'settings.json') | ConvertFrom-Json
        @($after.hooks.Stop).Count | Should -Be 1
        $after.hooks.PSObject.Properties['UserPromptSubmit'] | Should -BeNullOrEmpty
        Test-Path (Join-Path $h.Copilot 'hooks\mx-dev-kit.json') | Should -BeFalse
    }
}

Describe 'install.ps1 without PowerShell 7' -Skip:(-not $OnWindows) {
    BeforeAll {
        $h = New-TestHome
        # Windows PowerShell with a PATH that has no pwsh.
        $path = ($env:PATH -split ';' | Where-Object { $_ -and -not (Test-Path (Join-Path $_ 'pwsh.exe')) }) -join ';'
        $r = Invoke-Ps 'powershell' $h -File $install -Arguments '-Only', 'shell', '-NoHapticWeb' -Variables @{ PATH = $path }
    }
    AfterAll { Remove-Item $h.Dir -Recurse -Force -ErrorAction SilentlyContinue }

    It 'writes only the profile of Windows PowerShell' {
        $r.ExitCode | Should -Be 0 -Because ($r.Out + $r.Err)
        Get-MarkerCount (Join-Path (Join-Path $h.Documents 'WindowsPowerShell') 'profile.ps1') | Should -Be 1
        Test-Path (Join-Path $h.Documents 'PowerShell') | Should -BeFalse
    }
}
