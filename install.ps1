# MX Dev Kit installer for Windows. Needs nothing that Windows 10 and 11 do not ship:
# Windows PowerShell 5.1. No git clone, no .NET SDK, no winget.
#
#   irm https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/install.ps1 | iex
#   powershell -ExecutionPolicy Bypass -File .\install.ps1     the same, from the release archive or a clone
#
#   & ([scriptblock]::Create((irm https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/install.ps1))) -Only hooks
#
# It asks about each part, Enter meaning yes: the Actions Ring plugin, the coding agent hooks,
# the PowerShell integration. The haptic command and the tools of bin\ come with either of the
# last two, and so does the offer to install the Haptic Web Plugin, which their vibrations need.
# Without a console to ask, it installs everything.
#
#   -Only <parts>   install these parts without asking: plugin, hooks, shell (-Only hooks,shell)
#   -Yes            install everything without asking
#   -NoHapticWeb    do not install the Haptic Web Plugin, and do not ask about it
#   -FromSource     build the plugin from this clone instead (contributors: .NET 10 SDK, LogiPluginTool)
#   -Link           build and link the plugin from this clone, for development (.NET 10 SDK)
#
# The plugin is MxDevKit.lplug4: the one next to this script (release archive), or the one of the
# latest release. It goes to the plugins folder of Logi Plugin Service, which then loads it.
# The Haptic Web Plugin (github.com/Fallstop/HapticWebPlugin, MIT) comes from its latest release,
# the same way, unless it is already installed: an existing one is never replaced.
# The terminal integration goes to profile.ps1, which every host loads (console, Windows Terminal,
# VS Code), of Windows PowerShell and of PowerShell 7 when it is installed, in Documents wherever
# Windows keeps it (OneDrive included). Once each: a line already there, by hand or from an earlier
# version in a host profile, is not added twice (the earlier block moves to profile.ps1).
# Every file it changes (profiles, agent settings.json) is backed up first.
# %APPDATA%\mx-dev-kit\uninstall.ps1 reverses it.
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param([string[]]$Only, [switch]$Yes, [switch]$NoHapticWeb, [switch]$FromSource, [switch]$Link)

$repo = 'lesjoiesducode/mx-dev-kit'
$release = if ($env:MX_DEV_KIT_RELEASE_URL) { $env:MX_DEV_KIT_RELEASE_URL.TrimEnd('/') } else { "https://github.com/$repo/releases/latest/download" }

# Windows PowerShell 5.1: GitHub needs TLS 1.2, and the progress bar makes downloads very slow.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# One file of the release, checked against its SHA256SUMS. Throws when it cannot.
function Get-ReleaseFile([string]$Name, [string]$OutFile) {
    $progress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "$release/$Name" -OutFile $OutFile
        $sums = (Invoke-WebRequest -UseBasicParsing -Uri "$release/SHA256SUMS").Content
        if ($sums -is [byte[]]) { $sums = [Text.Encoding]::UTF8.GetString($sums) }
    } finally {
        $ProgressPreference = $progress
    }
    $expected = $null
    foreach ($line in ($sums -split "`n")) {
        $parts = $line.Trim() -split '\s+'
        if ($parts.Count -eq 2 -and $parts[1].TrimStart('*') -eq $Name) { $expected = $parts[0] }
    }
    $actual = (Get-FileHash -Algorithm SHA256 -Path $OutFile).Hash
    if (-not $expected -or $actual -ne $expected.ToUpperInvariant()) {
        Remove-Item $OutFile -ErrorAction SilentlyContinue
        throw "${Name}: checksum mismatch, download refused."
    }
}

