# Helpers shared by hooks\install.ps1 and hooks\uninstall.ps1.
# Works with Windows PowerShell 5.1 (PSCustomObject from ConvertFrom-Json) and PowerShell 7.

# Windows PowerShell 5.1 adds a Count property to arrays, which ConvertTo-Json can write out as
# {"value": [...], "Count": n}. Dropping that type data gives plain arrays, as in PowerShell 7.
if ($PSVersionTable.PSEdition -ne 'Core') { Remove-TypeData -TypeName System.Array -ErrorAction SilentlyContinue }

# Read as UTF-8: Windows PowerShell 5.1 would read a file without BOM as ANSI and garble accents.
function Read-Utf8([string]$Path) {
    return [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).ProviderPath, [Text.Encoding]::UTF8)
}

function Read-SettingsJson([string]$Path) {
    if ((Test-Path $Path) -and (Get-Item $Path).Length -gt 0) {
        return (Read-Utf8 $Path | ConvertFrom-Json)
    }
    return [pscustomobject]@{}
}

function Get-EventHooks($Settings, [string]$EventName) {
    if (-not $Settings.PSObject.Properties['hooks']) { return @() }
    $entry = $Settings.hooks.PSObject.Properties[$EventName]
    if (-not $entry) { return @() }
    return @($entry.Value)
}

function Set-EventHooks($Settings, [string]$EventName, $Hooks) {
    if (-not $Settings.PSObject.Properties['hooks']) {
        $Settings | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{})
    }
    if ($Settings.hooks.PSObject.Properties[$EventName]) {
        $Settings.hooks.PSObject.Properties.Remove($EventName)
    }
    if (@($Hooks).Count -gt 0) {
        $Settings.hooks | Add-Member -NotePropertyName $EventName -NotePropertyValue @($Hooks)
    }
}

# Matcher groups without any agent-haptics command; groups left empty are dropped.
function Remove-OurHooks($Groups) {
    $kept = @()
    foreach ($group in @($Groups)) {
        if ($null -eq $group) { continue }
        $inner = @(@($group.hooks) | Where-Object { $_ -and -not ("$($_.command)" -like '*agent-haptics*') })
        if ($inner.Count -gt 0) {
            $copy = $group.PSObject.Copy()
            $copy.hooks = $inner
            $kept += $copy
        }
    }
    return , $kept
}

# settings.json.bak-20261110-093000, then -2, -3... if run twice in the same second.
function Get-BackupName([string]$Path) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $name = "$Path.bak-$stamp"
    $n = 1
    while (Test-Path $name) { $n++; $name = "$Path.bak-$stamp-$n" }
    return $name
}

# Returns $false when the file already holds exactly these settings.
function Save-SettingsJson($Settings, [string]$Path, [string]$Name) {
    $json = ConvertTo-Json $Settings -Depth 64
    if ((Test-Path $Path) -and ((Read-Utf8 $Path).Trim() -eq $json.Trim())) { return $false }

    New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    if (Test-Path $Path) {
        $backup = Get-BackupName $Path
        Copy-Item $Path $backup
        Write-Host "${Name}: backup saved to $backup"
    }
    # UTF-8 without BOM: Windows PowerShell 5.1 would otherwise add one.
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding $false))
    return $true
}
