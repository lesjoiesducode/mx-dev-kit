using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MxDevKit;

// What the GitHub inbox reports. The order is the priority: when several things happen in the same
// poll, the first one gives the vibration and the bubble.
public enum InboxKind
{
    ChangesRequested,
    CiFailed,
    ReviewRequested,
    Mention,
    TeamMention,
    Assigned,
    Approved,
    Merged,
}

// One thing that happened on GitHub, worth a vibration and a notification.
public sealed record InboxEvent(InboxKind Kind, string Repo, int? Number, string Title, string Url)
{
    // Who approved or asked for changes.
    public string? Actor { get; init; }

    // Head branch of the pull request (CI failures), to avoid vibrating twice with the CI bubble.
    public string? Branch { get; init; }
}

public sealed record ReviewRequest(string Url, string Title, int? Number, string Repo);

public sealed record PullRequestReview(string Author, string State, string SubmittedAt, string? Url);

public sealed record MyPullRequest(string Url, string Title, int? Number, string Repo, string? Branch, string? HeadOid, string? Checks, IReadOnlyList<PullRequestReview> Reviews);

public sealed record MergedPullRequest(string Url, string Title, int? Number, string Repo);

// One GraphQL call: review requests (the review bubble), my open pull requests, my recently merged ones.
// A part that failed is null, so the trackers keep what they knew about it.
public sealed record InboxSnapshot(
    string? Login,
    int? ReviewTotal,
    IReadOnlyList<ReviewRequest>? Reviews,
    IReadOnlyList<MyPullRequest>? Open,
    IReadOnlyList<MergedPullRequest>? Merged);

// A thread of GET /notifications.
public sealed record NotificationThread(string Id, string Reason, string UpdatedAt, string Type, string Title, string Repo, string? SubjectUrl, string? LatestCommentUrl);

// GET /notifications read with `gh api -i`: status, the headers that drive the polling, the threads (200 only).
public sealed record NotificationsPage(int Status, string? LastModified, string? ETag, int? PollInterval, IReadOnlyList<NotificationThread>? Threads);

public static class GitHubInbox
{
    public const string Query =
        "query($reviews:String!,$open:String!,$merged:String!){viewer{login} " +
        "reviews:search(query:$reviews,type:ISSUE,first:50){issueCount nodes{... on PullRequest{url title number repository{nameWithOwner}}}} " +
        "open:search(query:$open,type:ISSUE,first:50){nodes{... on PullRequest{url title number headRefName repository{nameWithOwner} " +
        "latestOpinionatedReviews(first:20){nodes{state submittedAt url author{login}}} " +
        "commits(last:1){nodes{commit{oid statusCheckRollup{state}}}}}}} " +
        "merged:search(query:$merged,type:ISSUE,first:20){nodes{... on PullRequest{url title number repository{nameWithOwner}}}}}";

    public const string OpenQuery = "is:open is:pr author:@me archived:false sort:updated-desc";

    public const string NotificationsPath = "notifications?per_page=50";

    // Reasons of GET /notifications that the inbox reports. Review requests come from the search
    // (the same query as the review bubble), CI from the pull requests themselves.
    public static readonly IReadOnlyDictionary<string, InboxKind> Reasons = new Dictionary<string, InboxKind>(StringComparer.Ordinal)
    {
        ["mention"] = InboxKind.Mention,
        ["team_mention"] = InboxKind.TeamMention,
        ["assign"] = InboxKind.Assigned,
    };

