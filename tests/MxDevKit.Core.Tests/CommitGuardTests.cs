namespace MxDevKit.Tests;

public class CommitGuardTests
{
    [Fact]
    public void ParseStatus_reads_entries_renames_and_untracked_files()
    {
        var status = CommitGuard.ParseStatus(" M src/app.ts\0R  new name.md\0old.md\0?? .env\0UU api.ts\0");

        Assert.Equal(4, status.Count);
        Assert.Equal(new StatusEntry(" M", "src/app.ts", null), status[0]);
        Assert.Equal(new StatusEntry("R ", "new name.md", "old.md"), status[1]);
        Assert.True(status[2].Untracked);
        Assert.True(status[3].Unmerged);
    }

    [Theory]
    [InlineData("UU")]
    [InlineData("AA")]
    [InlineData("DU")]
    [InlineData("UD")]
    [InlineData("AU")]
    [InlineData("UA")]
    [InlineData("DD")]
    public void Every_unmerged_state_is_seen(string xy) => Assert.True(new StatusEntry(xy, "a", null).Unmerged);

    [Theory]
    [InlineData(".env", true)]
    [InlineData("api/.env", true)]
    [InlineData(".env.local", true)]
    [InlineData(".env.production", true)]
    [InlineData(".ENV", true)]
    [InlineData(".env.example", false)]
    [InlineData(".env.sample", false)]
    [InlineData(".env.template", false)]
    [InlineData(".env.dist", false)]
    [InlineData("config/server.pem", true)]
    [InlineData("tls/private.key", true)]
    [InlineData("cert.p12", true)]
    [InlineData("id_rsa", true)]
    [InlineData("keys/id_ed25519", true)]
    [InlineData("id_rsa.pub", false)]
    [InlineData("credentials.json", true)]
    [InlineData("client_secret_123.apps.googleusercontent.com.json", true)]
    [InlineData(".netrc", true)]
    [InlineData("src\\secrets\\.env", true)]
    [InlineData("environment.ts", false)]
    [InlineData("docs/env.md", false)]
    [InlineData("keyboard.ts", false)]
    [InlineData("package.json", false)]
    public void IsSensitive_flags_secret_files(string path, bool expected) => Assert.Equal(expected, CommitGuard.IsSensitive(path));

    [Theory]
    [InlineData("<<<<<<< HEAD", true)]
    [InlineData(">>>>>>> feature/search", true)]
    [InlineData("<<<<<<<", true)]
    [InlineData(">>>>>>> main\r", true)]
    [InlineData("=======", false)]
    [InlineData("<<<<<<<< eight", false)]
    [InlineData("  <<<<<<< indented", false)]
    [InlineData("a <<<<<<< b", false)]
    public void IsMarker_matches_what_git_writes(string line, bool expected) => Assert.Equal(expected, CommitGuard.IsMarker(line));

    [Fact]
    public void HasMarkers_ignores_binary_content()
    {
        Assert.True(CommitGuard.HasMarkers("a\n<<<<<<< HEAD\nb\n=======\nc\n>>>>>>> x\n"));
        Assert.False(CommitGuard.HasMarkers("PNG\0<<<<<<< HEAD\n"));
        Assert.False(CommitGuard.HasMarkers("Title\n=======\n"));
        Assert.False(CommitGuard.HasMarkers(null));
    }

    [Fact]
    public void MarkersInDiff_only_looks_at_added_lines()
    {
        const string diff = """
            diff --git a/app.ts b/app.ts
            index 1111111..2222222 100644
            --- a/app.ts
            +++ b/app.ts
            @@ -3,0 +4,5 @@
            +<<<<<<< HEAD
            +const a = 1;
            +=======
            +const a = 2;
            +>>>>>>> feature
            diff --git a/docs/git.md b/docs/git.md
            index 3333333..4444444 100644
            --- a/docs/git.md
            +++ b/docs/git.md
            @@ -10 +10 @@
            -<<<<<<< HEAD
            + git writes a line of seven < signs
            diff --git "a/sp ace.txt" "b/sp ace.txt"
            --- "a/sp ace.txt"
            +++ "b/sp ace.txt"
            @@ -1 +1 @@
            ->>>>>>> old
            +>>>>>>> new
            """;

        Assert.Equal(new[] { "app.ts", "sp ace.txt" }, CommitGuard.MarkersInDiff(diff));
    }

    [Fact]
    public void MarkersInDiff_of_nothing_is_empty()
    {
        Assert.Empty(CommitGuard.MarkersInDiff(""));
        Assert.Empty(CommitGuard.MarkersInDiff(null));
    }

