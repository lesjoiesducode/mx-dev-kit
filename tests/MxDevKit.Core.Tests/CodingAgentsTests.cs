namespace MxDevKit.Tests;

public class CodingAgentsTests
{
    private static readonly Strings En = Strings.English;
    private static readonly Strings Fr = Strings.French;

    [Fact]
    public void Each_agent_has_its_own_bubble_and_command()
    {
        Assert.Equal(new[] { "claude", "codex", "gemini", "copilot" }, CodingAgents.All.Select(a => a.Command));
        Assert.Equal(4, CodingAgents.All.Select(a => a.Logo).Distinct().Count());
        Assert.DoesNotContain(CodingAgents.All, a => a.Logo == AgentLogo.None);
        Assert.All(CodingAgents.All, a => Assert.StartsWith("https://", a.InstallUrl));
        Assert.Same(CodingAgents.Copilot, CodingAgents.Find("Copilot"));
        Assert.Null(CodingAgents.Find("aider"));
    }

    [Fact]
    public void Installed_agent_shows_its_mark_alone()
    {
        var bubble = Bubbles.CodingAgent(CodingAgents.Claude, true, En);

        Assert.Equal(AgentLogo.Claude, bubble.Logo);
        Assert.Equal("", bubble.Value);
        Assert.Equal("Claude Code", bubble.Label);
        Assert.Equal(new Rgb(0xD9, 0x77, 0x57), bubble.Background);
    }

    [Fact]
    public void Missing_agent_is_grey_and_says_so()
    {
        var bubble = Bubbles.CodingAgent(CodingAgents.Codex, false, En);

        Assert.Equal(AgentLogo.Codex, bubble.Logo);
        Assert.True(bubble.LogoMono);
        Assert.Equal(Palette.Grey, bubble.Background);
        Assert.Equal("Install", bubble.Value);
        Assert.Equal("Codex: not installed", bubble.Label);
        Assert.Equal("Copilot CLI : non installé", Bubbles.CodingAgent(CodingAgents.Copilot, false, Fr).Label);
    }

    [Theory]
    [InlineData("codex", "codex")]
    [InlineData("  codex --full-auto ", "codex")]
    [InlineData("claude\t--continue", "claude")]
    [InlineData("'/opt/my tools/claude' --continue", "/opt/my tools/claude")]
    [InlineData("\"C:\\Program Files\\copilot.exe\"", "C:\\Program Files\\copilot.exe")]
    [InlineData("", "")]
    public void Program_is_the_first_word_of_the_command_line(string line, string program) =>
        Assert.Equal(program, CodingAgents.Program(line));

    [Fact]
    public void Command_lines_can_be_set_per_agent()
    {
        var config = DevKitConfig.Parse("""{ "agent": { "commands": { "Codex": "codex --full-auto", "gemini": " " } } }""").Agent;

        Assert.Equal("codex --full-auto", CodingAgents.CommandLine(CodingAgents.Codex, config));
        Assert.Equal("gemini", CodingAgents.CommandLine(CodingAgents.Gemini, config));
        Assert.Equal("claude", CodingAgents.CommandLine(CodingAgents.Claude, config));
        Assert.Equal("claude", CodingAgents.CommandLine(CodingAgents.Claude, DevKitConfig.Parse("{}").Agent));
    }

    [Fact]
    public void Descriptions_name_the_agent_and_its_command()
    {
        Assert.Contains("Gemini CLI (gemini)", En.CodingAgentText(CodingAgents.Gemini));
        Assert.Contains("Copilot CLI (copilot)", Fr.CodingAgentText(CodingAgents.Copilot));
    }
}
