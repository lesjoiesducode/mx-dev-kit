using System;

namespace MxDevKit;

public enum TerminalApp
{
    Unknown,
    Terminal,
    ITerm,
    Ghostty,
    Warp,
    VsCode,
    WindowsTerminal,
    Console,
}

// The terminal the agent bubbles open. The shell integration writes, at every prompt, a line to
// current-terminal: TERM_PROGRAM and the bundle id of the app on macOS (__CFBundleIdentifier, which
// survives tmux), WindowsTerminal or console and the PowerShell edition on Windows, tab-separated.
public static class Terminals
{
    public static TerminalApp Detect(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return TerminalApp.Unknown;
        }

        // The bundle id comes after TERM_PROGRAM and is the more reliable of the two.
        var fields = hint.Trim().Split('\t');
        for (var i = fields.Length - 1; i >= 0; i--)
        {
            var app = FromName(fields[i].Trim());
            if (app != TerminalApp.Unknown)
            {
                return app;
            }
        }

        return TerminalApp.Unknown;
    }

    // pwsh or powershell, when the Windows integration said which PowerShell the prompt runs in.
    public static string? WindowsShell(string? hint)
    {
        foreach (var field in (hint ?? "").Split('\t'))
        {
            var f = field.Trim().ToLowerInvariant();
            if (f is "pwsh" or "powershell")
            {
                return f;
            }
        }

        return null;
    }

    // macOS: agent.terminal wins unless it is auto; auto takes the terminal of the last prompt. A terminal
    // that cannot be driven from outside (the one of VS Code) or an unknown one: Terminal.
    public static TerminalApp ChooseMac(string? configured, string? hint)
    {
        var set = FromName((configured ?? "").Trim());
        if (set is TerminalApp.Terminal or TerminalApp.ITerm or TerminalApp.Ghostty or TerminalApp.Warp)
        {
            return set;
        }

        return Detect(hint) switch
        {
            TerminalApp.ITerm => TerminalApp.ITerm,
            TerminalApp.Ghostty => TerminalApp.Ghostty,
            TerminalApp.Warp => TerminalApp.Warp,
            _ => TerminalApp.Terminal,
        };
    }

    public static TerminalApp FromName(string name)
    {
        var n = name.ToLowerInvariant();
        return n switch
        {
            "terminal" or "apple_terminal" or "com.apple.terminal" => TerminalApp.Terminal,
            "iterm" or "iterm2" or "iterm.app" or "com.googlecode.iterm2" => TerminalApp.ITerm,
            "ghostty" or "com.mitchellh.ghostty" => TerminalApp.Ghostty,
            "warp" or "warpterminal" => TerminalApp.Warp,
            "vscode" or "com.microsoft.vscode" or "com.microsoft.vscodeinsiders" or "com.vscodium" => TerminalApp.VsCode,
            "windowsterminal" or "wt" => TerminalApp.WindowsTerminal,
            "console" => TerminalApp.Console,
            _ when n.StartsWith("dev.warp.", StringComparison.Ordinal) => TerminalApp.Warp,
            _ => TerminalApp.Unknown,
        };
    }

    // A string for a TOML file (Warp tab configs): quotes, backslashes and control characters escaped.
    public static string TomlString(string s)
    {
        var b = new System.Text.StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\t': b.Append("\\t"); break;
                case '\r': b.Append("\\r"); break;
                default:
                    if (c < ' ')
                    {
                        b.Append("\\u").Append(((int)c).ToString("X4"));
                    }
                    else
                    {
                        b.Append(c);
                    }

                    break;
            }
        }

        return b.Append('"').ToString();
    }
}