    // Merged pull requests are searched over the last two days; the tracker keeps the ones already seen.
    public static string MergedQuery(DateTime nowUtc) =>
        "is:pr is:merged author:@me merged:>=" + nowUtc.AddDays(-2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " sort:updated-desc";

    public static InboxSnapshot? Snapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var login = data.TryGetProperty("viewer", out var viewer) ? Text(viewer, "login") : null;

            int? reviewTotal = null;
            List<ReviewRequest>? reviews = null;
            if (Search(data, "reviews") is { } r)
            {
                reviews = new List<ReviewRequest>();
                foreach (var node in Nodes(r))
                {
                    if (Text(node, "url") is { } url)
                    {
                        reviews.Add(new ReviewRequest(url, Text(node, "title") ?? "", Int(node, "number"), RepoOf(node)));
                    }
                }

                reviewTotal = r.TryGetProperty("issueCount", out var c) && c.TryGetInt32(out var n) ? Math.Max(n, reviews.Count) : reviews.Count;
            }

            List<MyPullRequest>? open = null;
            if (Search(data, "open") is { } o)
            {
                open = new List<MyPullRequest>();
                foreach (var node in Nodes(o))
                {
                    if (Text(node, "url") is not { } url)
                    {
                        continue;
                    }

                    var list = new List<PullRequestReview>();
                    if (node.TryGetProperty("latestOpinionatedReviews", out var lr))
                    {
                        foreach (var review in Nodes(lr))
                        {
                            var author = review.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object ? Text(a, "login") : null;
                            if (author != null && Text(review, "state") is { } state)
                            {
                                list.Add(new PullRequestReview(author, state, Text(review, "submittedAt") ?? "", Text(review, "url")));
                            }
                        }
                    }

                    string? oid = null, checks = null;
                    if (node.TryGetProperty("commits", out var commits) && Nodes(commits).FirstOrDefault() is { ValueKind: JsonValueKind.Object } last
                        && last.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.Object)
                    {
                        oid = Text(commit, "oid");
                        checks = commit.TryGetProperty("statusCheckRollup", out var rollup) && rollup.ValueKind == JsonValueKind.Object ? Text(rollup, "state") : null;
                    }

                    open.Add(new MyPullRequest(url, Text(node, "title") ?? "", Int(node, "number"), RepoOf(node), Text(node, "headRefName"), oid, checks, list));
                }
            }

            List<MergedPullRequest>? merged = null;
            if (Search(data, "merged") is { } m)
            {
                merged = new List<MergedPullRequest>();
                foreach (var node in Nodes(m))
                {
                    if (Text(node, "url") is { } url)
                    {
                        merged.Add(new MergedPullRequest(url, Text(node, "title") ?? "", Int(node, "number"), RepoOf(node)));
                    }
                }
            }

            return new InboxSnapshot(login, reviewTotal, reviews, open, merged);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // `gh api -i notifications`: status line, headers, empty line, body. gh exits with 1 on a 304 but
    // still prints the status line, so the output is read whatever the exit code.
    public static NotificationsPage? Notifications(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var text = output.Replace("\r\n", "\n");
        var split = text.IndexOf("\n\n", StringComparison.Ordinal);
        var head = split < 0 ? text : text[..split];
        var body = split < 0 ? "" : text[(split + 2)..];
        var lines = head.Split('\n');

        var status = Regex.Match(lines[0], @"^HTTP/\S+\s+(\d{3})");
        if (!status.Success)
        {
            return null;
        }

        string? lastModified = null, etag = null;
        int? poll = null;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase))
            {
                lastModified = value;
            }
            else if (name.Equals("ETag", StringComparison.OrdinalIgnoreCase))
            {
                etag = value;
            }
            else if (name.Equals("X-Poll-Interval", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                poll = p;
            }
        }

        var code = int.Parse(status.Groups[1].Value, CultureInfo.InvariantCulture);
        IReadOnlyList<NotificationThread>? threads = null;
        if (code == 200)
        {
            threads = Threads(body);
            if (threads == null)
            {
                return null;
            }
        }

