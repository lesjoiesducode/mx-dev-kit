namespace MxDevKit.Tests;

public class GitHubParsersTests
{
    [Fact]
    public void RepoCounts_reads_issues_and_pull_requests()
    {
        const string json = """{"data":{"repository":{"issues":{"totalCount":7},"pullRequests":{"totalCount":2}}}}""";
        Assert.Equal((7, 2), GitHubParsers.RepoCounts(json));
    }

    [Theory]
    [InlineData("""{"data":{"repository":null},"errors":[{"message":"Could not resolve to a Repository"}]}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void RepoCounts_is_null_on_errors(string json) => Assert.Null(GitHubParsers.RepoCounts(json));

    [Theory]
    [InlineData("completed", "success", CiState.Passed)]
    [InlineData("completed", "failure", CiState.Failed)]
    [InlineData("completed", "cancelled", CiState.Failed)]
    [InlineData("completed", "timed_out", CiState.Failed)]
    [InlineData("COMPLETED", "STARTUP_FAILURE", CiState.Failed)]
    [InlineData("COMPLETED", "ACTION_REQUIRED", CiState.Failed)]
    [InlineData("completed", "skipped", CiState.None)]
    [InlineData("completed", "neutral", CiState.None)]
    [InlineData("COMPLETED", "STALE", CiState.None)]
    [InlineData("in_progress", "", CiState.Running)]
    [InlineData("QUEUED", null, CiState.Running)]
    [InlineData("waiting", null, CiState.Running)]
    [InlineData(null, null, CiState.Unknown)]
    public void StateOf_maps_status_and_conclusion(string? status, string? conclusion, CiState expected) =>
        Assert.Equal(expected, GitHubParsers.StateOf(status, conclusion));

    [Theory]
    [InlineData("SUCCESS", CiState.Passed)]
    [InlineData("PENDING", CiState.Running)]
    [InlineData("EXPECTED", CiState.Running)]
    [InlineData("FAILURE", CiState.Failed)]
    [InlineData("ERROR", CiState.Failed)]
    public void StatusState_maps_commit_statuses(string state, CiState expected) =>
        Assert.Equal(expected, GitHubParsers.StatusState(state));

    // The answer of RepoStatusQuery, with the given contexts on the branch and on the default branch.
    private static string Status(string? branchContexts, string? mainContexts = null, string branchRollup = "SUCCESS")
    {
        static string Commit(string oid, string? contexts, string rollup) => contexts == null
            ? "{\"target\":{\"oid\":\"" + oid + "\",\"statusCheckRollup\":null}}"
            : "{\"target\":{\"oid\":\"" + oid + "\",\"statusCheckRollup\":{\"state\":\"" + rollup + "\",\"contexts\":{\"nodes\":[" + contexts + "]}}}}";

        return "{\"data\":{\"repository\":{\"issues\":{\"totalCount\":3},\"pullRequests\":{\"totalCount\":1},\"ref\":"
            + Commit("b1", branchContexts, branchRollup) + ",\"defaultBranchRef\":" + Commit("m1", mainContexts, "SUCCESS") + "}}}";
    }

    private static string Quoted(string? s) => s == null ? "null" : "\"" + s + "\"";

    private static string Check(string status, string? conclusion, string run) =>
        "{\"__typename\":\"CheckRun\",\"status\":\"" + status + "\",\"conclusion\":" + Quoted(conclusion)
        + ",\"detailsUrl\":\"https://github.com/o/r/actions/runs/" + run + "/job/9\",\"checkSuite\":{\"workflowRun\":{\"url\":\"https://github.com/o/r/actions/runs/" + run + "\"}}}";

    private static string AppCheck(string status, string? conclusion, string url) =>
        "{\"__typename\":\"CheckRun\",\"status\":\"" + status + "\",\"conclusion\":" + Quoted(conclusion)
        + ",\"detailsUrl\":\"" + url + "\",\"checkSuite\":{\"workflowRun\":null}}";

    private static string Commit(string state, string url) =>
        "{\"__typename\":\"StatusContext\",\"state\":\"" + state + "\",\"targetUrl\":\"" + url + "\"}";

    [Fact]
    public void RepoCounts_reads_the_status_answer() =>
        Assert.Equal((3, 1), GitHubParsers.RepoCounts(Status(Check("COMPLETED", "SUCCESS", "1"))));

    [Fact]
    public void Checks_pass_when_every_workflow_and_status_passed()
    {
        var json = Status(string.Join(",", Check("COMPLETED", "SUCCESS", "1"), Check("COMPLETED", "SKIPPED", "2"), Commit("SUCCESS", "https://app.netlify.com/x")));

        Assert.Equal(new CiRun(CiState.Passed, "b1", "https://github.com/o/r/actions/runs/1"), GitHubParsers.CommitChecks(json));
    }

    [Fact]
    public void One_failed_workflow_makes_the_commit_red_and_is_the_one_to_open()
    {
        var json = Status(string.Join(",", Check("COMPLETED", "SUCCESS", "1"), Check("IN_PROGRESS", null, "2"), Check("COMPLETED", "FAILURE", "3")), branchRollup: "FAILURE");

        Assert.Equal(new CiRun(CiState.Failed, "b1", "https://github.com/o/r/actions/runs/3"), GitHubParsers.CommitChecks(json));
    }

    [Fact]
    public void A_failed_status_of_another_service_makes_the_commit_red()
    {
        var json = Status(string.Join(",", Check("COMPLETED", "SUCCESS", "1"), Commit("ERROR", "https://vercel.com/o/r/deploy")));

        Assert.Equal(new CiRun(CiState.Failed, "b1", "https://vercel.com/o/r/deploy"), GitHubParsers.CommitChecks(json));
    }

    [Fact]
    public void One_running_check_keeps_the_commit_running()
    {
        var json = Status(string.Join(",", Check("COMPLETED", "SUCCESS", "1"), AppCheck("QUEUED", null, "https://app.circleci.com/x")), branchRollup: "PENDING");

        Assert.Equal(new CiRun(CiState.Running, "b1", "https://app.circleci.com/x"), GitHubParsers.CommitChecks(json));
    }

    [Fact]
    public void The_rollup_covers_the_checks_beyond_the_first_hundred() =>
        Assert.Equal(CiState.Failed, GitHubParsers.CommitChecks(Status(Check("COMPLETED", "SUCCESS", "1"), branchRollup: "FAILURE"))!.State);

    [Fact]
    public void A_branch_without_checks_shows_the_default_branch()
    {
        var json = Status(null, Check("COMPLETED", "FAILURE", "7"));

        Assert.Equal(new CiRun(CiState.Failed, "m1", "https://github.com/o/r/actions/runs/7"), GitHubParsers.CommitChecks(json));
    }

    [Fact]
    public void A_branch_unknown_to_github_shows_the_default_branch()
    {
        const string json = """{"data":{"repository":{"issues":{"totalCount":0},"pullRequests":{"totalCount":0},"ref":null,"defaultBranchRef":{"target":{"oid":"m1","statusCheckRollup":{"state":"SUCCESS","contexts":{"nodes":[]}}}}}}}""";

        Assert.Equal(new CiRun(CiState.Passed, "m1", null), GitHubParsers.CommitChecks(json));
    }

    [Fact]
    public void No_check_anywhere_means_no_ci() =>
        Assert.Equal(new CiRun(CiState.None, "b1", null), GitHubParsers.CommitChecks(Status(null)));

    [Theory]
    [InlineData("""{"data":{"repository":null},"errors":[{"message":"Could not resolve to a Repository"}]}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void CommitChecks_is_null_on_errors(string json) => Assert.Null(GitHubParsers.CommitChecks(json));

    [Theory]
    [InlineData(null, "")]
    [InlineData("feature/search", "refs/heads/feature/search")]
    public void BranchRef_names_the_branch_for_graphql(string? branch, string expected) =>
        Assert.Equal(expected, GitHubParsers.BranchRef(branch));

    [Fact]
    public void Finished_reports_the_end_of_the_checks_that_were_running()
    {
        var running = new CiRun(CiState.Running, "a", null);

        Assert.Equal(CiState.Passed, GitHubParsers.Finished(running, new CiRun(CiState.Passed, "a", null)));
        Assert.Equal(CiState.Failed, GitHubParsers.Finished(running, new CiRun(CiState.Failed, "a", null)));
    }

    [Fact]
    public void Finished_ignores_other_commits_and_steady_states()
    {
        Assert.Null(GitHubParsers.Finished(null, new CiRun(CiState.Passed, "a", null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Passed, "a", null), new CiRun(CiState.Passed, "a", null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Running, "a", null), new CiRun(CiState.Passed, "b", null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Running, "a", null), new CiRun(CiState.Running, "a", null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Running, "a", null), null));
    }

    [Fact]
    public void After_a_push_checks_that_ended_between_two_polls_still_count()
    {
        // Seen as nothing yet, then already green; or a new commit already red.
        Assert.Equal(CiState.Passed, GitHubParsers.Finished(new CiRun(CiState.None, "b", null), new CiRun(CiState.Passed, "b", null), afterPush: true));
        Assert.Equal(CiState.Failed, GitHubParsers.Finished(new CiRun(CiState.Passed, "a", null), new CiRun(CiState.Failed, "b", null), afterPush: true));

        // The same result seen twice vibrates once.
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Passed, "b", null), new CiRun(CiState.Passed, "b", null), afterPush: true));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.None, "b", null), new CiRun(CiState.Passed, "b", null)));
    }

    [Fact]
    public void ReviewTracker_counts_only_new_requests_after_the_first_list()
    {
        var tracker = new ReviewTracker();

        Assert.Equal(0, tracker.Update(new[] { "a", "b" }));
        Assert.Equal(0, tracker.Update(new[] { "a", "b" }));
        Assert.Equal(1, tracker.Update(new[] { "a", "b", "c" }));
        Assert.Equal(0, tracker.Update(new[] { "c" }));
        Assert.Equal(1, tracker.Update(new[] { "c", "a" }));
    }
}
