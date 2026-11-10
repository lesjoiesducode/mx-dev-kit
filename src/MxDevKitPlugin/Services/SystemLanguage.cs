namespace Loupedeck.MxDevKitPlugin;

using System.Globalization;
using System.Text.RegularExpressions;

internal static class SystemLanguage
{
    // Logi Plugin Service often runs without LANG on macOS, so the culture would be invariant:
    // the first preferred language of the user account is more reliable there.
    public static string Detect()
    {
        if (OperatingSystem.IsMacOS())
        {
            var output = ProcessRunner.Run("/usr/bin/defaults", null, TimeSpan.FromSeconds(3), new[] { "read", "-g", "AppleLanguages" })?.Stdout;
            // (\n    "fr-FR",\n    "en-FR"\n)
            var match = output == null ? null : Regex.Match(output, "\\(\\s*\"?([A-Za-z]{2,3})\\b");
            if (match is { Success: true })
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    }
}
