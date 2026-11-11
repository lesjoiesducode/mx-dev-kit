namespace Loupedeck.MxDevKitPlugin;

using System.Diagnostics;

// Opens a terminal in the project and starts an agent: the configured command (claude by default)
// for the generic Agent bubble, claude, codex, gemini or copilot for their own bubbles.
internal static class AgentLauncher
{
    public static void Launch(string dir, AgentConfig agent)
    {
        try
        {
            if (agent.Launch is { Length: > 0 } custom)
            {
                LaunchCustom(dir, agent.Command, custom);
            }
            else if (OperatingSystem.IsMacOS())
            {
                LaunchMac(dir, agent);
            }
            else if (OperatingSystem.IsWindows())
            {
                LaunchWindows(dir, agent);
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            PluginLog.Warning($"Could not start {agent.Command} in {dir}: {e.Message}");
        }
    }

    // "{dir}" and "{command}" are replaced in every argument.
    private static void LaunchCustom(string dir, string command, string[] launch)
    {
        var args = launch.Select(a => a.Replace("{dir}", dir, StringComparison.Ordinal).Replace("{command}", command, StringComparison.Ordinal)).ToArray();
        var exe = ToolLocator.Find(args[0]) ?? args[0];
        var ok = ProcessRunner.Spawn(exe, dir, args.Skip(1));
        PluginLog.Info($"Agent launch ({args[0]}) in {dir}: {(ok ? "started" : "failed")}");
    }

    // agent.terminal, or the terminal of the last prompt when it is auto (see Terminals).
    private static string? TerminalHint()
    {
        try
        {
            var file = DevKitPaths.CurrentTerminalFile;
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void LaunchMac(string dir, AgentConfig agent)
    {
        var app = Terminals.ChooseMac(agent.Terminal, TerminalHint());
        ProcessRunner.Detach(() =>
        {
            var ok = app switch
            {
                TerminalApp.ITerm => Osascript(ITermScript(dir, agent.Command)),
                TerminalApp.Ghostty => Osascript(GhosttyScript(dir, agent.Command)),
                TerminalApp.Warp => LaunchWarp(dir, agent.Command),
                _ => false,
            };

            // Terminal, and the fallback when the other one did not answer (Ghostty before 1.3 has no AppleScript).
            if (!ok)
            {
                if (app != TerminalApp.Terminal)
                {
                    PluginLog.Info($"{app} did not start {agent.Command}: Terminal instead");
                }

                ok = Osascript(TerminalScript(dir, agent.Command));
                app = TerminalApp.Terminal;
            }

            PluginLog.Info($"{agent.Command} started in {app} for {dir}: {(ok ? "ok" : "failed")}");
        });
    }

    private static bool Osascript(string script)
    {
        var r = ProcessRunner.Run("/usr/bin/osascript", null, TimeSpan.FromSeconds(15), new[] { "-e", script });
        if (r?.Ok != true)
        {
            PluginLog.Info($"osascript: {r?.Stderr.Trim()}");
        }

        return r?.Ok == true;
    }

    private static string Line(string dir, string command) => $"cd {ShellQuote(dir)} && {command}";

    internal static string TerminalScript(string dir, string command) =>
        $"tell application \"Terminal\"\nactivate\ndo script {AppleScriptString(Line(dir, command))}\nend tell";

    internal static string ITermScript(string dir, string command) =>
        $"tell application id \"com.googlecode.iterm2\"\nactivate\nset w to (create window with default profile)\n" +
        $"tell current session of w to write text {AppleScriptString(Line(dir, command))}\nend tell";

    // Ghostty 1.3 and later: a window in the project, the command typed into its shell, which stays.
    internal static string GhosttyScript(string dir, string command) =>
        "tell application \"Ghostty\"\nactivate\nset cfg to new surface configuration\n" +
        $"set initial working directory of cfg to {AppleScriptString(dir)}\nset w to new window with configuration cfg\n" +
        $"input text {AppleScriptString(command)} & linefeed to focused terminal of selected tab of w\nend tell";

    // Warp runs commands only from a tab config: the kit keeps one of its own, rewritten at each click.
    private static bool LaunchWarp(string dir, string command)
    {
        try
        {
            var folder = Path.Combine(DevKitPaths.Home, ".warp", "tab_configs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, WarpTabConfig + ".toml"), WarpToml(dir, command));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            PluginLog.Info($"Warp tab config not written: {e.Message}");
            return false;
        }

        var r = ProcessRunner.Run("/usr/bin/open", null, TimeSpan.FromSeconds(15), new[] { $"warp://tab_config/{WarpTabConfig}?new_window=true" });
        return r?.Ok == true;
    }

    internal const string WarpTabConfig = "mx_dev_kit_agent";

    internal static string WarpToml(string dir, string command) =>
        "# Written by MX Dev Kit at each click on an agent bubble.\nname = \"MX Dev Kit agent\"\n\n[[panes]]\nid = \"main\"\ntype = \"terminal\"\n" +
        $"directory = {Terminals.TomlString(dir)}\ncommands = [{Terminals.TomlString(command)}]\n";

    // Untested on Windows. agent.terminal, or with auto the terminal of the last prompt: Windows Terminal,
    // or a plain PowerShell window for a prompt that ran in one; Windows Terminal when present otherwise.
    private static void LaunchWindows(string dir, AgentConfig agent)
    {
        var terminal = agent.Terminal.ToLowerInvariant();
        var hint = terminal == "auto" ? TerminalHint() : null;
        var detected = Terminals.Detect(hint);
        var hinted = Terminals.WindowsShell(hint);
        var shell = terminal switch
        {
            "pwsh" => "pwsh",
            "powershell" => "powershell",
            _ when hinted != null && ToolLocator.Find(hinted) != null => hinted,
            _ => ToolLocator.Find("pwsh") != null ? "pwsh" : "powershell",
        };
        var wt = terminal == "wt" || (terminal == "auto" && detected != TerminalApp.Console) ? ToolLocator.Find("wt") : null;

        ProcessStartInfo psi;
        if (wt != null)
        {
            psi = new ProcessStartInfo(wt) { UseShellExecute = false };
            foreach (var a in new[] { "-d", dir, shell, "-NoExit", "-Command", agent.Command })
            {
                psi.ArgumentList.Add(a);
            }
        }
        else
        {
            psi = new ProcessStartInfo(shell + ".exe") { UseShellExecute = true, WorkingDirectory = dir };
            psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(agent.Command);
        }

        Process.Start(psi)?.Dispose();
        PluginLog.Info($"{agent.Command} started in {(wt != null ? "Windows Terminal" : shell)} for {dir}");
    }

    // The settings of the generic agent (terminal, launch), with the command line of this agent.
    public static AgentConfig For(CodingAgent agent, AgentConfig config) => new()
    {
        Label = agent.Name,
        Command = CodingAgents.CommandLine(agent, config),
        Terminal = config.Terminal,
        Launch = config.Launch,
    };

    public static string Program(CodingAgent agent) => CodingAgents.Program(CodingAgents.CommandLine(agent, DevKit.Config.Agent));

    // recheck: look again now rather than trust a "not found" from the last two minutes (after a click).
    public static bool IsInstalled(CodingAgent agent, bool recheck = false)
    {
        var program = Program(agent);
        if (recheck)
        {
            ToolLocator.Forget(program);
        }

        return program.Length > 0 && ToolLocator.Find(program) != null;
    }

    internal static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    internal static string AppleScriptString(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
