using System;
using System.Collections.Generic;
using System.Linq;

namespace MxDevKit;

// What can stop the Commit bubble before it runs `git add -A`: a merge conflict, a secret file
// that would be committed. On the default branch it asks for a second click instead.
public enum CommitBlock
{
    None,
    Unmerged,
    Operation,
    Markers,
    Sensitive,
}

public sealed record CommitCheck(CommitBlock Block, IReadOnlyList<string> Files, string? Operation = null)
{
    public static readonly CommitCheck Allowed = new(CommitBlock.None, Array.Empty<string>());

    public bool Ok => this.Block == CommitBlock.None;
}

// One entry of `git status --porcelain=v1 -z`. OldPath is set for renames and copies.
public sealed record StatusEntry(string Xy, string Path, string? OldPath)
{
    public bool Unmerged => Xy is "DD" or "AU" or "UD" or "UA" or "DU" or "AA" or "UU";

    public bool Untracked => Xy == "??";

    // Deleted on either side: `git add -A` records the removal, no content goes into the commit.
    public bool Deleted => Xy.Length == 2 && (Xy[0] == 'D' || Xy[1] == 'D');
}

public static class CommitGuard
{
    // Second click on the default branch: within this window, or the bubble goes back to normal.
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(5);

    // Files of the git directory that say an operation is not finished, and its name.
    public static readonly IReadOnlyList<(string Path, string Operation)> OperationFiles = new[]
    {
        ("MERGE_HEAD", "merge"),
        ("rebase-merge", "rebase"),
        ("rebase-apply", "rebase"),
        ("CHERRY_PICK_HEAD", "cherry-pick"),
        ("REVERT_HEAD", "revert"),
    };

    private static readonly string[] TemplateEnvSuffixes = { "example", "sample", "template", "dist" };

    private static readonly string[] SensitiveExtensions = { ".pem", ".key", ".p12", ".pfx", ".ppk", ".jks", ".keystore", ".kdbx" };

    private static readonly string[] SensitiveNames = { "credentials.json", ".netrc", "_netrc", ".pgpass", ".htpasswd", ".git-credentials" };

    private static readonly string[] SshKeys = { "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519" };

    // `git status --porcelain=v1 -z --untracked-files=all`: "XY path\0", renames add "\0old".
    public static IReadOnlyList<StatusEntry> ParseStatus(string? z)
    {
        var entries = new List<StatusEntry>();
        var fields = (z ?? "").Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f.Length < 4 || f[2] != ' ')
            {
                continue;
            }

            var xy = f[..2];
            string? old = null;
            if ((xy[0] is 'R' or 'C') && i + 1 < fields.Length)
            {
                old = fields[++i];
            }

            entries.Add(new StatusEntry(xy, f[3..], old));
        }

        return entries;
    }

    // The paths whose content `git add -A` then commit would record.
    public static IReadOnlyList<string> CommittedPaths(IEnumerable<StatusEntry> status) =>
        status.Where(e => !e.Deleted && !e.Unmerged).Select(e => e.Path).Distinct().ToList();

    // .env and its variants (not the templates), private keys, keystores, credential files.
    public static bool IsSensitive(string path)
    {
        var name = path.Replace('\\', '/').TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..].ToLowerInvariant();
        if (name == ".env")
        {
            return true;
        }

        if (name.StartsWith(".env.", StringComparison.Ordinal))
        {
            var suffix = name[5..];
            return !TemplateEnvSuffixes.Any(t => suffix == t || suffix.StartsWith(t + ".", StringComparison.Ordinal));
        }

        if (SensitiveNames.Contains(name) || SensitiveExtensions.Any(e => name.EndsWith(e, StringComparison.Ordinal)))
        {
            return true;
        }

        if (name.StartsWith("client_secret", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
        {
            return true;
        }

        return SshKeys.Any(k => name.StartsWith(k, StringComparison.Ordinal)) && !name.EndsWith(".pub", StringComparison.Ordinal);
    }

    // A line git writes around a conflict: "<<<<<<< HEAD", ">>>>>>> branch". "=======" alone is
    // left out, it is also a Markdown heading underline.
    public static bool IsMarker(string line)
    {
        var l = line.TrimEnd('\r');
        return (l.StartsWith("<<<<<<<", StringComparison.Ordinal) || l.StartsWith(">>>>>>>", StringComparison.Ordinal))
            && (l.Length == 7 || l[7] == ' ');
    }

    // Untracked files: any marker in the text. Binary content (a NUL byte) is skipped.
    public static bool HasMarkers(string? text) =>
        !string.IsNullOrEmpty(text) && !text.Contains('\0') && text.Split('\n').Any(IsMarker);

    // `git diff HEAD -U0`: files with a marker in an added line. Markers already in HEAD (a test fixture,
    // a page about git) never block.
    public static IReadOnlyList<string> MarkersInDiff(string? diff)
    {
        var files = new List<string>();
        string? file = null;
        var inHunk = false;
        foreach (var raw in (diff ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                file = null;
                inHunk = false;
                continue;
            }

            if (!inHunk && line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                file = DiffPath(line[4..]);
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
                continue;
            }

            if (inHunk && file != null && line.Length > 1 && line[0] == '+' && IsMarker(line[1..]) && !files.Contains(file))
            {
                files.Add(file);
            }
        }

        return files;
    }

    // Unmerged files first, then an unfinished merge or rebase, then markers left in a file, then secrets.
    public static CommitCheck Check(IReadOnlyList<StatusEntry> status, string? operation, IReadOnlyList<string> markerFiles)
    {
        var unmerged = status.Where(e => e.Unmerged).Select(e => e.Path).Distinct().ToList();
        if (unmerged.Count > 0)
        {
            return new CommitCheck(CommitBlock.Unmerged, unmerged, operation);
        }

        if (operation != null)
        {
            return new CommitCheck(CommitBlock.Operation, Array.Empty<string>(), operation);
        }

        if (markerFiles.Count > 0)
        {
            return new CommitCheck(CommitBlock.Markers, markerFiles);
        }

        var secrets = CommittedPaths(status).Where(IsSensitive).ToList();
        return secrets.Count > 0 ? new CommitCheck(CommitBlock.Sensitive, secrets) : CommitCheck.Allowed;
    }

    // `git symbolic-ref --short refs/remotes/origin/HEAD` prints "origin/main".
    public static string? DefaultBranch(string? symbolicRef)
    {
        var r = (symbolicRef ?? "").Trim();
        var slash = r.IndexOf('/');
        return slash > 0 && slash < r.Length - 1 ? r[(slash + 1)..] : null;
    }

    // main, master, or the default branch of origin.
    public static bool NeedsConfirmation(string? branch, string? defaultBranch) =>
        branch != null && (branch is "main" or "master" || branch == defaultBranch);

    private static string DiffPath(string p)
    {
        if (p.Length > 1 && p[0] == '"' && p[^1] == '"')
        {
            p = p[1..^1];
        }

        return p.StartsWith("b/", StringComparison.Ordinal) ? p[2..] : p;
    }
}
