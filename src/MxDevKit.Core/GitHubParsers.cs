using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MxDevKit;

public enum CiState
{
    Unknown,
    None,
    Running,
    Passed,
    Failed,
}

// The checks of one commit: State sums them up, Url is the one worth opening (a failure first).
public sealed record CiRun(CiState State, string? Commit, string? Url);

public static class GitHubParsers
{
    // Open issues and pull requests, and every check of the head commit of the branch: the check runs of
    // all workflows and of the GitHub Apps (CircleCI, Vercel...), and the commit statuses (Netlify...).
    // The default branch stands in when the branch is not on GitHub or has no check at all.
    public const string RepoStatusQuery =
        "query($owner:String!,$name:String!,$branch:String!){repository(owner:$owner,name:$name){" +
        "issues(states:OPEN){totalCount} pullRequests(states:OPEN){totalCount} " +
        "ref(qualifiedName:$branch){target{...Checks}} defaultBranchRef{target{...Checks}}}} " +
        "fragment Checks on Commit{oid statusCheckRollup{state contexts(first:100){nodes{__typename " +
        "...on CheckRun{status conclusion detailsUrl checkSuite{workflowRun{url}}} " +
        "...on StatusContext{state targetUrl}}}}}";

    // The variable $branch of RepoStatusQuery. A detached HEAD has none: the default branch shows.
    public static string BranchRef(string? branch) => string.IsNullOrEmpty(branch) ? "" : "refs/heads/" + branch;

    // `gh api graphql` with RepoStatusQuery.
    public static (int Issues, int PullRequests)? RepoCounts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("repository", out var repo)
                || repo.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return (
                repo.GetProperty("issues").GetProperty("totalCount").GetInt32(),
                repo.GetProperty("pullRequests").GetProperty("totalCount").GetInt32());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // `gh api graphql` with RepoStatusQuery: the checks of the branch, else of the default branch.
    public static CiRun? CommitChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("repository", out var repo)
                || repo.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var branch = Commit(repo, "ref");
            if (branch is { HasChecks: true })
            {
                return branch.Run;
            }

            var main = Commit(repo, "defaultBranchRef");
            return main is { HasChecks: true } ? main.Run : branch?.Run ?? main?.Run ?? new CiRun(CiState.None, null, null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record CommitStatus(CiRun Run, bool HasChecks);

    private static CommitStatus? Commit(JsonElement repo, string name)
    {
        if (!repo.TryGetProperty(name, out var r) || r.ValueKind != JsonValueKind.Object
            || !r.TryGetProperty("target", out var commit) || commit.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var oid = Text(commit, "oid");
        if (!commit.TryGetProperty("statusCheckRollup", out var rollup) || rollup.ValueKind != JsonValueKind.Object)
        {
            return new CommitStatus(new CiRun(CiState.None, oid, null), false);
        }

        var checks = new List<(CiState State, string? Url)>();
        if (rollup.TryGetProperty("contexts", out var contexts) && contexts.ValueKind == JsonValueKind.Object
            && contexts.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (Text(node, "__typename") == "StatusContext")
                {
                    checks.Add((StatusState(Text(node, "state")), Text(node, "targetUrl")));
                }
                else
                {
                    var run = node.TryGetProperty("checkSuite", out var suite) && suite.ValueKind == JsonValueKind.Object
                        && suite.TryGetProperty("workflowRun", out var w) && w.ValueKind == JsonValueKind.Object ? Text(w, "url") : null;
                    checks.Add((StateOf(Text(node, "status"), Text(node, "conclusion")), run ?? Text(node, "detailsUrl")));
                }
            }
        }

        var state = Overall(checks.Select(c => c.State), Text(rollup, "state"));
        var url = checks.Where(c => c.State == CiState.Failed).Concat(checks.Where(c => c.State == CiState.Running)).Concat(checks)
            .Select(c => c.Url).FirstOrDefault(u => !string.IsNullOrEmpty(u));
        return new CommitStatus(new CiRun(state, oid, url), true);
    }

    // Red as soon as one check fails, running while one still runs, green when the others passed.
    // The rollup state of GitHub covers the checks beyond the first 100.
    public static CiState Overall(IEnumerable<CiState> checks, string? rollup = null)
    {
        var all = checks.ToList();
        var failed = all.Contains(CiState.Failed) || rollup is "FAILURE" or "ERROR";
        if (failed)
        {
            return CiState.Failed;
        }

        if (all.Contains(CiState.Running) || rollup is "PENDING" or "EXPECTED")
        {
            return CiState.Running;
        }

        return all.Contains(CiState.Passed) || (all.Count == 0 && rollup == "SUCCESS") ? CiState.Passed : CiState.None;
    }

    // A check run: status QUEUED, IN_PROGRESS, COMPLETED..., conclusion SUCCESS, FAILURE, SKIPPED...
    public static CiState StateOf(string? status, string? conclusion)
    {
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(status) ? CiState.Unknown : CiState.Running;
        }

        return conclusion?.ToLowerInvariant() switch
        {
            "success" => CiState.Passed,
            "skipped" or "neutral" or "stale" => CiState.None,
            _ => CiState.Failed,
        };
    }

    // A commit status (Netlify, Jenkins...): SUCCESS, PENDING, EXPECTED, FAILURE, ERROR.
    public static CiState StatusState(string? state) => state?.ToUpperInvariant() switch
    {
        "SUCCESS" => CiState.Passed,
        "PENDING" or "EXPECTED" => CiState.Running,
        "FAILURE" or "ERROR" => CiState.Failed,
        _ => CiState.Unknown,
    };

    // The checks have just ended: Passed or Failed, else null. Either the bubble saw them running on
    // this commit, or a push came shortly before (checks fast enough to end between two polls).
    public static CiState? Finished(CiRun? before, CiRun? now, bool afterPush = false)
    {
        if (now is not { State: CiState.Passed or CiState.Failed, Commit: not null })
        {
            return null;
        }

        if (before is { State: CiState.Running } && before.Commit == now.Commit)
        {
            return now.State;
        }

        var fresh = before == null || before.Commit != now.Commit || before.State is CiState.None or CiState.Unknown;
        return afterPush && fresh ? now.State : null;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

// Remembers the review requests already seen, so only new ones vibrate. The first list is the baseline.
public sealed class ReviewTracker
{
    private HashSet<string>? _known;

    public int Update(IReadOnlyCollection<string> urls) => this.Fresh(urls).Count;

    // The URLs that were not in the previous list.
    public IReadOnlyList<string> Fresh(IReadOnlyCollection<string> urls)
    {
        var current = new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
        var fresh = new List<string>();
        if (_known != null)
        {
            foreach (var url in current)
            {
                if (!_known.Contains(url))
                {
                    fresh.Add(url);
                }
            }
        }

        _known = current;
        return fresh;
    }

    public void Reset() => _known = null;
}
