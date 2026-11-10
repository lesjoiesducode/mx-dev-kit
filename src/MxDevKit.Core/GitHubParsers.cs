using System;
using System.Collections.Generic;
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

public sealed record CiRun(CiState State, long? Id, string? Url);

public static class GitHubParsers
{
    public const string RepoCountsQuery =
        "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){" +
        "issues(states:OPEN){totalCount} pullRequests(states:OPEN){totalCount}}}";

    public const string RunFields = "databaseId,status,conclusion,url";

    // `gh api graphql` with RepoCountsQuery.
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

    // `gh run list --json databaseId,status,conclusion,url -L 1`
    public static CiRun? LatestRun(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (doc.RootElement.GetArrayLength() == 0)
            {
                return new CiRun(CiState.None, null, null);
            }

            var run = doc.RootElement[0];
            var status = Text(run, "status");
            var conclusion = Text(run, "conclusion");
            long? id = run.TryGetProperty("databaseId", out var d) && d.TryGetInt64(out var v) ? v : null;
            return new CiRun(StateOf(status, conclusion), id, Text(run, "url"));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static CiState StateOf(string? status, string? conclusion)
    {
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(status) ? CiState.Unknown : CiState.Running;
        }

        return conclusion?.ToLowerInvariant() switch
        {
            "success" => CiState.Passed,
            "skipped" or "neutral" => CiState.None,
            _ => CiState.Failed,
        };
    }

    // The run the bubble was showing as running has just finished: Passed or Failed, else null.
    public static CiState? Finished(CiRun? before, CiRun? now)
    {
        if (before is not { State: CiState.Running, Id: not null } || now is null || now.Id != before.Id)
        {
            return null;
        }

        return now.State is CiState.Passed or CiState.Failed ? now.State : null;
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
