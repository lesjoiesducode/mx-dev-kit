namespace MxDevKit.Tests;

public class GitParsersTests
{
    [Theory]
    [InlineData("git@github.com:octo-org/hello-world.git", "octo-org/hello-world")]
    [InlineData("https://github.com/octo-org/hello-world.git", "octo-org/hello-world")]
    [InlineData("https://github.com/octo-org/hello-world", "octo-org/hello-world")]
    [InlineData("https://github.com/octo-org/hello-world/", "octo-org/hello-world")]
    [InlineData("ssh://git@github.com/octo-org/hello-world.git", "octo-org/hello-world")]
    [InlineData("https://someone@github.com/octo-org/hello-world.git\n", "octo-org/hello-world")]
    [InlineData("git@gitlab.com:octo-org/hello-world.git", null)]
    [InlineData("https://notgithub.com/octo-org/hello-world", null)]
    [InlineData("https://github.com.evil.example/octo-org/hello-world", null)]
    [InlineData("https://github.com/octo-org", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GitHubRepo_reads_owner_and_name(string? url, string? expected) =>
        Assert.Equal(expected, GitParsers.GitHubRepo(url));

    [Fact]
    public void CountChanges_counts_porcelain_lines() =>
        Assert.Equal(3, GitParsers.CountChanges(" M src/app.ts\n?? notes.md\r\nA  README.md\n\n"));

    [Fact]
    public void CountChanges_of_a_clean_tree_is_zero() => Assert.Equal(0, GitParsers.CountChanges(""));

    [Theory]
    [InlineData("3\t1\n", 3, 1)]
    [InlineData("0 0", 0, 0)]
    public void AheadBehind_reads_rev_list_counts(string output, int behind, int ahead) =>
        Assert.Equal((behind, ahead), GitParsers.AheadBehind(output));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("fatal: no upstream configured")]
    public void AheadBehind_is_null_without_upstream(string? output) => Assert.Null(GitParsers.AheadBehind(output));

    [Fact]
    public void FindUp_returns_the_closest_folder_with_the_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var files = new HashSet<string> { Path.Combine(root, "package-lock.json"), Path.Combine(root, "web", "package-lock.json") };
        bool Exists(string p) => files.Contains(p);

        Assert.Equal(Path.Combine(root, "web"), GitParsers.FindUp(Path.Combine(root, "web", "src", "lib"), root, "package-lock.json", Exists));
        Assert.Equal(root, GitParsers.FindUp(Path.Combine(root, "docs"), root, "package-lock.json", Exists));
    }

    [Fact]
    public void FindUp_never_leaves_the_repository()
    {
        var parent = Path.Combine(Path.GetTempPath(), "work");
        var root = Path.Combine(parent, "repo");
        bool Exists(string p) => p == Path.Combine(parent, "package-lock.json");

        Assert.Null(GitParsers.FindUp(Path.Combine(root, "src"), root, "package-lock.json", Exists));
        Assert.Null(GitParsers.FindUp(parent, root, "package-lock.json", Exists));
    }

    [Fact]
    public void NameStatus_keeps_the_new_path_of_renames()
    {
        var changes = GitParsers.NameStatus("M\tsrc/app.ts\nR087\told/name.ts\tsrc/name.ts\nA\tREADME.md\n");

        Assert.Equal(3, changes.Count);
        Assert.Equal(new FileChange('M', "src/app.ts", null), changes[0]);
        Assert.Equal(new FileChange('R', "src/name.ts", "old/name.ts"), changes[1]);
        Assert.Equal('A', changes[2].Kind);
    }
}
