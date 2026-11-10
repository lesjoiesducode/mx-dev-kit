using System;
using System.IO;

namespace MxDevKit;

// The shell integration writes current-dir here; the plugin reads it along with config.json.
// macOS: ~/.config/mx-dev-kit   Windows: %APPDATA%\mx-dev-kit
public static class DevKitPaths
{
    public const string FolderName = "mx-dev-kit";

    public static string StateDir { get; } = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName)
        : Path.Combine(Home, ".config", FolderName);

    public static string Home =>
        Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home && !OperatingSystem.IsWindows()
            ? home
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ConfigFile => Path.Combine(StateDir, "config.json");

    public static string CurrentDirFile => Path.Combine(StateDir, "current-dir");

    // Present: no vibration at all, from the plugin, the hooks or the shell scripts.
    public static string MuteFile => Path.Combine(StateDir, "haptics.off");

    // Present: no system notification at all (bin/mx-notify checks it).
    public static string NotificationsMuteFile => Path.Combine(StateDir, "notifications.off");

    // The installers copy bin/ here: haptic, mx-notify, uptime-watch, docker-watch.
    public static string BinDir => Path.Combine(StateDir, "bin");

    // Writing commit, push, pull, agent or focus here simulates a click (handy without a mouse).
    public static string TriggerFile => Path.Combine(StateDir, "trigger");

    // When this folder exists, every bubble the plugin draws is also saved there as a PNG.
    public static string PreviewDir => Path.Combine(StateDir, "preview");
}
