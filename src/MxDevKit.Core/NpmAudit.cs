using System;
using System.Text.Json;

namespace MxDevKit;

public static class NpmAudit
{
    private static readonly string[] Severities = { "low", "moderate", "high", "critical" };

    public static string NormalizeSeverity(string? severity)
    {
        var s = (severity ?? "").Trim().ToLowerInvariant();
        if (s == "medium")
        {
            s = "moderate";
        }

        return Array.IndexOf(Severities, s) >= 0 ? s : "high";
    }

    // Vulnerabilities at or above `minSeverity` in `npm audit --json`. Null when the output is not a report.
    public static int? Count(string? json, string minSeverity = "high")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("metadata", out var metadata)
                || !metadata.TryGetProperty("vulnerabilities", out var counts))
            {
                return null;
            }

            var total = 0;
            var from = Array.IndexOf(Severities, NormalizeSeverity(minSeverity));
            for (var i = from; i < Severities.Length; i++)
            {
                if (counts.TryGetProperty(Severities[i], out var n) && n.TryGetInt32(out var v))
                {
                    total += v;
                }
            }

            return total;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