        return new NotificationsPage(code, lastModified, etag, poll, threads);
    }

    private static List<NotificationThread>? Threads(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var threads = new List<NotificationThread>();
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object || Text(t, "id") is not { } id || !t.TryGetProperty("subject", out var subject))
                {
                    continue;
                }

                var repo = t.TryGetProperty("repository", out var r) && r.ValueKind == JsonValueKind.Object ? Text(r, "full_name") ?? "" : "";
                threads.Add(new NotificationThread(
                    id,
                    Text(t, "reason") ?? "",
                    Text(t, "updated_at") ?? "",
                    Text(subject, "type") ?? "",
                    Text(subject, "title") ?? "",
                    repo,
                    Text(subject, "url"),
                    Text(subject, "latest_comment_url")));
            }

            return threads;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // A comment fetched from latest_comment_url: its text and its page.
    public static (string Body, string? HtmlUrl)? Comment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? (Text(doc.RootElement, "body") ?? "", Text(doc.RootElement, "html_url"))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // @login as GitHub links it: not inside an e-mail address, not the start of a longer login.
    public static bool Mentions(string? body, string? login) =>
        !string.IsNullOrEmpty(body) && !string.IsNullOrEmpty(login)
        && Regex.IsMatch(body, @"(?<![\w@/.-])@" + Regex.Escape(login) + @"(?![\w-])", RegexOptions.IgnoreCase);

    // https://api.github.com/repos/o/r/pulls/12 -> https://github.com/o/r/pull/12 (issues, commits too).
    public static string HtmlUrl(string? apiUrl, string? repo)
    {
        var m = Regex.Match(apiUrl ?? "", @"^https://api\.github\.com/repos/([^/]+/[^/]+)/(issues|pulls|commits)/([^/?#]+)$");
        if (m.Success)
        {
            var kind = m.Groups[2].Value switch { "pulls" => "pull", "commits" => "commit", _ => "issues" };
            return $"https://github.com/{m.Groups[1].Value}/{kind}/{m.Groups[3].Value}";
        }

        return string.IsNullOrEmpty(repo) ? "https://github.com/notifications" : $"https://github.com/{repo}";
    }

    public static int? NumberOf(string? apiUrl)
    {
        var m = Regex.Match(apiUrl ?? "", @"/(?:issues|pulls)/(\d+)$");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    private static JsonElement? Search(JsonElement data, string name) =>
        data.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.Object ? s : null;

    private static IEnumerable<JsonElement> Nodes(JsonElement parent) =>
        parent.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array
            ? nodes.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.Object).ToList()
            : Enumerable.Empty<JsonElement>();

    private static string RepoOf(JsonElement node) =>
        node.TryGetProperty("repository", out var r) && r.ValueKind == JsonValueKind.Object ? Text(r, "nameWithOwner") ?? "" : "";

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : null;
}

// Turns successive snapshots into events. The first snapshot is the baseline: what was already
// there when the plugin started does not vibrate. A part missing from a snapshot keeps its state.
public sealed class InboxTracker
{
    private const int MaxRemembered = 2000;

