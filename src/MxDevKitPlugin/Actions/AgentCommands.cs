namespace Loupedeck.MxDevKitPlugin;

// One bubble per coding agent, with its mark. Each opens a terminal in the current project and
// starts the agent; grey with "Install" when the agent is not found on this computer.
public abstract class CodingAgentCommand : ProjectCommand
{
    private readonly CodingAgent _agent;

    protected CodingAgentCommand(CodingAgent agent)
        : base(agent.Name, S.CodingAgentText(agent))
    {
        this._agent = agent;
        DevKit.ConfigChanged += this.Redraw;
    }

    protected override Bubble Build() => Bubbles.CodingAgent(this._agent, AgentLauncher.IsInstalled(this._agent), S);

    protected override void RunCommand(string actionParameter) => DevKit.Project.LaunchAgent(this._agent);
}

public class ClaudeCodeCommand : CodingAgentCommand
{
    public ClaudeCodeCommand()
        : base(CodingAgents.Claude)
    {
    }
}

public class CodexCommand : CodingAgentCommand
{
    public CodexCommand()
        : base(CodingAgents.Codex)
    {
    }
}

public class GeminiCommand : CodingAgentCommand
{
    public GeminiCommand()
        : base(CodingAgents.Gemini)
    {
    }
}

public class CopilotCommand : CodingAgentCommand
{
    public CopilotCommand()
        : base(CodingAgents.Copilot)
    {
    }
}
