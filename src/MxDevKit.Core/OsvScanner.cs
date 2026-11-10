using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MxDevKit;

// osv-scanner (https://google.github.io/osv-scanner/) reads every lockfile of a repository, for npm,
// Python, PHP, Go, Rust, Ruby, .NET, Java, Dart, Elixir... The vulnerability bubble uses it when it
// is installed, and npm audit otherwise.
public static class OsvScanner
{
    public const string Name = "osv-scanner";

    // osv-scanner exits with 128 when the folder has no lockfile or manifest it knows.
    public const int NoPackagesExitCode = 128;

    private static readonly string[] Severities = { "low", "moderate", "high", "critical" };

    private static readonly HashSet<string> LockfileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "npm-shrinkwrap.json", "pnpm-lock.yaml", "yarn.lock", "bun.lock", "deno.lock",
        "poetry.lock", "uv.lock", "pdm.lock", "Pipfile.lock", "pylock.toml",
        "composer.lock", "Cargo.lock", "go.mod", "Gemfile.lock", "gems.locked",
        "packages.lock.json", "pom.xml", "gradle.lockfile", "buildscript-gradle.lockfile", "verification-metadata.xml",
        "mix.lock", "pubspec.lock", "renv.lock", "conan.lock", "Package.resolved", "Podfile.lock", "MODULE.bazel.lock",
    };

    // A file osv-scanner reads, by its name: requirements*.txt, *.deps.json and the lockfiles above.
    public static bool IsLockfile(string path)
    {
        var name = Path.GetFileName(path.Replace('\\', '/'));
        return LockfileNames.Contains(name)
            || (name.StartsWith("requirements", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase);
    }

    // 0: nothing found, 1: vulnerabilities found, 128: nothing to scan. Anything else is an error
    // (offline, a flag this version does not know...).
    public static bool Understood(int exitCode) => exitCode is 0 or 1 or NoPackagesExitCode;

    // Arguments of the scan. osv-scanner 2 has a `scan source` command; version 1 takes the flags directly.
    public static string[] Arguments(string dir, bool version1) => version1
        ? new[] { "--format", "json", "--recursive", dir }
        : new[] { "scan", "source", "--format", "json", "--recursive", dir };

    // Vulnerabilities at or above `minSeverity` in `osv-scanner --format json`, one per advisory group
    // (aliases of the same flaw count once) and per package version. Null when the output is not a report.
    // An advisory without any score counts as `unknownSeverity`.
    public static int? Count(string? json, string minSeverity = "high", string unknownSeverity = "high")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("results", out var results))
            {
                return null;
            }

            if (results.ValueKind == JsonValueKind.Null)
            {
                return 0;
            }

            if (results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var from = Array.IndexOf(Severities, NpmAudit.NormalizeSeverity(minSeverity));
            var unknown = Array.IndexOf(Severities, NpmAudit.NormalizeSeverity(unknownSeverity));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            foreach (var result in results.EnumerateArray())
            {
                foreach (var package in Items(result, "packages"))
                {
                    var key = PackageKey(package);
                    var vulnerabilities = Items(package, "vulnerabilities").ToList();
                    foreach (var group in Items(package, "groups"))
                    {
                        var ids = Items(group, "ids").Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).ToList();
                        if (ids.Count == 0 || !seen.Add(key + "|" + ids.Min(StringComparer.Ordinal)))
                        {
                            continue;
                        }

                        var level = Level(group, ids, vulnerabilities) ?? unknown;
                        if (level >= from)
                        {
                            total++;
                        }
                    }
                }
            }

            return total;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // CVSS score of the group (low < 4, moderate < 7, high < 9, critical), else the severity word of
    // the advisory database (GitHub's "MODERATE", "HIGH"...), else unknown (null).
    private static int? Level(JsonElement group, IReadOnlyCollection<string> ids, IEnumerable<JsonElement> vulnerabilities)
    {
        if (group.TryGetProperty("max_severity", out var max) && max.ValueKind == JsonValueKind.String
            && double.TryParse(max.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
        {
            return score >= 9 ? 3 : score >= 7 ? 2 : score >= 4 ? 1 : 0;
        }

        var best = -1;
        foreach (var v in vulnerabilities)
        {
            if (v.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && ids.Contains(id.GetString()!)
                && v.TryGetProperty("database_specific", out var db) && db.ValueKind == JsonValueKind.Object
                && db.TryGetProperty("severity", out var word) && word.ValueKind == JsonValueKind.String)
            {
                var w = word.GetString()!.Trim().ToLowerInvariant();
                best = Math.Max(best, Array.IndexOf(Severities, w == "medium" ? "moderate" : w));
            }
        }

        return best >= 0 ? best : null;
    }

    private static string PackageKey(JsonElement package)
    {
        if (!package.TryGetProperty("package", out var p) || p.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        return string.Join("|", new[] { "ecosystem", "name", "version" }
            .Select(n => p.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : ""));
    }

    private static IEnumerable<JsonElement> Items(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
}
