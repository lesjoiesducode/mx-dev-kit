namespace MxDevKit.Tests;

public class TerminalsTests
{
    [Theory]
    [InlineData("Apple_Terminal\tcom.apple.Terminal", TerminalApp.Terminal)]
    [InlineData("iTerm.app\tcom.googlecode.iterm2", TerminalApp.ITerm)]
    [InlineData("ghostty\tcom.mitchellh.ghostty", TerminalApp.Ghostty)]
    [InlineData("WarpTerminal\tdev.warp.Warp-Stable", TerminalApp.Warp)]
    [InlineData("vscode\tcom.microsoft.VSCode", TerminalApp.VsCode)]
    [InlineData("tmux\tcom.mitchellh.ghostty", TerminalApp.Ghostty)]
    [InlineData("ghostty\t", TerminalApp.Ghostty)]
    [InlineData("WindowsTerminal\tpwsh", TerminalApp.WindowsTerminal)]
    [InlineData("console\tpowershell", TerminalApp.Console)]
    [InlineData("tmux\t", TerminalApp.Unknown)]
    [InlineData("", TerminalApp.Unknown)]
    [InlineData(null, TerminalApp.Unknown)]
    public void Detect_reads_the_line_of_the_shell_integration(string? hint, TerminalApp expected) =>
        Assert.Equal(expected, Terminals.Detect(hint));

    [Theory]
    [InlineData("auto", "ghostty\tcom.mitchellh.ghostty", TerminalApp.Ghostty)]
    [InlineData("auto", "WarpTerminal\tdev.warp.Warp-Stable", TerminalApp.Warp)]
    [InlineData("auto", "iTerm.app\tcom.googlecode.iterm2", TerminalApp.ITerm)]
    [InlineData("auto", "Apple_Terminal\tcom.apple.Terminal", TerminalApp.Terminal)]
    [InlineData("auto", "vscode\tcom.microsoft.VSCode", TerminalApp.Terminal)]
    [InlineData("auto", null, TerminalApp.Terminal)]
    [InlineData("iTerm", "ghostty\tcom.mitchellh.ghostty", TerminalApp.ITerm)]
    [InlineData("Terminal", "ghostty\tcom.mitchellh.ghostty", TerminalApp.Terminal)]
    [InlineData("ghostty", null, TerminalApp.Ghostty)]
    [InlineData("Warp", null, TerminalApp.Warp)]
    public void The_setting_wins_over_the_detected_terminal(string configured, string? hint, TerminalApp expected) =>
        Assert.Equal(expected, Terminals.ChooseMac(configured, hint));

    [Theory]
    [InlineData("WindowsTerminal\tpwsh", "pwsh")]
    [InlineData("console\tpowershell", "powershell")]
    [InlineData("vscode\t", null)]
    [InlineData(null, null)]
    public void WindowsShell_is_the_powershell_of_the_prompt(string? hint, string? expected) =>
        Assert.Equal(expected, Terminals.WindowsShell(hint));

    [Fact]
    public void TomlString_escapes_what_toml_needs() =>
        Assert.Equal("\"C:\\\\it's \\\"here\\\"\\n\"", Terminals.TomlString("C:\\it's \"here\"\n"));
}