# Run through irm | iex, this script is alone: get the rest of the kit from the release archive,
# and run its installer in a new Windows PowerShell, whatever the execution policy.
if (-not $PSScriptRoot -or -not (Test-Path (Join-Path $PSScriptRoot 'hooks\install.ps1'))) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("mx-dev-kit-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    try {
        Write-Host "Downloading MX Dev Kit from $release..."
        Get-ReleaseFile 'mx-dev-kit.zip' (Join-Path $tmp 'kit.zip')
        Expand-Archive -Path (Join-Path $tmp 'kit.zip') -DestinationPath $tmp -Force
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $tmp 'mx-dev-kit\install.ps1'))
        foreach ($p in $PSBoundParameters.Keys) {
            if ($p -eq 'Only') { $arguments += '-Only', ($Only -join ',') } else { $arguments += "-$p" }
        }
        # The same PowerShell as the one that runs this (Windows PowerShell or PowerShell 7).
        $exe = (Get-Process -Id $PID).Path
        if ((Split-Path -Leaf $exe) -notmatch '^(powershell|pwsh)(\.exe)?$') {
            $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        }
        & $exe @arguments
    } catch {
        Write-Warning $_.Exception.Message
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    return
}

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hooks\settings-json.ps1')
. (Join-Path $PSScriptRoot 'shell\profiles.ps1')
$root = $PSScriptRoot
$state = Join-Path $env:APPDATA 'mx-dev-kit'
$plugins = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Plugins'
$hapticWebUrl = if ($env:MX_HAPTIC_WEB_URL) { $env:MX_HAPTIC_WEB_URL } else { 'https://github.com/Fallstop/HapticWebPlugin/releases/latest/download/HapticWeb.lplug4' }
$yaml = Join-Path $root 'src\MxDevKitPlugin\package\metadata\LoupedeckPackage.yaml'
if ($Link) { $FromSource = $true }

$version = $null
if (Test-Path (Join-Path $root 'VERSION')) {
    $version = (Get-Content -Raw (Join-Path $root 'VERSION')).Trim()
} elseif (Test-Path $yaml) {
    $version = ((Select-String -Path $yaml -Pattern '^version:\s*(.+)$').Matches[0].Groups[1].Value).Trim()
}

if ($FromSource -and -not (Test-Path (Join-Path $root 'src\MxDevKitPlugin'))) {
    Write-Host "-FromSource and -Link build the plugin from a clone: git clone https://github.com/$repo.git"
    exit 2
}

