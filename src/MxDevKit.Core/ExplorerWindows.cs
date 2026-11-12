using System;
using System.Collections.Generic;
using System.Linq;

namespace MxDevKit;

// Windows: which folder the front File Explorer window shows. Shell.Application lists one entry per
// tab, all with the handle of their window, so the window title (the name of the active tab, then
// " and 2 more tabs", " - File Explorer"...) tells them apart. Path is null for a tab that shows no
// folder of the disk (Home, This PC...).
public static class ExplorerWindows
{
    public static string? Pick(IReadOnlyList<(string? Path, string? Name)> tabs, string? title)
    {
        if (!string.IsNullOrEmpty(title))
        {
            var named = tabs
                .Where(t => !string.IsNullOrEmpty(t.Name) && (title == t.Name || title.StartsWith(t.Name + " ", StringComparison.Ordinal)))
                .ToList();
            if (named.Count > 0)
            {
                // "My Project" over "My" for the title "My Project - File Explorer".
                var longest = named.Max(t => t.Name!.Length);
                return One(named.Where(t => t.Name!.Length == longest));
            }
        }

        // The title says nothing: only a window whose tabs all show the same folder.
        return One(tabs);
    }

    // The folder when every tab shows the same one, null otherwise. Two tabs on folders of the same
    // name cannot be told apart.
    private static string? One(IEnumerable<(string? Path, string? Name)> tabs)
    {
        var paths = tabs.Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return paths.Count == 1 && !string.IsNullOrEmpty(paths[0]) ? paths[0] : null;
    }
}
