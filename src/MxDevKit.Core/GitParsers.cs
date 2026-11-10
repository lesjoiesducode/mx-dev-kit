using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MxDevKit;

public static class GitParsers
{
    // owner/name for a github.com remote (ssh, scp-like or https), null otherwise.
    public static string? GitHubRepo(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var url = remoteUrl.Trim();
        var i = url.IndexOf("github.com", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        // Reject lookalikes such as notgithub.com or github.com.evil.example
        var before = i == 0 ? '/' : url[i - 1];
        if (before != '@' && before != '/' && before != '.')
        {
            return null;
        }

        var rest = url[(i + "github.com".Length)..];
        if (rest.Length == 0 || (rest[0] != ':' && rest[0] != '/'))
        {
            return null;
        }

        rest = rest.TrimStart(':', '/').TrimEnd('/');
        if (rest.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[..^4];
        }

        var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? $"{parts[0]}/{parts[1]}" : null;
    }

    // Number of entries in `git status --porcelain`.
    public static int CountChanges(string porcelain) =>
        porcelain.Split('\n').Count(line => line.TrimEnd('\r').Length > 0);

    // `git rev-list --left-right --count @{upstream}...HEAD` prints "<behind>\t<ahead>".
    public static (int Behind, int Ahead)? AheadBehind(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var parts = output.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out var behind) && int.TryParse(parts[1], out var ahead)
            ? (behind, ahead)
            : null;
    }

    // git prints forward slashes on Windows ("C:/src/app"); bring them back to the platform form.
    public static string NormalizePath(string path)
    {
        var p = path.Trim();
        if (OperatingSystem.IsWindows())
        {
            p = p.Replace('/', '\\');
        }

        return p.Length > 1 ? p.TrimEnd('/', '\\') : p;
    }

    // Closest folder holding `fileName`, from `dir` up to `stopAt` included. Null when there is none.
    public static string? FindUp(string dir, string stopAt, string fileName, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var stop = NormalizePath(stopAt);
        var d = NormalizePath(dir);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!d.StartsWith(stop, comparison))
        {
            return null;
        }

        while (!string.IsNullOrEmpty(d))
        {
            if (exists(Path.Combine(d, fileName)))
            {
                return d;
            }

            if (string.Equals(d, stop, comparison))
            {
                return null;
            }

            d = Path.GetDirectoryName(d);
        }

        return null;
    }

    // `git diff --cached --name-status`: "M\tpath", "R100\told\tnew"...
    public static IReadOnlyList<FileChange> NameStatus(string output)
    {
        var entries = new List<FileChange>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var fields = line.Split('\t');
            if (fields.Length < 2 || fields[0].Length == 0)
            {
                continue;
            }

            var kind = fields[0][0];
            entries.Add(fields.Length >= 3
                ? new FileChange(kind, fields[2], fields[1])
                : new FileChange(kind, fields[1], null));
        }

        return entries;
    }
}

// Kind is the first letter of git's status: A, M, D, R, C, T...
public sealed record FileChange(char Kind, string Path, string? OldPath);