    private readonly ReviewTracker _reviews = new();
    private readonly HashSet<string> _reviewsSeen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string? Oid, bool Failing)> _checks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _merged = new(StringComparer.Ordinal);
    private bool _openKnown;
    private bool _mergedKnown;

    public IReadOnlyList<InboxEvent> Update(InboxSnapshot snapshot)
    {
        var events = new List<InboxEvent>();

        if (snapshot.Reviews != null)
        {
            var fresh = new HashSet<string>(_reviews.Fresh(snapshot.Reviews.Select(r => r.Url).ToList()), StringComparer.OrdinalIgnoreCase);
            events.AddRange(snapshot.Reviews.Where(r => fresh.Contains(r.Url))
                .Select(r => new InboxEvent(InboxKind.ReviewRequested, r.Repo, r.Number, r.Title, r.Url)));
        }

        if (snapshot.Open != null)
        {
            var report = _openKnown;
            var stillOpen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pr in snapshot.Open)
            {
                stillOpen.Add(pr.Url);
                var known = _checks.TryGetValue(pr.Url, out var before);

                foreach (var review in pr.Reviews)
                {
                    var key = pr.Url + "|" + review.Author + "|" + review.State + "|" + review.SubmittedAt;
                    var kind = review.State switch
                    {
                        "APPROVED" => InboxKind.Approved,
                        "CHANGES_REQUESTED" => InboxKind.ChangesRequested,
                        _ => (InboxKind?)null,
                    };
                    if (_reviewsSeen.Add(key) && report && kind != null
                        && !string.Equals(review.Author, snapshot.Login, StringComparison.OrdinalIgnoreCase))
                    {
                        events.Add(new InboxEvent(kind.Value, pr.Repo, pr.Number, pr.Title, review.Url ?? pr.Url) { Actor = review.Author });
                    }
                }

                var failing = pr.Checks is "FAILURE" or "ERROR";
                var wasFailing = known && before.Oid == pr.HeadOid && before.Failing;
                if (report && failing && !wasFailing)
                {
                    events.Add(new InboxEvent(InboxKind.CiFailed, pr.Repo, pr.Number, pr.Title, pr.Url + "/checks") { Branch = pr.Branch });
                }

                _checks[pr.Url] = (pr.HeadOid, failing);
            }

            foreach (var gone in _checks.Keys.Where(k => !stillOpen.Contains(k)).ToList())
            {
                _checks.Remove(gone);
            }

            if (_reviewsSeen.Count > MaxRemembered)
            {
                _reviewsSeen.RemoveWhere(k => !stillOpen.Contains(k[..k.IndexOf('|')]));
            }

            _openKnown = true;
        }

        if (snapshot.Merged != null)
        {
            foreach (var pr in snapshot.Merged)
            {
                if (_merged.Add(pr.Url) && _mergedKnown)
                {
                    events.Add(new InboxEvent(InboxKind.Merged, pr.Repo, pr.Number, pr.Title, pr.Url));
                }
            }

            // The search covers two days: what left it will not come back.
            _merged.IntersectWith(snapshot.Merged.Select(p => p.Url));
            _mergedKnown = true;
        }

        return events.OrderBy(e => e.Kind).ToList();
    }
}

// Threads of GET /notifications that are new since the last poll. Nothing is ever marked as read on
// GitHub: threads are told apart by id and updated_at. `reason` says why you follow a thread, not what
// the last comment did, so a thread you were once mentioned in keeps the reason "mention": a later
// update only counts if its last comment mentions you again (CheckComment, checked by the caller).
public sealed class NotificationTracker
{
    private const int MaxRemembered = 5000;

    private readonly Dictionary<string, (string Reason, string UpdatedAt)> _known = new(StringComparer.Ordinal);
    private bool _baseline;

    public IReadOnlyList<(NotificationThread Thread, InboxKind Kind, bool CheckComment)> Update(IReadOnlyList<NotificationThread> threads)
    {
        var found = new List<(NotificationThread, InboxKind, bool)>();
        foreach (var t in threads)
        {
            var seen = _known.TryGetValue(t.Id, out var before);
            if (seen && before.UpdatedAt == t.UpdatedAt && before.Reason == t.Reason)
            {
                continue;
            }

            if (_baseline && GitHubInbox.Reasons.TryGetValue(t.Reason, out var kind))
            {
                if (!seen || before.Reason != t.Reason)
                {
                    found.Add((t, kind, false));
                }
                else if (kind == InboxKind.Mention)
                {
                    found.Add((t, kind, true));
                }
            }

            _known[t.Id] = (t.Reason, t.UpdatedAt);
        }

        if (_known.Count > MaxRemembered)
        {
            var current = new HashSet<string>(threads.Select(t => t.Id));
            foreach (var id in _known.Keys.Where(k => !current.Contains(k)).ToList())
            {
                _known.Remove(id);
            }
        }

        _baseline = true;
        return found;
    }
}
