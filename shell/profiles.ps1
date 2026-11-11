# Helpers for install.ps1 and uninstall.ps1: the PowerShell profiles that load shell\mx.ps1.
# Works with Windows PowerShell 5.1 and PowerShell 7.
#
# The line goes to profile.ps1 (CurrentUserAllHosts), which every host loads: the console, Windows
# Terminal, the terminal of VS Code. One per edition: Windows PowerShell 5.1, and PowerShell 7 when it
# is installed. Versions before 1.2 wrote it to the profile of one host ($PROFILE): it moves.

$MxMarker = '# >>> mx-dev-kit >>>'
$MxMarkerEnd = '# <<< mx-dev-kit <<<'
# $env:APPDATA unexpanded: the line stays ASCII whatever the encoding of the profile.
$MxProfileLine = '. "$env:APPDATA\mx-dev-kit\shell\mx.ps1"'

# Documents comes from Windows, so a Documents folder moved to OneDrive is found.
# MX_DEV_KIT_DOCUMENTS stands in for it in the tests.
function Get-MxDocuments {
    if ($env:MX_DEV_KIT_DOCUMENTS) { return $env:MX_DEV_KIT_DOCUMENTS }
    return [Environment]::GetFolderPath('MyDocuments')
}

# The profile folders of the editions present; -All for both, whatever is installed (uninstall).
function Get-MxProfileFolders([switch]$All) {
    $docs = Get-MxDocuments
    $folders = @([pscustomobject]@{ Name = 'Windows PowerShell'; Path = (Join-Path $docs 'WindowsPowerShell') })
    $pwsh = $PSVersionTable.PSEdition -eq 'Core' -or [bool](Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue)
    if ($All -or $pwsh) { $folders += [pscustomobject]@{ Name = 'PowerShell 7'; Path = (Join-Path $docs 'PowerShell') } }
    return $folders
}

# profile.ps1 and the profiles of each host (Microsoft.PowerShell_profile.ps1, Microsoft.VSCode_profile.ps1...).
function Get-MxProfileFiles([string]$Folder) {
    if (-not (Test-Path -LiteralPath $Folder)) { return @() }
    return @(Get-ChildItem -LiteralPath $Folder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'profile.ps1' -or $_.Name -like '*_profile.ps1' } | ForEach-Object { $_.FullName })
}

function Test-MxBlock([string]$Path) {
    return (Test-Path -LiteralPath $Path) -and [bool](Select-String -LiteralPath $Path -SimpleMatch $MxMarker -Quiet)
}

# The text of a profile, its encoding (the byte order mark, or the one PowerShell reads a file without it).
function Read-MxProfile([string]$Path) {
    $reader = New-Object IO.StreamReader((Resolve-Path -LiteralPath $Path).ProviderPath, [Text.Encoding]::Default, $true)
    try { $text = $reader.ReadToEnd(); $encoding = $reader.CurrentEncoding } finally { $reader.Dispose() }
    return [pscustomobject]@{ Text = $text; Encoding = $encoding }
}

# The text without the kit's block and the empty line the installer put before it.
function Remove-MxBlockText([string]$Text) {
    $kept = New-Object Collections.Generic.List[string]
    $inside = $false
    $blank = $false
    foreach ($line in ($Text -split "`r?`n")) {
        if ($inside) { if ($line -eq $MxMarkerEnd) { $inside = $false }; continue }
        if ($line -eq $MxMarker) { $inside = $true; $blank = $false; continue }
        if ($blank) { $kept.Add(''); $blank = $false }
        if ($line -eq '') { $blank = $true; continue }
        $kept.Add($line)
    }
    $newline = if ($Text -match "`r`n") { "`r`n" } else { "`n" }
    $result = ($kept -join $newline)
    if ($kept.Count -gt 0 -and $Text.EndsWith("`n")) { $result += $newline }
    return $result
}

# Takes the block out of a profile, with a backup, in the encoding it had. Returns the backup.
function Remove-MxBlock([string]$Path) {
    $backup = Get-BackupName $Path
    Copy-Item -LiteralPath $Path $backup
    $content = Read-MxProfile $backup
    [IO.File]::WriteAllText((Resolve-Path -LiteralPath $Path).ProviderPath, (Remove-MxBlockText $content.Text), $content.Encoding)
    return $backup
}

# A profile of this folder that loads mx.ps1 outside the kit's block: a line added by hand.
function Find-MxHandLine([string]$Folder) {
    foreach ($file in Get-MxProfileFiles $Folder) {
        $text = Remove-MxBlockText (Read-MxProfile $file).Text
        if ($text -match 'mx-dev-kit[\\/]+shell[\\/]+mx\.ps1') { return $file }
    }
    return $null
}

# install.ps1: the block in profile.ps1 of each edition present, once. Moves it out of the host profiles.
function Install-MxProfiles {
    foreach ($edition in Get-MxProfileFolders) {
        $target = Join-Path $edition.Path 'profile.ps1'
        foreach ($old in Get-MxProfileFiles $edition.Path) {
            if ($old -ne $target -and (Test-MxBlock $old)) {
                $backup = Remove-MxBlock $old
                Write-Host "$($edition.Name): moved from $old to profile.ps1 (backup: $backup)"
            }
        }

        $hand = Find-MxHandLine $edition.Path
        if (Test-MxBlock $target) {
            Write-Host "$($edition.Name): already in $target"
        } elseif ($hand) {
            Write-Host "$($edition.Name): $hand already loads it, left as it is"
        } else {
            New-Item -ItemType Directory -Force -Path $edition.Path | Out-Null
            if (Test-Path -LiteralPath $target) {
                $backup = Get-BackupName $target
                Copy-Item -LiteralPath $target $backup
                Write-Host "Backup: $backup"
            }
            Add-Content -LiteralPath $target -Value "`n$MxMarker`n$MxProfileLine`n$MxMarkerEnd"
            Write-Host "$($edition.Name): added to $target (open a new terminal to use it)"
        }
    }
}

# uninstall.ps1: the block out of every profile of both editions, and of $PROFILE.
function Uninstall-MxProfiles {
    $files = @()
    foreach ($edition in Get-MxProfileFolders -All) { $files += Get-MxProfileFiles $edition.Path }
    if ($PROFILE -and -not $env:MX_DEV_KIT_DOCUMENTS) { $files += "$PROFILE" }
    foreach ($file in ($files | Select-Object -Unique)) {
        if (Test-MxBlock $file) {
            $backup = Remove-MxBlock $file
            Write-Host "Removed from $file (backup: $backup)"
        }
    }
}
