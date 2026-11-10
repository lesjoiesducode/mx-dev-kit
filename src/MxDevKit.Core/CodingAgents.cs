using System;
using System.Collections.Generic;
using System.Linq;

namespace MxDevKit;

// The coding agents that have a bubble of their own. Id: key of agent.commands in config.json and
// of the trigger file. Command: what the bubble types in the terminal unless config.json says otherwise.
public sealed record CodingAgent(string Id, string Name, string Command, AgentLogo Logo, Rgb Background, bool MonoLogo, string InstallUrl);

public static class CodingAgents
{
    public static readonly CodingAgent Claude = new(
        "claude", "Claude Code", "claude", AgentLogo.Claude, new Rgb(0xD9, 0x77, 0x57), true, "https://code.claude.com/docs/en/setup");

    public static readonly CodingAgent Codex = new(
        "codex", "Codex", "codex", AgentLogo.Codex, new Rgb(0xFF, 0xFF, 0xFF), false, "https://developers.openai.com/codex/cli");

    public static readonly CodingAgent Gemini = new(
        "gemini", "Gemini CLI", "gemini", AgentLogo.Gemini, new Rgb(0x16, 0x1B, 0x22), false, "https://github.com/google-gemini/gemini-cli");

    public static readonly CodingAgent Copilot = new(
        "copilot", "Copilot CLI", "copilot", AgentLogo.Copilot, new Rgb(0x0D, 0x11, 0x17), true, "https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli");

    public static readonly IReadOnlyList<CodingAgent> All = new[] { Claude, Codex, Gemini, Copilot };

    public static CodingAgent? Find(string id) => All.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // The command line typed for this agent: agent.commands in config.json, or the agent's own command.
    public static string CommandLine(CodingAgent agent, AgentConfig config) =>
        config.Commands.TryGetValue(agent.Id, out var custom) && !string.IsNullOrWhiteSpace(custom) ? custom.Trim() : agent.Command;

    // The program of a command line, to check that it is installed: "codex --full-auto" -> codex,
    // "'/opt/my tools/claude' -c" -> /opt/my tools/claude.
    public static string Program(string commandLine)
    {
        var line = commandLine.Trim();
        if (line.Length == 0)
        {
            return "";
        }

        if (line[0] is '"' or '\'')
        {
            var end = line.IndexOf(line[0], 1);
            return end > 0 ? line[1..end] : line[1..];
        }

        var space = line.IndexOfAny(new[] { ' ', '\t' });
        return space < 0 ? line : line[..space];
    }
}
