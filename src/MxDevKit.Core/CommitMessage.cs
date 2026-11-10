using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MxDevKit;

public static class CommitMessage
{
    public const int MaxLength = 100;

    // Used when no message command is configured or when it fails: "Update app.ts and 2 more files".
    public static string Fallback(IReadOnlyList<FileChange> changes)
    {
        if (changes.Count == 0)
        {
            return "Update files";
        }

        if (changes.Count == 1 && changes[0] is { Kind: 'R', OldPath: { } old })
        {
            return $"Rename {Name(old)} to {Name(changes[0].Path)}";
        }

        var verb = changes.All(c => c.Kind == 'A') ? "Add"
            : changes.All(c => c.Kind == 'D') ? "Remove"
            : changes.All(c => c.Kind == 'R') ? "Rename"
            : "Update";

        var names = changes.Select(c => Name(c.Path)).Distinct().ToList();
        return names.Count switch
        {
            1 => $"{verb} {names[0]}",
            2 => $"{verb} {names[0]} and {names[1]}",
            3 => $"{verb} {names[0]}, {names[1]} and {names[2]}",
            _ => $"{verb} {names[0]}, {names[1]} and {names.Count - 2} more files",
        };
    }

    // Keeps the first meaningful line of what a model answered, without quotes, fences or labels.
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var l in raw.Split('\n'))
        {
            var line = l.Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var label in new[] { "commit message:", "message:", "commit:" })
            {
                if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    line = line[label.Length..].Trim();
                }
            }

            line = line.Trim('"', '\'', '`', '*').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Length > MaxLength)
            {
                var cut = line.LastIndexOf(' ', MaxLength);
                line = line[..(cut > 20 ? cut : MaxLength)].TrimEnd(',', ';', ':', ' ');
            }

            return line;
        }

        return null;
    }

    // Arguments for the message command and what to send on its standard input.
    public static (string[] Args, string Stdin) Invocation(string[] commandArgs, string prompt, string diff)
    {
        var hasPlaceholder = commandArgs.Any(a => a.Contains("{prompt}", StringComparison.Ordinal));
        var args = commandArgs.Select(a => a.Replace("{prompt}", prompt, StringComparison.Ordinal)).ToArray();
        return (args, hasPlaceholder ? diff : prompt + "\n\n" + diff);
    }

    private static string Name(string path) => Path.GetFileName(path.TrimEnd('/')) is { Length: > 0 } n ? n : path;
}