    [Fact]
    public void Check_refuses_unmerged_files_first()
    {
        var status = CommitGuard.ParseStatus("UU api.ts\0?? .env\0");
        var check = CommitGuard.Check(status, "merge", Array.Empty<string>());

        Assert.Equal(CommitBlock.Unmerged, check.Block);
        Assert.Equal(new[] { "api.ts" }, check.Files);
    }

    [Fact]
    public void Check_refuses_an_unfinished_merge_without_conflicts()
    {
        var check = CommitGuard.Check(CommitGuard.ParseStatus("M  api.ts\0"), "rebase", Array.Empty<string>());

        Assert.Equal(CommitBlock.Operation, check.Block);
        Assert.Equal("rebase", check.Operation);
    }

    [Fact]
    public void Check_refuses_markers_then_secrets()
    {
        var status = CommitGuard.ParseStatus(" M app.ts\0?? .env\0");

        Assert.Equal(CommitBlock.Markers, CommitGuard.Check(status, null, new[] { "app.ts" }).Block);

        var secrets = CommitGuard.Check(status, null, Array.Empty<string>());
        Assert.Equal(CommitBlock.Sensitive, secrets.Block);
        Assert.Equal(new[] { ".env" }, secrets.Files);
    }

    [Fact]
    public void Check_lets_a_deleted_secret_go()
    {
        var check = CommitGuard.Check(CommitGuard.ParseStatus(" D .env\0D  certs/server.pem\0 M app.ts\0"), null, Array.Empty<string>());

        Assert.True(check.Ok);
    }

    [Fact]
    public void Check_allows_ordinary_changes() =>
        Assert.True(CommitGuard.Check(CommitGuard.ParseStatus(" M app.ts\0?? .env.example\0A  README.md\0"), null, Array.Empty<string>()).Ok);

    [Theory]
    [InlineData("origin/main\n", "main")]
    [InlineData("origin/develop", "develop")]
    [InlineData("origin/release/2026", "release/2026")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DefaultBranch_reads_origin_head(string? symbolicRef, string? expected) =>
        Assert.Equal(expected, CommitGuard.DefaultBranch(symbolicRef));

    [Theory]
    [InlineData("main", null, true)]
    [InlineData("master", null, true)]
    [InlineData("develop", "develop", true)]
    [InlineData("feature/search", "main", false)]
    [InlineData(null, "main", false)]
    public void NeedsConfirmation_on_the_default_branch(string? branch, string? defaultBranch, bool expected) =>
        Assert.Equal(expected, CommitGuard.NeedsConfirmation(branch, defaultBranch));

    [Fact]
    public void Confirming_shows_for_the_confirm_window_only()
    {
        var at = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        var status = new ActionStatus(ActionPhase.Confirming, at);

        Assert.Equal(ActionPhase.Confirming, status.Visible(at.AddSeconds(4.9)));
        Assert.Equal(ActionPhase.Idle, status.Visible(at.AddSeconds(5.1)));
    }

    [Fact]
    public void Commit_bubble_asks_for_confirmation()
    {
        var at = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        var view = new ProjectView
        {
            Root = "/work/api",
            Changes = 3,
            Actions = new Dictionary<string, ActionStatus> { [GitActions.Commit] = new(ActionPhase.Confirming, at) },
        };

        Assert.Equal("Confirm?", Bubbles.Commit(view, Strings.English, at.AddSeconds(1)).Value);
        Assert.Equal("Confirmer\u00A0?", Bubbles.Commit(view, Strings.French, at.AddSeconds(1)).Value);
        Assert.Equal("3", Bubbles.Commit(view, Strings.English, at.AddSeconds(6)).Value);
    }

    [Fact]
    public void Refusal_texts_name_the_files()
    {
        var en = Strings.English;
        var fr = Strings.French;

        Assert.Equal("Merge conflict in api.ts and web.ts. Resolve it, then click again.",
            en.CommitRefusal(new CommitCheck(CommitBlock.Unmerged, new[] { "api.ts", "web.ts" })));
        Assert.Equal("Conflit de merge dans a, b, c et 2 autres. Résolvez-le, puis cliquez à nouveau.",
            fr.CommitRefusal(new CommitCheck(CommitBlock.Unmerged, new[] { "a", "b", "c", "d", "e" })));
        Assert.Equal(".env would be committed. Add it to .gitignore, or commit from a terminal if it is meant to be shared.",
            en.CommitRefusal(new CommitCheck(CommitBlock.Sensitive, new[] { ".env" })));
        Assert.StartsWith(".env et id_rsa partiraient dans le commit.",
            fr.CommitRefusal(new CommitCheck(CommitBlock.Sensitive, new[] { ".env", "id_rsa" })));
        Assert.Equal("A rebase is in progress. Finish it or abort it in a terminal.",
            en.CommitRefusal(new CommitCheck(CommitBlock.Operation, Array.Empty<string>(), "rebase")));
    }
}
