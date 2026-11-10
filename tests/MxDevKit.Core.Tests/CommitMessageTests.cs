namespace MxDevKit.Tests;

public class CommitMessageTests
{
    private static IReadOnlyList<FileChange> Changes(string nameStatus) => GitParsers.NameStatus(nameStatus);

    [Theory]
    [InlineData("M\tsrc/app.ts", "Update app.ts")]
    [InlineData("A\tdocs/setup.md\nA\tdocs/usage.md", "Add setup.md and usage.md")]
    [InlineData("D\ta.txt\nD\tb.txt\nD\tc.txt", "Remove a.txt, b.txt and c.txt")]
    [InlineData("M\ta.ts\nA\tb.ts\nM\tc.ts\nD\td.ts\nM\te.ts", "Update a.ts, b.ts and 3 more files")]
    [InlineData("R100\tsrc/old.ts\tsrc/new.ts", "Rename old.ts to new.ts")]
    public void Fallback_describes_the_staged_files(string nameStatus, string expected) =>
        Assert.Equal(expected, CommitMessage.Fallback(Changes(nameStatus)));

    [Fact]
    public void Fallback_counts_a_file_once_even_in_two_folders() =>
        Assert.Equal("Update index.ts", CommitMessage.Fallback(Changes("M\tsrc/index.ts\nM\ttest/index.ts")));

    [Theory]
    [InlineData("Fix token refresh on expired sessions\n", "Fix token refresh on expired sessions")]
    [InlineData("\"Add retry to the upload client\"", "Add retry to the upload client")]
    [InlineData("```\nAdd dark mode toggle\n```", "Add dark mode toggle")]
    [InlineData("Commit message: Bump lodash to 4.17.21", "Bump lodash to 4.17.21")]
    [InlineData("`Rename config loader`", "Rename config loader")]
    [InlineData("\n\n  Update README  \nMore details here", "Update README")]
    public void Clean_keeps_the_first_meaningful_line(string raw, string expected) =>
        Assert.Equal(expected, CommitMessage.Clean(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("```\n```")]
    [InlineData(null)]
    public void Clean_is_null_when_nothing_is_left(string? raw) => Assert.Null(CommitMessage.Clean(raw));

    [Fact]
    public void Clean_shortens_long_lines_on_a_word_boundary()
    {
        var raw = string.Join(" ", Enumerable.Repeat("refactor", 20));
        var message = CommitMessage.Clean(raw)!;

        Assert.True(message.Length <= CommitMessage.MaxLength);
        Assert.EndsWith("refactor", message);
    }

    [Fact]
    public void Invocation_puts_the_prompt_in_place_of_the_placeholder()
    {
        var (args, stdin) = CommitMessage.Invocation(new[] { "-p", "{prompt}", "--model", "haiku" }, "Write it", "diff --git a b");

        Assert.Equal(new[] { "-p", "Write it", "--model", "haiku" }, args);
        Assert.Equal("diff --git a b", stdin);
    }

    [Fact]
    public void Invocation_without_placeholder_sends_prompt_then_diff_on_stdin()
    {
        var (args, stdin) = CommitMessage.Invocation(new[] { "--no-stream" }, "Write it", "diff --git a b");

        Assert.Equal(new[] { "--no-stream" }, args);
        Assert.Equal("Write it\n\ndiff --git a b", stdin);
    }
}
