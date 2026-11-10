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

    private static void LaunchMac(string dir, AgentConfig agent)
    {
        var line = $"cd {ShellQuote(dir)} && {agent.Command}";
        var app = agent.Terminal.Equals("iterm", StringComparison.OrdinalIgnoreCase)
            || agent.Terminal.Equals("iterm2", StringComparison.OrdinalIgnoreCase) ? "iTerm" : "Terminal";

        var script = app == "iTerm"
            ? $"tell application \"iTerm\"\nactivate\nset w to (create window with default profile)\ntell current session of w to write text {AppleScriptString(line)}\nend tell"
            : $"tell application \"Terminal\"\nactivate\ndo script {AppleScriptString(line)}\nend tell";

        ProcessRunner.Detach(() =>
        {
            var r = ProcessRunner.Run("/usr/bin/osascript", null, TimeSpan.FromSeconds(15), new[] { "-e", script });
            PluginLog.Info($"{agent.Command} started in {app} for {dir}: {(r?.Ok == true ? "ok" : "failed " + r?.Stderr.Trim())}");
        });
    }

    // Untested on Windows: Windows Terminal when present, a PowerShell window otherwise.
    private static void LaunchWindows(string dir, AgentConfig agent)
    {
        var terminal = agent.Terminal.ToLowerInvariant();
        var shell = terminal == "pwsh" || (terminal == "auto" && ToolLocator.Find("pwsh") != null) ? "pwsh" : "powershell";
        var wt = terminal is "auto" or "wt" ? ToolLocator.Find("wt") : null;

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
