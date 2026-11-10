namespace Loupedeck.MxDevKitPlugin;

// System notifications. Logi Plugin Service has no app identity of its own to post a clickable
// notification, so the plugin runs bin/mx-notify, like the scripts do: terminal-notifier or
// AppleScript on macOS, a native toast on Windows. The copy in the state folder wins (the
// installers keep it up to date); the plugin package carries its own for the .lplug4 alone.
internal static class Notifier
{
    private static bool _warned;

    public static void Show(string title, string text, string? url = null)
    {
        if (!DevKit.Config.Notifications.Enabled)
        {
            return;
        }

        ProcessRunner.Detach(() =>
        {
            var (exe, args) = Command(title, text, url);
            if (exe == null)
            {
                return;
            }

            var result = ProcessRunner.Run(exe, null, TimeSpan.FromSeconds(15), args);
            if (result is { Ok: false })
            {
                PluginLog.Warning($"mx-notify exit {result.ExitCode}: {result.Stderr.Trim()}");
            }
        });
    }

    private static (string? Exe, List<string> Args) Command(string title, string text, string? url)
    {
        var windows = OperatingSystem.IsWindows();
        var name = windows ? "mx-notify.ps1" : "mx-notify";
        var script = Path.Combine(DevKitPaths.BinDir, name);
        if (!File.Exists(script))
        {
            script = Path.Combine(Path.GetDirectoryName(typeof(Notifier).Assembly.Location) ?? string.Empty, name);
        }

        if (!File.Exists(script))
        {
            if (!_warned)
            {
                _warned = true;
                PluginLog.Warning($"{name} not found in {DevKitPaths.BinDir} nor next to the plugin: notifications are off");
            }

            return (null, new List<string>());
        }

        var args = new List<string>();
        string exe;
        if (windows)
        {
            exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            args.AddRange(new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script });
        }
        else
        {
            exe = "/bin/sh";
            args.Add(script);
        }

        args.Add(title);
        args.Add(text);
        if (!string.IsNullOrEmpty(url))
        {
            args.Add(url);
        }

        return (exe, args);
    }
}
