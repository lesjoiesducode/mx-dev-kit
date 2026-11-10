# Shows a system notification. The plugin, docker-watch.ps1 and your own scripts use it.
#
#   mx-notify.ps1 <title> <text> [url]      a click on the toast opens the URL
#
# Windows: a native toast (Windows.UI.Notifications), nothing to install. PowerShell 7 hands it
# over to Windows PowerShell, which is the one that can load the toast API.
# A file named notifications.off in the state folder mutes every notification of the kit.
# Untested on Windows: please test before relying on it, issues and PRs welcome.

param(
    [Parameter(Position = 0)][string]$Title,
    [Parameter(Position = 1)][string]$Text,
    [Parameter(Position = 2)][string]$Url
)

if (-not $Title -or -not $Text) {
    Write-Host 'Usage: mx-notify.ps1 <title> <text> [url]'
    exit 2
}

$state = if ($env:APPDATA) { Join-Path $env:APPDATA 'mx-dev-kit' } else { Join-Path $HOME '.config/mx-dev-kit' }
if (Test-Path (Join-Path $state 'notifications.off')) { exit 0 }

# PowerShell on macOS or Linux: the shell version knows what to do there.
if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) {
    & sh (Join-Path $PSScriptRoot 'mx-notify') $Title $Text $Url
    exit $LASTEXITCODE
}

# PowerShell 7 cannot load WinRT types: Windows PowerShell 5.1 ships with every Windows 10 and 11.
if ($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath $Title $Text $Url
    exit $LASTEXITCODE
}

# Toasts need the identity of an installed app; Windows PowerShell's is always there.
$appId = if ($env:MX_NOTIFY_APP_ID) { $env:MX_NOTIFY_APP_ID } else { '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe' }

$escape = { param([string]$s) [Security.SecurityElement]::Escape($s) }
$launch = ''
if ($Url) { $launch = " activationType=`"protocol`" launch=`"$(& $escape $Url)`"" }
$xml = "<toast$launch><visual><binding template=`"ToastGeneric`"><text>$(& $escape $Title)</text><text>$(& $escape $Text)</text></binding></visual></toast>"

try {
    [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
    $doc = New-Object Windows.Data.Xml.Dom.XmlDocument
    $doc.LoadXml($xml)
    $toast = New-Object Windows.UI.Notifications.ToastNotification $doc
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show($toast)
} catch {
    Write-Error "mx-notify: could not show the toast ($($_.Exception.Message))"
    exit 1
}
