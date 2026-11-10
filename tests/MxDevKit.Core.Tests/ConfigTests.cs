namespace MxDevKit.Tests;

public class ConfigTests
{
    [Fact]
    public void Empty_config_gives_defaults()
    {
        var config = DevKitConfig.Parse("{}");

        Assert.Equal("auto", config.Language);
        Assert.Equal("sdk", config.Haptics.Mode);
        Assert.Equal(HapticsConfig.DefaultUrl, config.Haptics.Url);
        Assert.Equal(60, config.GitHub.RefreshSeconds);
        Assert.Equal("high", config.Audit.MinSeverity);
        Assert.Null(config.Commit.MessageCommand);
        Assert.Equal("claude", config.Agent.Command);
        Assert.Equal(25, config.Focus.Minutes);
        Assert.True(config.Notifications.Enabled);
    }

    [Fact]
    public void Notifications_can_be_turned_off()
    {
        Assert.False(DevKitConfig.Parse("""{ "notifications": { "enabled": false } }""").Notifications.Enabled);
        Assert.True(DevKitConfig.Parse("""{ "notifications": null }""").Notifications.Enabled);
    }

    [Fact]
    public void Comments_trailing_commas_and_any_case_are_accepted()
    {
        const string json = """
            {
              // haptics through the Haptic Web Plugin
              "haptics": { "mode": "HTTP", "waveforms": { "ciPassed": "firework" } },
              "GITHUB": { "refreshSeconds": 120, },
              "commit": { "messageCommand": ["claude", "-p", "{prompt}", "--model", "haiku"] },
              "tools": { "gh": "/usr/local/bin/gh" },
            }
            """;

        var config = DevKitConfig.Parse(json);

        Assert.Equal("http", config.Haptics.Mode);
        Assert.Equal("firework", HapticEvents.WaveformFor(HapticEvents.CiPassed, config.Haptics.Waveforms));
        Assert.Equal(120, config.GitHub.RefreshSeconds);
        Assert.Equal(5, config.Commit.MessageCommand!.Length);
        Assert.Equal("/usr/local/bin/gh", config.Tools["GH"]);
    }

    [Fact]
    public void Out_of_range_values_are_brought_back()
    {
        var config = DevKitConfig.Parse("""
            {
              "haptics": { "mode": "loud" },
              "github": { "refreshSeconds": 1, "runningRefreshSeconds": 500, "reviewQuery": " " },
              "audit": { "refreshMinutes": 0, "minSeverity": "urgent" },
              "focus": { "minutes": 0, "breakMinutes": -3 },
              "git": { "fetchMinutes": -1 },
              "agent": { "command": "", "label": "" }
            }
            """);

        Assert.Equal("sdk", config.Haptics.Mode);
        Assert.Equal(15, config.GitHub.RefreshSeconds);
        Assert.Equal(15, config.GitHub.RunningRefreshSeconds);
        Assert.Equal(GitHubConfig.DefaultReviewQuery, config.GitHub.ReviewQuery);
        Assert.Equal(1, config.Audit.RefreshMinutes);
        Assert.Equal("high", config.Audit.MinSeverity);
        Assert.Equal(1, config.Focus.Minutes);
        Assert.Equal(0, config.Focus.BreakMinutes);
        Assert.Equal(0, config.Git.FetchMinutes);
        Assert.Equal("claude", config.Agent.Command);
        Assert.Equal("Claude Code", config.Agent.Label);
    }

    [Fact]
    public void Null_sections_do_not_break_the_plugin()
    {
        var config = DevKitConfig.Parse("""{ "haptics": null, "github": null, "tools": null }""");

        Assert.Equal("sdk", config.Haptics.Mode);
        Assert.True(config.GitHub.Enabled);
        Assert.Empty(config.Tools);
    }

    [Fact]
    public void Broken_file_falls_back_to_defaults_with_an_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mx-dev-kit-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"haptics\": ");
        try
        {
            var config = DevKitConfig.Load(path, out var error);
            Assert.NotNull(error);
            Assert.Equal("sdk", config.Haptics.Mode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_file_is_not_an_error()
    {
        DevKitConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist", "config.json"), out var error);
        Assert.Null(error);
    }

    [Fact]
    public void Example_config_in_the_repository_is_valid()
    {
        var example = Path.Combine(RepoRoot(), "config.example.json");
        var config = DevKitConfig.Parse(File.ReadAllText(example));

        Assert.Equal("sdk", config.Haptics.Mode);
    }

    [Fact]
    public void Unknown_waveforms_fall_back_to_the_default()
    {
        var overrides = new Dictionary<string, string> { [HapticEvents.CiFailed] = "explosion" };
        Assert.Equal("angry_alert", HapticEvents.WaveformFor(HapticEvents.CiFailed, overrides));
    }

    [Fact]
    public void Event_mapping_yaml_matches_the_code()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "MxDevKitPlugin", "package", "events", "extra", "eventMapping.yaml"));
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "MxDevKitPlugin", "package", "events", "DefaultEventSource.yaml"));

        foreach (var (name, waveform) in HapticEvents.DefaultWaveforms)
        {
            Assert.Contains($"    {name}:\n        DEFAULT: {waveform}\n", yaml.Replace("\r\n", "\n"));
            Assert.Contains($"- name: {name}\n", source.Replace("\r\n", "\n"));
            Assert.Contains(waveform, HapticEvents.Waveforms);
        }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "config.example.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Repository root not found");
    }
}
