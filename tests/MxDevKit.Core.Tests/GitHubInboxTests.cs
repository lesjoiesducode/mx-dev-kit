namespace MxDevKit.Tests;

public class GitHubInboxTests
{
    private const string Api = "https://github.com/acme/api/pull/";

    [Fact]
    public void Snapshot_reads_the_three_searches()
    {
        const string json = """
            {"data":{"viewer":{"login":"octocat"},
              "reviews":{"issueCount":3,"nodes":[
                {"url":"https://github.com/acme/web/pull/4","title":"Dark mode","number":4,"repository":{"nameWithOwner":"acme/web"}},
                {}]},
              "open":{"nodes":[
                {"url":"https://github.com/acme/api/pull/12","title":"Add search","number":12,"headRefName":"search",
                 "repository":{"nameWithOwner":"acme/api"},
                 "latestOpinionatedReviews":{"nodes":[{"state":"APPROVED","submittedAt":"2026-11-10T09:00:00Z","url":"https://github.com/acme/api/pull/12#pullrequestreview-1","author":{"login":"alice"}},
                                                      {"state":"COMMENTED","submittedAt":"2026-11-10T09:01:00Z","url":null,"author":null}]},
                 "commits":{"nodes":[{"commit":{"oid":"abc","statusCheckRollup":{"state":"FAILURE"}}}]}},
                {"url":"https://github.com/acme/api/pull/13","title":"No CI","number":13,"headRefName":"docs",
                 "repository":{"nameWithOwner":"acme/api"},"latestOpinionatedReviews":{"nodes":[]},
                 "commits":{"nodes":[{"commit":{"oid":"def","statusCheckRollup":null}}]}}]},
              "merged":{"nodes":[{"url":"https://github.com/acme/api/pull/9","title":"Fix login","number":9,"repository":{"nameWithOwner":"acme/api"}}]}}}
            """;

        var s = GitHubInbox.Snapshot(json)!;

        Assert.Equal("octocat", s.Login);
        Assert.Equal(3, s.ReviewTotal);
        Assert.Equal(new ReviewRequest("https://github.com/acme/web/pull/4", "Dark mode", 4, "acme/web"), Assert.Single(s.Reviews!));
        Assert.Equal(2, s.Open!.Count);
        Assert.Equal("search", s.Open[0].Branch);
        Assert.Equal("abc", s.Open[0].HeadOid);
        Assert.Equal("FAILURE", s.Open[0].Checks);
        Assert.Equal("alice", Assert.Single(s.Open[0].Reviews).Author);
        Assert.Null(s.Open[1].Checks);
        Assert.Equal(9, Assert.Single(s.Merged!).Number);
    }

