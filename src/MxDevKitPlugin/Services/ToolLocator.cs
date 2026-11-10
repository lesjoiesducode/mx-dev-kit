namespace Loupedeck.MxDevKitPlugin;

using System.Collections.Concurrent;

// Logi Plugin Service does not get your shell's PATH, so git, gh, npm, osv-scanner and the agent command
// are looked up in the usual install locations as well. config.json "tools" wins when set.
internal static class ToolLocator
{
    private static readonly ConcurrentDictionary<string, (string? Path, DateTime CheckedAt)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan RecheckMissingAfter = TimeSpan.FromMinutes(2);
    private static IReadOnlyList<string>? _dirs;

    public static string? Find(string name)
    {
        if (DevKit.Config.Tools.TryGetValue(name, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        if (Path.IsPathRooted(name))
        {
            return File.Exists(name) ? name : null;
        }

        if (Cache.TryGetValue(name, out var hit) && (hit.Path != null || DateTime.UtcNow - hit.CheckedAt < RecheckMissingAfter))
        {
            return hit.Path;
        }

        var found = Search(name);
        Cache[name] = (found, DateTime.UtcNow);
        return found;
    }

    public static void Forget(string name) => Cache.TryRemove(name, out _);

    public static void Reset()
    {
        Cache.Clear();
        _dirs = null;
    }

    public static void PrepareEnvironment(IDictionary<string, string?> env)
    {
        env.TryGetValue("PATH", out var current);
        var parts = Dirs().Concat((current ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)).Distinct();
        env["PATH"] = string.Join(Path.PathSeparator, parts);
        env["GH_PROMPT_DISABLED"] = "1";
        env["GIT_TERMINAL_PROMPT"] = "0";
        env["NO_COLOR"] = "1";
        if (!env.ContainsKey("HOME") && !OperatingSystem.IsWindows())
        {
            env["HOME"] = DevKitPaths.Home;
        }
    }

    private static string? Search(string name)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { name + ".exe", name + ".cmd", name + ".bat", name }
            : new[] { name };
        foreach (var dir in Dirs())
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(dir, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> Dirs()
    {
        if (_dirs != null)
        {
            return _dirs;
        }

        var home = DevKitPaths.Home;
        var dirs = new List<string>();
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.AddRange(new[]
            {
                Path.Combine(home, ".local", "bin"),
                Path.Combine(programFiles, "Git", "cmd"),
                Path.Combine(programFiles, "GitHub CLI"),
                Path.Combine(programFiles, "nodejs"),
                Path.Combine(appData, "npm"),
                Path.Combine(localAppData, "Programs", "Git", "cmd"),
                Path.Combine(localAppData, "Microsoft", "WinGet", "Links"),
                Path.Combine(localAppData, "Microsoft", "WindowsApps"),
                Path.Combine(home, "scoop", "shims"),
                Path.Combine(home, "go", "bin"),
            });
        }
        else
        {
            dirs.InsertRange(0, new[]
            {
                Path.Combine(home, ".local", "bin"),
                "/opt/homebrew/bin",
                "/usr/local/bin",
            });
            dirs.AddRange(new[]
            {
                Path.Combine(home, ".volta", "bin"),
                Path.Combine(home, ".bun", "bin"),
                Path.Combine(home, ".npm-global", "bin"),
                Path.Combine(home, ".local", "share", "mise", "shims"),
                Path.Combine(home, "go", "bin"),
                Path.Combine(home, ".asdf", "shims"),
                "/usr/bin",
                "/bin",
            });

            // nvm: newest installed Node first.
            var nvm = Path.Combine(home, ".nvm", "versions", "node");
            if (Directory.Exists(nvm))
            {
                dirs.AddRange(Directory.GetDirectories(nvm)
                    .OrderByDescending(d => Version.TryParse(Path.GetFileName(d).TrimStart('v'), out var v) ? v : new Version(0, 0))
                    .Select(d => Path.Combine(d, "bin")));
            }
        }

        _dirs = dirs.Where(d => d.Length > 0).Distinct().ToList();
        return _dirs;
    }
}
