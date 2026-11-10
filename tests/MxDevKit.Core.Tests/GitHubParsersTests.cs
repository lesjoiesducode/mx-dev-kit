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
    [InlineData("completed", "skipped", CiState.None)]
    [InlineData("completed", "neutral", CiState.None)]
    [InlineData("in_progress", "", CiState.Running)]
    [InlineData("queued", null, CiState.Running)]
    [InlineData("waiting", null, CiState.Running)]
    [InlineData(null, null, CiState.Unknown)]
    public void StateOf_maps_status_and_conclusion(string? status, string? conclusion, CiState expected) =>
        Assert.Equal(expected, GitHubParsers.StateOf(status, conclusion));

    [Fact]
    public void LatestRun_reads_the_first_run()
    {
        const string json = """[{"conclusion":"","databaseId":42719876846,"status":"in_progress","url":"https://github.com/octo-org/api/actions/runs/42719876846"}]""";

        var run = GitHubParsers.LatestRun(json);

        Assert.Equal(new CiRun(CiState.Running, 42719876846, "https://github.com/octo-org/api/actions/runs/42719876846"), run);
    }

    [Fact]
    public void LatestRun_of_an_empty_list_means_no_ci() =>
        Assert.Equal(CiState.None, GitHubParsers.LatestRun("[]")!.State);

    [Fact]
    public void Finished_reports_the_end_of_the_run_that_was_running()
    {
        var running = new CiRun(CiState.Running, 10, null);

        Assert.Equal(CiState.Passed, GitHubParsers.Finished(running, new CiRun(CiState.Passed, 10, null)));
        Assert.Equal(CiState.Failed, GitHubParsers.Finished(running, new CiRun(CiState.Failed, 10, null)));
    }

    [Fact]
    public void Finished_ignores_other_runs_and_steady_states()
    {
        Assert.Null(GitHubParsers.Finished(null, new CiRun(CiState.Passed, 10, null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Passed, 10, null), new CiRun(CiState.Passed, 10, null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Running, 10, null), new CiRun(CiState.Passed, 11, null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Running, 10, null), new CiRun(CiState.Running, 10, null)));
        Assert.Null(GitHubParsers.Finished(new CiRun(CiState.Running, 10, null), null));
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