    [Fact]
    public void Snapshot_keeps_the_parts_that_worked()
    {
        const string json = """{"data":{"viewer":{"login":"octocat"},"reviews":{"issueCount":0,"nodes":[]},"open":null,"merged":null},"errors":[{"message":"timeout"}]}""";

        var s = GitHubInbox.Snapshot(json)!;

        Assert.Equal(0, s.ReviewTotal);
        Assert.Null(s.Open);
        Assert.Null(s.Merged);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"errors":[{"message":"Bad credentials"}]}""")]
    public void Snapshot_is_null_without_data(string json) => Assert.Null(GitHubInbox.Snapshot(json));

    [Fact]
    public void MergedQuery_covers_the_last_two_days() =>
        Assert.Equal("is:pr is:merged author:@me merged:>=2026-11-08 sort:updated-desc",
            GitHubInbox.MergedQuery(new DateTime(2026, 11, 10, 0, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void Notifications_reads_status_headers_and_threads()
    {
        const string output = "HTTP/2.0 200 OK\r\nContent-Type: application/json\r\nLast-Modified: Tue, 10 Nov 2026 12:09:56 GMT\r\n" +
            "Etag: \"abc\"\r\nX-Poll-Interval: 120\r\n\r\n" +
            """
            [{"id":"1","reason":"mention","updated_at":"2026-11-10T12:00:00Z",
              "subject":{"title":"Crash on login","url":"https://api.github.com/repos/acme/api/issues/5","latest_comment_url":"https://api.github.com/repos/acme/api/issues/comments/99","type":"Issue"},
              "repository":{"full_name":"acme/api"}},
             {"id":"2","reason":"subscribed","updated_at":"2026-11-10T11:00:00Z","subject":{"title":"v2","url":null,"latest_comment_url":null,"type":"Release"},"repository":{"full_name":"acme/api"}}]
            """;

        var page = GitHubInbox.Notifications(output)!;

        Assert.Equal(200, page.Status);
        Assert.Equal("Tue, 10 Nov 2026 12:09:56 GMT", page.LastModified);
        Assert.Equal("\"abc\"", page.ETag);
        Assert.Equal(120, page.PollInterval);
        Assert.Equal(2, page.Threads!.Count);
        Assert.Equal(new NotificationThread("1", "mention", "2026-11-10T12:00:00Z", "Issue", "Crash on login", "acme/api",
            "https://api.github.com/repos/acme/api/issues/5", "https://api.github.com/repos/acme/api/issues/comments/99"), page.Threads[0]);
        Assert.Null(page.Threads[1].SubjectUrl);
    }

    [Fact]
    public void Notifications_understands_304_and_errors()
    {
        var notModified = GitHubInbox.Notifications("HTTP/2.0 304 Not Modified\nX-Poll-Interval: 60\n\n")!;
        Assert.Equal(304, notModified.Status);
        Assert.Null(notModified.Threads);
        Assert.Equal(60, notModified.PollInterval);

        Assert.Equal(403, GitHubInbox.Notifications("HTTP/2.0 403 Forbidden\n\n{\"message\":\"Resource not accessible\"}")!.Status);
        Assert.Null(GitHubInbox.Notifications("gh: not signed in"));
        Assert.Null(GitHubInbox.Notifications("HTTP/2.0 200 OK\n\nnot json"));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/acme/api/pulls/12", "https://github.com/acme/api/pull/12")]
    [InlineData("https://api.github.com/repos/acme/api/issues/5", "https://github.com/acme/api/issues/5")]
    [InlineData("https://api.github.com/repos/acme/api/commits/abc123", "https://github.com/acme/api/commit/abc123")]
    [InlineData("https://api.github.com/repos/acme/api/releases/1", "https://github.com/acme/api")]
    [InlineData(null, "https://github.com/acme/api")]
    public void HtmlUrl_turns_api_urls_into_pages(string? api, string expected) =>
        Assert.Equal(expected, GitHubInbox.HtmlUrl(api, "acme/api"));

    [Fact]
    public void HtmlUrl_without_repository_opens_the_notifications() =>
        Assert.Equal("https://github.com/notifications", GitHubInbox.HtmlUrl(null, ""));

    [Theory]
    [InlineData("https://api.github.com/repos/acme/api/pulls/12", 12)]
    [InlineData("https://api.github.com/repos/acme/api/issues/5", 5)]
    [InlineData("https://api.github.com/repos/acme/api/commits/abc", null)]
    [InlineData(null, null)]
    public void NumberOf_reads_issue_and_pull_numbers(string? api, int? expected) =>
        Assert.Equal(expected, GitHubInbox.NumberOf(api));

    [Theory]
    [InlineData("Thanks @octocat, can you look?", true)]
    [InlineData("@OctoCat ping", true)]
    [InlineData("cc @octocat.", true)]
    [InlineData("Thanks @octocat-bot", false)]
    [InlineData("mail me at me@octocat.com", false)]
    [InlineData("@acme/octocat team", false)]
    [InlineData("no mention", false)]
    public void Mentions_finds_the_login_as_GitHub_links_it(string body, bool expected) =>
        Assert.Equal(expected, GitHubInbox.Mentions(body, "octocat"));

    [Fact]
    public void Comment_reads_body_and_page()
    {
        var c = GitHubInbox.Comment("""{"body":"hi @octocat","html_url":"https://github.com/acme/api/issues/5#issuecomment-99"}""");
        Assert.Equal(("hi @octocat", "https://github.com/acme/api/issues/5#issuecomment-99"), c);
        Assert.Null(GitHubInbox.Comment("oops"));
    }

    [Fact]
    public void Tracker_baseline_does_not_report_what_was_already_there()
    {
        var tracker = new InboxTracker();
        var first = Snapshot(reviews: new[] { Review(4) }, open: new[] { Pr(12, "FAILURE", Approval("alice")) }, merged: new[] { Merged(9) });

        Assert.Empty(tracker.Update(first));
        Assert.Empty(tracker.Update(first));
    }

    [Fact]
    public void Tracker_reports_new_review_requests_reviews_failures_and_merges()
    {
        var tracker = new InboxTracker();
        tracker.Update(Snapshot(reviews: Array.Empty<ReviewRequest>(), open: new[] { Pr(12, "PENDING") }, merged: Array.Empty<MergedPullRequest>()));

        var events = tracker.Update(Snapshot(
            reviews: new[] { Review(4) },
            open: new[] { Pr(12, "FAILURE", Approval("alice"), Changes("bob")) },
            merged: new[] { Merged(9) }));

        Assert.Equal(
            new[] { InboxKind.ChangesRequested, InboxKind.CiFailed, InboxKind.ReviewRequested, InboxKind.Approved, InboxKind.Merged },
            events.Select(e => e.Kind));
        Assert.Equal("bob", events[0].Actor);
        Assert.Equal(Api + "12/checks", events[1].Url);
        Assert.Equal("branch-12", events[1].Branch);
        Assert.Equal("acme/web", events[2].Repo);
        Assert.Equal("alice", events[3].Actor);
        Assert.Equal(9, events[4].Number);
    }

    [Fact]
    public void Tracker_reports_a_red_ci_once_per_commit()
    {
        var tracker = new InboxTracker();
        tracker.Update(Snapshot(open: new[] { Pr(12, "PENDING", oid: "a") }));

        Assert.Single(tracker.Update(Snapshot(open: new[] { Pr(12, "FAILURE", oid: "a") })));
        Assert.Empty(tracker.Update(Snapshot(open: new[] { Pr(12, "FAILURE", oid: "a") })));
        Assert.Empty(tracker.Update(Snapshot(open: new[] { Pr(12, "ERROR", oid: "a") })));

        // A new push that fails again is a new failure.
        Assert.Single(tracker.Update(Snapshot(open: new[] { Pr(12, "FAILURE", oid: "b") })));
        Assert.Empty(tracker.Update(Snapshot(open: new[] { Pr(12, "SUCCESS", oid: "c") })));
    }

    [Fact]
    public void Tracker_reports_a_reviewer_changing_their_mind()
    {
        var tracker = new InboxTracker();
        tracker.Update(Snapshot(open: new[] { Pr(12, null, Changes("bob", "09:00")) }));

        var events = tracker.Update(Snapshot(open: new[] { Pr(12, null, Approval("bob", "10:00")) }));

        Assert.Equal(InboxKind.Approved, Assert.Single(events).Kind);
    }

    [Fact]
    public void Tracker_ignores_my_own_reviews_and_comments()
    {
        var tracker = new InboxTracker();
        tracker.Update(Snapshot(open: new[] { Pr(12, null) }));

        var events = tracker.Update(Snapshot(open: new[] { Pr(12, null, Approval("octocat"), new PullRequestReview("carol", "COMMENTED", "11:00", null)) }));

        Assert.Empty(events);
    }

    [Fact]
    public void Tracker_keeps_its_state_when_a_part_is_missing()
    {
        var tracker = new InboxTracker();
        tracker.Update(Snapshot(merged: new[] { Merged(9) }));

        // The merged search failed once: no state lost, so the old merge does not come back as new.
        tracker.Update(Snapshot(merged: null));

        Assert.Empty(tracker.Update(Snapshot(merged: new[] { Merged(9) })));
        Assert.Single(tracker.Update(Snapshot(merged: new[] { Merged(9), Merged(10) })));
    }

    [Fact]
    public void Tracker_reports_a_new_pull_request_already_red()
    {
        var tracker = new InboxTracker();
        tracker.Update(Snapshot(open: Array.Empty<MyPullRequest>()));

        Assert.Equal(InboxKind.CiFailed, Assert.Single(tracker.Update(Snapshot(open: new[] { Pr(14, "FAILURE") }))).Kind);
    }

    [Fact]
    public void NotificationTracker_reports_new_mentions_and_assignments_after_the_baseline()
    {
        var tracker = new NotificationTracker();
        Assert.Empty(tracker.Update(new[] { Thread("1", "mention", "t1") }));

        var found = tracker.Update(new[]
        {
            Thread("1", "mention", "t1"),
            Thread("2", "team_mention", "t1"),
            Thread("3", "assign", "t1"),
            Thread("4", "subscribed", "t1"),
            Thread("5", "review_requested", "t1"),
            Thread("6", "ci_activity", "t1"),
        });

        Assert.Equal(new[] { ("2", InboxKind.TeamMention, false), ("3", InboxKind.Assigned, false) },
            found.Select(f => (f.Thread.Id, f.Kind, f.CheckComment)));
    }

    [Fact]
    public void NotificationTracker_checks_the_comment_when_a_mention_thread_moves()
    {
        var tracker = new NotificationTracker();
        tracker.Update(new[] { Thread("1", "mention", "t1"), Thread("2", "assign", "t1"), Thread("3", "subscribed", "t1") });

        var found = tracker.Update(new[] { Thread("1", "mention", "t2"), Thread("2", "assign", "t2"), Thread("3", "mention", "t2") });

        // 1: same reason, new activity: only if the last comment mentions you. 2: an assigned thread
        // with a new comment is not a new assignment. 3: you were just mentioned in a thread you followed.
        Assert.Equal(new[] { ("1", true), ("3", false) }, found.Select(f => (f.Thread.Id, f.CheckComment)));
        Assert.Empty(tracker.Update(new[] { Thread("1", "mention", "t2") }));
    }

    [Fact]
    public void Events_can_be_turned_off_one_by_one()
    {
        var config = DevKitConfig.Parse("""{ "github": { "events": { "merged": false, "TEAMMENTION": false } } }""");

        Assert.False(config.GitHub.Events.IsOn(InboxKind.Merged));
        Assert.False(config.GitHub.Events.IsOn(InboxKind.TeamMention));
        Assert.True(config.GitHub.Events.IsOn(InboxKind.Mention));
        Assert.True(config.GitHub.Events.IsOn(InboxKind.CiFailed));
    }

    [Fact]
    public void NotifyReviews_from_1_0_still_turns_review_requests_off()
    {
        Assert.False(DevKitConfig.Parse("""{ "github": { "notifyReviews": false } }""").GitHub.Events.ReviewRequested);
        Assert.True(DevKitConfig.Parse("""{ "github": { "events": null } }""").GitHub.Events.ReviewRequested);
    }

    [Theory]
    [InlineData(InboxKind.ReviewRequested, "knock")]
    [InlineData(InboxKind.Mention, "knock")]
    [InlineData(InboxKind.TeamMention, "knock")]
    [InlineData(InboxKind.Assigned, "knock")]
    [InlineData(InboxKind.Approved, "happy_alert")]
    [InlineData(InboxKind.Merged, "happy_alert")]
    [InlineData(InboxKind.ChangesRequested, "angry_alert")]
    [InlineData(InboxKind.CiFailed, "angry_alert")]
    public void Each_kind_has_its_vibration(InboxKind kind, string waveform) =>
        Assert.Equal(waveform, HapticEvents.WaveformFor(HapticEvents.For(kind), null));

    [Fact]
    public void Notification_texts_say_what_and_where()
    {
        var e = new InboxEvent(InboxKind.Approved, "acme/api", 12, "Add search", Api + "12") { Actor = "alice" };

        Assert.Equal("Approved by alice", Strings.English.InboxTitle(e));
        Assert.Equal("Approuvée par alice", Strings.French.InboxTitle(e));
        Assert.Equal("acme/api#12 Add search", Strings.InboxText(e));
        Assert.Equal("acme/api", Strings.InboxText(e with { Number = null, Title = "" }));
        Assert.Equal("4 more GitHub events", Strings.English.InboxMoreText(4));
    }

    [Fact]
    public void Inbox_bubble_shows_the_last_event_until_it_is_opened()
    {
        var merged = new InboxEvent(InboxKind.Merged, "acme/api", 12, "Add search", Api + "12");

        var fresh = Bubbles.Inbox(merged, seen: false, available: true, Strings.English);
        Assert.Equal(Glyph.PullRequest, fresh.Glyph);
        Assert.Equal("#12", fresh.Value);
        Assert.Equal(Palette.Purple, fresh.Background);
        Assert.Equal("GitHub: Pull request merged acme/api#12 Add search", fresh.Label);

        Assert.Equal(Palette.Grey, Bubbles.Inbox(merged, seen: true, available: true, Strings.English).Background);
        Assert.Equal(Palette.Red, Bubbles.Inbox(merged with { Kind = InboxKind.CiFailed }, false, true, Strings.English).Background);
        Assert.Equal(Glyph.At, Bubbles.Inbox(merged with { Kind = InboxKind.Mention }, false, true, Strings.English).Glyph);
    }

    [Fact]
    public void Inbox_bubble_when_empty_or_unavailable()
    {
        var empty = Bubbles.Inbox(null, false, true, Strings.English);
        Assert.Equal(Glyph.Bell, empty.Glyph);
        Assert.Equal("", empty.Value);

        Assert.Equal(Bubbles.None, Bubbles.Inbox(null, false, false, Strings.English).Value);
    }

    [Fact]
    public void Inbox_bubble_label_stays_short()
    {
        var e = new InboxEvent(InboxKind.Mention, "acme/a-very-long-repository-name", 1234, new string('x', 200), "u");
        Assert.True(Bubbles.Inbox(e, false, true, Strings.English).Label.Length <= 60);
    }

    private static InboxSnapshot Snapshot(
        ReviewRequest[]? reviews = null, MyPullRequest[]? open = null, MergedPullRequest[]? merged = null) =>
        new("octocat", reviews?.Length, reviews, open, merged);

    private static ReviewRequest Review(int n) => new($"https://github.com/acme/web/pull/{n}", "Dark mode", n, "acme/web");

    private static MyPullRequest Pr(int n, string? checks, params PullRequestReview[] reviews) => Pr(n, checks, "sha", reviews);

    private static MyPullRequest Pr(int n, string? checks, string oid, params PullRequestReview[] reviews) =>
        new(Api + n, "Add search", n, "acme/api", "branch-" + n, oid, checks, reviews);

    private static MergedPullRequest Merged(int n) => new(Api + n, "Fix login", n, "acme/api");

    private static PullRequestReview Approval(string who, string at = "09:00") => new(who, "APPROVED", at, Api + "12#review-" + who);

    private static PullRequestReview Changes(string who, string at = "09:30") => new(who, "CHANGES_REQUESTED", at, null);

    private static NotificationThread Thread(string id, string reason, string updated) =>
        new(id, reason, updated, "Issue", "Title " + id, "acme/api", "https://api.github.com/repos/acme/api/issues/" + id, null);
}