$parts = @()
foreach ($o in $Only) { $parts += @("$o" -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
foreach ($p in $parts) {
    if ($p -notin 'plugin', 'hooks', 'shell') {
        Write-Host "Unknown part: $p (-Only takes plugin, hooks or shell)"
        exit 2
    }
}

# Read-Host needs a console: none in a scheduled task or with -NonInteractive.
function Test-CanAsk {
    if (-not [Environment]::UserInteractive) { return $false }
    if ([Environment]::GetCommandLineArgs() | Where-Object { $_ -match '^-NonI' }) { return $false }
    try { return -not [Console]::IsInputRedirected } catch { return $true }
}

# The kit used to be named Ring Dev Kit and lived in %APPDATA%\actions-ring-dev-kit: its own
# uninstaller removes it (plugin, profile line, hooks), and the settings move here.
function Move-Legacy {
    $legacy = Join-Path $env:APPDATA 'actions-ring-dev-kit'
    if (-not (Test-Path $legacy)) { return }
    $uninstaller = Join-Path $legacy 'uninstall.ps1'
    if (Test-Path $uninstaller) {
        $exe = (Get-Process -Id $PID).Path
        if ((Split-Path -Leaf $exe) -notmatch '^(powershell|pwsh)(\.exe)?$') { $exe = if ($env:SystemRoot) { Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe' } else { 'pwsh' } }
        try { & $exe -NoProfile -ExecutionPolicy Bypass -File $uninstaller *> $null } catch { }
    }
    New-Item -ItemType Directory -Force -Path $state | Out-Null
    foreach ($f in 'config.json', 'haptics.off', 'notifications.off') {
        $from = Join-Path $legacy $f
        if ((Test-Path $from) -and -not (Test-Path (Join-Path $state $f))) { Move-Item $from $state }
    }
    if (-not (Get-ChildItem $legacy -Force -ErrorAction SilentlyContinue)) { Remove-Item $legacy -ErrorAction SilentlyContinue }
    Write-Host "Replaced Ring Dev Kit, the former name of this kit (your settings moved to $state)."
}

function Get-InstalledVersion {
    $metadata = Join-Path $plugins 'MxDevKit\metadata\LoupedeckPackage.yaml'
    if (-not (Test-Path $metadata)) { return $null }
    $m = Select-String -Path $metadata -Pattern '^version:\s*(.+)$'
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim() }
    return $null
}

# The version written in a .lplug4 (a zip), without unpacking it.
function Get-PackageVersion([string]$Package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entry = $zip.Entries | Where-Object { ($_.FullName -replace '\\', '/') -eq 'metadata/LoupedeckPackage.yaml' } | Select-Object -First 1
        if (-not $entry) { return $null }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($text -match '(?m)^version:\s*(.+?)\s*$') { return $Matches[1] }
        return $null
    } finally {
        $zip.Dispose()
    }
}

# Puts the plugin in place as Logi Plugin Service does (a .lplug4 is a zip of the plugin folder),
# then asks the service to load it. A double-click on the .lplug4 does the same, but only while
# the plugins manager of Logi Options+ is open, and after a confirmation in it.
function Install-Package([string]$Package) {
    $packageVersion = Get-PackageVersion $Package
    if (-not $packageVersion) { Write-Host "$Package is not a Logi plugin package."; return }
    if ((Get-InstalledVersion) -eq $packageVersion) {
        Write-Host "MX Dev Kit $packageVersion is already installed."
        return
    }
    if (-not (Test-Path (Split-Path $plugins))) {
        Write-Host 'Logi Options+ is not installed: install it (https://www.logitech.com/software/logi-options-plus.html), then run this again.'
        return
    }
    $unpack = Join-Path $state 'plugin.new'
    $old = Join-Path $state 'plugin.old'
    $target = Join-Path $plugins 'MxDevKit'
    Remove-Item $unpack, $old -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($Package, $unpack)
    if (-not (Test-Path (Join-Path $unpack 'metadata\LoupedeckPackage.yaml')) -or -not (Test-Path (Join-Path $unpack 'bin\MxDevKitPlugin.dll'))) {
        Remove-Item $unpack -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "$Package is incomplete."
        return
    }
    if (Test-Path $target) {
        # Windows keeps the loaded DLL locked: the official installer knows how to replace it.
        try { Move-Item $target $old } catch {
            Remove-Item $unpack -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "Logi Plugin Service is using the installed version. Open the plugins manager of Logi Options+, then double-click $Package and confirm."
            return
        }
    }
    Move-Item $unpack $target
    Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Installed MX Dev Kit $packageVersion in $target"

    if ((Invoke-PluginLoad 'MxDevKit' $packageVersion) -eq 'loaded') {
        Write-Host 'Logi Plugin Service loaded it.'
    } else {
        Write-Host 'If "MX Dev Kit" does not show in Logi Options+, restart Logi Plugin Service (Options+ settings).'
    }
}

# Asks Logi Plugin Service to load a plugin, then waits for its log to say so: 'loaded', 'refused'
# or 'unknown' when it said nothing in time.
function Invoke-PluginLoad([string]$Name, [string]$PluginVersion) {
    $started = Get-Date -Format 'yyyy-MM-ddTHH-mm-ss'
    try { Start-Process "loupedeck://plugin/$Name/reload" } catch { }
    $log = Join-Path (Split-Path $plugins) "Logs\plugin_logs\$Name.log"
    $want = "Plugin '$Name' version '$PluginVersion' loaded"
    $refused = "Cannot load plugin from '$(Join-Path $plugins $Name)'"
    $wait = if ($env:MX_DEV_KIT_INSTALL_WAIT) { [int]$env:MX_DEV_KIT_INSTALL_WAIT } else { 20 }
    for ($i = 0; $i -lt $wait; $i++) {
        if (Test-Path $log) {
            $result = $null
            foreach ($line in [IO.File]::ReadAllLines($log)) {
                if (($line -split ' ')[0] -lt $started) { continue }
                if ($line.Contains($want) -or $line.Contains('is already loaded')) { $result = 'loaded' }
                elseif ($line.Contains($refused)) { $result = 'refused' }
            }
            if ($result) { return $result }
        }
        Start-Sleep -Seconds 1
    }
    return 'unknown'
}

# The text of metadata/LoupedeckPackage.yaml in a .lplug4 (a zip), without unpacking it.
function Get-PackageMetadata([string]$Package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entry = $zip.Entries | Where-Object { ($_.FullName -replace '\\', '/') -eq 'metadata/LoupedeckPackage.yaml' } | Select-Object -First 1
        if (-not $entry) { return '' }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally {
        $zip.Dispose()
    }
}

# The Haptic Web Plugin, from its latest release. Never replaces one that is already there
# (from the Marketplace or elsewhere). Returns $false, leaving nothing behind, when it is not installed.
function Install-HapticWeb {
    $target = Join-Path $plugins 'HapticWeb'
    if (Test-Path $target) {
        Write-Host "Already installed ($target): left as it is."
        return $true
    }
    if (-not (Test-Path (Split-Path $plugins))) {
        Write-Host 'Logi Options+ is not installed: install it (https://www.logitech.com/software/logi-options-plus.html), then run this again.'
        return $false
    }
    $file = Join-Path $state 'HapticWeb.lplug4.part'
    $unpack = Join-Path $state 'haptic-web.new'
    Remove-Item $file, $unpack -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading HapticWeb.lplug4 from $hapticWebUrl..."
    $progress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $hapticWebUrl -OutFile $file
    } catch {
        Remove-Item $file -ErrorAction SilentlyContinue
        Write-Host "Could not download it ($($_.Exception.Message))."
        return $false
    } finally {
        $ProgressPreference = $progress
    }
    try {
        $metadata = Get-PackageMetadata $file
        $name = if ($metadata -match '(?m)^name:\s*(.+?)\s*$') { $Matches[1] } else { $null }
        $hwVersion = if ($metadata -match '(?m)^version:\s*(.+?)\s*$') { $Matches[1] } else { $null }
        if ($name -ne 'HapticWeb') {
            Write-Host "The download is not the Haptic Web Plugin package (name: $(if ($name) { $name } else { 'none' })): not installed."
            return $false
        }
        New-Item -ItemType Directory -Force -Path $plugins | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($file, $unpack)
        if (-not (Test-Path (Join-Path $unpack 'metadata\LoupedeckPackage.yaml')) -or -not (Test-Path (Join-Path $unpack 'bin'))) {
            Write-Host 'The package is incomplete: not installed.'
            return $false
        }
        Get-ChildItem $unpack -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
        Move-Item $unpack $target
    } catch {
        Write-Host "Not installed: $($_.Exception.Message)"
        return $false
    } finally {
        Remove-Item $file, $unpack -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Installed Haptic Web Plugin $hwVersion in $target"
    switch (Invoke-PluginLoad 'HapticWeb' $hwVersion) {
        'loaded' { Write-Host 'Logi Plugin Service loaded it.' }
        'refused' {
            Write-Host 'Logi Plugin Service refused to load it: removed.'
            Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
            return $false
        }
        default { Write-Host 'Logi Plugin Service has not loaded it yet: if haptic.ps1 completed does not vibrate, restart Logi Plugin Service (Options+ settings).' }
    }
    return $true
}

Write-Host "MX Dev Kit $(if ($version) { $version } else { '(latest release)' }) (Windows support is untested: please report what works and what does not)"
$plugin = $parts -contains 'plugin'
$hooks = $parts -contains 'hooks'
$shell = $parts -contains 'shell'
if (-not $parts) {
    if ($Yes -or -not (Test-CanAsk)) {
        if (-not $Yes) { Write-Host 'No console to ask questions: installing everything (-Only picks parts).' }
        $plugin = $hooks = $shell = $true
    } else {
        $plugin = (Read-Host 'Actions Ring bubbles (Options+ plugin)? [Y/n]') -notmatch '^[nN]'
        $hooks = (Read-Host 'Coding agent haptics (Claude Code, Codex, Gemini, Copilot)? [Y/n]') -notmatch '^[nN]'
        $shell = (Read-Host 'Terminal integration (long commands vibrate, bubbles follow your prompt)? [Y/n]') -notmatch '^[nN]'
    }
}
$tools = $hooks -or $shell
if (-not $plugin -and -not $tools) {
    Write-Host 'Nothing to install.'
    return
}
# The vibrations of the hooks, the shell and the scripts go through the Haptic Web Plugin.
$hapticWeb = $tools -and -not $NoHapticWeb
if ($hapticWeb -and -not $parts -and -not $Yes -and (Test-CanAsk) -and -not (Test-Path (Join-Path $plugins 'HapticWeb')) -and (Test-Path (Split-Path $plugins))) {
    $hapticWeb = (Read-Host 'Haptic Web Plugin by Jasper M-W (MIT license), for these vibrations: install it from github.com/Fallstop/HapticWebPlugin? [Y/n]') -notmatch '^[nN]'
}

Move-Legacy

Write-Host "`n== Files"
Write-Host "In $state"
New-Item -ItemType Directory -Force -Path (Join-Path $state 'hooks') | Out-Null
if ($tools) {
    New-Item -ItemType Directory -Force -Path (Join-Path $state 'bin'), (Join-Path $state 'shell') | Out-Null
    Copy-Item (Join-Path $root 'shell\*') (Join-Path $state 'shell') -Include 'mx.ps1', 'mx.bash' -Force
    Copy-Item (Join-Path $root 'bin\*') (Join-Path $state 'bin') -Force
    Write-Host "haptic, mx-notify, uptime-watch and docker-watch in $state\bin"
}
# The uninstaller stays here, so that it works once this folder is gone.
New-Item -ItemType Directory -Force -Path (Join-Path $state 'shell') | Out-Null
Copy-Item (Join-Path $root 'uninstall.ps1') $state -Force
Copy-Item (Join-Path $root 'shell\profiles.ps1') (Join-Path $state 'shell') -Force
Copy-Item (Join-Path $root 'hooks\uninstall.ps1'), (Join-Path $root 'hooks\settings-json.ps1') (Join-Path $state 'hooks') -Force
Copy-Item (Join-Path $root 'config.example.json') $state -Force
if (Test-Path (Join-Path $state 'config.json')) {
    Write-Host 'Kept your config.json'
} else {
    Copy-Item (Join-Path $root 'config.example.json') (Join-Path $state 'config.json')
    Write-Host "Created $state\config.json"
}
# Files unpacked from a downloaded zip keep its "from the internet" mark, which RemoteSigned blocks.
Get-ChildItem $state -Recurse -File -Include *.ps1 | Unblock-File -ErrorAction SilentlyContinue

if ($shell) {
    Write-Host "`n== PowerShell"
    Install-MxProfiles
    $policy = try { Get-ExecutionPolicy } catch { $null }
    if ($policy -eq 'Restricted' -or $policy -eq 'AllSigned') {
        Write-Host "PowerShell runs no profile script with the execution policy $policy. To let it load the integration:"
        Write-Host '  Set-ExecutionPolicy -Scope CurrentUser RemoteSigned'
    }
}

if ($hooks) {
    Write-Host "`n== Coding agent hooks"
    try { & (Join-Path $root 'hooks\install.ps1') } catch { Write-Warning "Hooks skipped: $($_.Exception.Message)" }
}

if ($plugin) {
    Write-Host "`n== Plugin"
    if ($FromSource) {
        $sdks = & dotnet --list-sdks 2>$null
        if (-not ($sdks -match '^10\.')) {
            Write-Host 'Building from source needs the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10. Without it, run .\install.ps1: it installs the prebuilt plugin.'
            exit 1
        }

        $project = Join-Path $root 'src\MxDevKitPlugin'
        if ($Link) {
            dotnet build $project -c Debug -p:LinkPlugin=true -nologo -v q
            Write-Host 'Linked. Rebuild with: dotnet build src\MxDevKitPlugin -p:LinkPlugin=true'
        } else {
            if (-not (Get-Command logiplugintool -ErrorAction SilentlyContinue)) {
                Write-Host 'Installing LogiPluginTool (the packaging tool of the Logi Actions SDK)...'
                dotnet tool install --global LogiPluginTool
                $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
            }

            dotnet build $project -c Release -nologo -v q
            if ($LASTEXITCODE -ne 0) { exit 1 }
            $dist = Join-Path $root 'dist'
            New-Item -ItemType Directory -Force -Path $dist | Out-Null
            $package = Join-Path $dist 'MxDevKit.lplug4'
            Remove-Item $package -ErrorAction SilentlyContinue
            logiplugintool pack (Join-Path $project 'bin\Release') $package
            logiplugintool verify $package
            logiplugintool install $package
            if ($LASTEXITCODE -ne 0) { Install-Package $package }
        }
    } else {
        if (Test-Path (Join-Path $plugins 'MxDevKitPlugin.link')) {
            Write-Host "Note: a development link (MxDevKitPlugin.link) is in $plugins. Delete it to use the installed plugin."
        }
        # Kept in the state folder: Logi Plugin Service reads it after this script is gone.
        $package = Join-Path $state 'MxDevKit.lplug4'
        $got = $true
        if (Test-Path (Join-Path $root 'MxDevKit.lplug4')) {
            Copy-Item (Join-Path $root 'MxDevKit.lplug4') $package -Force
        } else {
            Write-Host "Downloading MxDevKit.lplug4 from $release..."
            try {
                Get-ReleaseFile 'MxDevKit.lplug4' "$package.part"
                Move-Item "$package.part" $package -Force
            } catch {
                $got = $false
                Write-Host "No plugin installed ($($_.Exception.Message)): download MxDevKit.lplug4 from https://github.com/$repo/releases (see the README)."
            }
        }
        if ($got) {
            Unblock-File $package -ErrorAction SilentlyContinue
            Install-Package $package
        }
    }
}

$hapticWebReady = $false
if ($tools) {
    Write-Host "`n== Haptic Web Plugin"
    if (Test-Path (Join-Path $plugins 'HapticWeb')) {
        Write-Host "Already installed ($(Join-Path $plugins 'HapticWeb')): left as it is."
        $hapticWebReady = $true
    } elseif (-not $hapticWeb) {
        Write-Host 'Not installed (your choice).'
    } else {
        $hapticWebReady = [bool](Install-HapticWeb | Select-Object -Last 1)
    }
}

Write-Host "`n== Next steps"
$n = 0
if ($tools) {
    $n++
    if ($hapticWebReady) {
        Write-Host "$n. MX Master 4: test the vibrations with & `"$state\bin\haptic.ps1`" completed"
    } else {
        Write-Host "$n. MX Master 4: in Logi Options+, open the Marketplace and install the Haptic Web Plugin, for the vibrations."
        Write-Host "   Test it: & `"$state\bin\haptic.ps1`" completed"
    }
}
if ($plugin) {
    $n++
    Write-Host "$n. Logi Options+ > your device > Actions Ring: drag actions from `"MX Dev Kit`" onto the ring."
    Write-Host '   If a bubble shows only its name, edit its icon once and reset it to see the live image.'
}
$n++
Write-Host "$n. Optional tools, each for its own feature: gh (GitHub bubbles: gh auth login), osv-scanner (vulnerabilities"
Write-Host '   for every stack), docker (docker-watch). Nothing breaks without them.'
if ($tools) {
    $n++
    Write-Host "$n. Notifications are native toasts, nothing to install. Test: & `"$state\bin\mx-notify.ps1`" 'MX Dev Kit' 'It works' https://github.com"
}
Write-Host "Uninstall: powershell -ExecutionPolicy Bypass -File `"$state\uninstall.ps1`""
