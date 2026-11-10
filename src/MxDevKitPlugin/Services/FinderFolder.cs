namespace Loupedeck.MxDevKitPlugin;

// macOS: folder of the front Finder window, while the Finder is the active app.
// The first call makes macOS ask whether Logi Plugin Service may control the Finder.
internal sealed class FinderFolder
{
    private const string LsAppInfo = "/usr/bin/lsappinfo";
    private const string OsaScript = "/usr/bin/osascript";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    private DateTime _pausedUntil = DateTime.MinValue;
    private bool _deniedLogged;

    public bool IsFront()
    {
        if (!OperatingSystem.IsMacOS() || DateTime.UtcNow < _pausedUntil)
        {
            return false;
        }

        var asn = ProcessRunner.Run(LsAppInfo, null, Timeout, new[] { "front" })?.Stdout.Trim();
        if (string.IsNullOrEmpty(asn))
        {
            return false;
        }

        var info = ProcessRunner.Run(LsAppInfo, null, Timeout, new[] { "info", "-only", "bundleid", asn })?.Stdout;
        return info != null && info.Contains("\"com.apple.finder\"", StringComparison.Ordinal);
    }

    public string? FrontWindowFolder()
    {
        var result = ProcessRunner.Run(OsaScript, null, Timeout, new[]
        {
            "-e", "tell application \"Finder\"",
            "-e", "if (count of Finder windows) is 0 then return \"\"",
            "-e", "try",
            "-e", "return POSIX path of (target of front Finder window as alias)",
            "-e", "on error number n",
            "-e", "if n is -1743 then return \"DENIED\"",
            "-e", "return \"\"",
            "-e", "end try",
            "-e", "end tell",
        });

        var path = result?.Stdout.Trim();
        if (path == null || path == "DENIED")
        {
            // Not allowed (or osascript failed): try again in a minute, log once.
            _pausedUntil = DateTime.UtcNow.AddMinutes(1);
            if (!_deniedLogged)
            {
                _deniedLogged = true;
                PluginLog.Warning("Cannot read the Finder window. Allow Logi Plugin Service to control the Finder in System Settings > Privacy & Security > Automation, or set followFinder to false.");
            }

            return null;
        }

        if (path.Length == 0)
        {
            return null;
        }

        var dir = path.Length > 1 ? path.TrimEnd('/') : path;
        return Directory.Exists(dir) ? dir : null;
    }
}
