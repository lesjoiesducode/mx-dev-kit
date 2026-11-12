namespace Loupedeck.MxDevKitPlugin;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Windows: folder of the front File Explorer window, while one is the active window. The window is
// found by its class, its folder through Shell.Application, which lists the open Explorer tabs.
internal sealed class ExplorerFolder : IFolderWindow
{
    private const string ExplorerWindowClass = "CabinetWClass";

    private DateTime _pausedUntil = DateTime.MinValue;
    private bool _errorLogged;

    public bool IsFront()
    {
        if (!OperatingSystem.IsWindows() || DateTime.UtcNow < _pausedUntil)
        {
            return false;
        }

        var window = GetForegroundWindow();
        return window != IntPtr.Zero && ClassName(window) == ExplorerWindowClass;
    }

    public string? FrontWindowFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var window = GetForegroundWindow();
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (window == IntPtr.Zero || type == null)
        {
            return null;
        }

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(type);
            var tabs = new List<(string? Path, string? Name)>();
            dynamic windows = ((dynamic)shell!).Windows();
            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    object? item = windows.Item(i);
                    if (item == null)
                    {
                        continue;
                    }

                    dynamic tab = item;
                    if (Convert.ToInt64(tab.HWND) != window.ToInt64())
                    {
                        continue;
                    }

                    // Document is null while the tab loads; Folder.Self fails for a view that is not a folder.
                    object? document = tab.Document;
                    string? path = document == null ? null : (string?)((dynamic)document).Folder.Self.Path;
                    string? name = tab.LocationName;
                    tabs.Add((path != null && Directory.Exists(path) ? path : null, name));
                }
                catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
                {
                    // A window closing, or one that is not a folder view: skip it.
                }
            }

            return ExplorerWindows.Pick(tabs, WindowTitle(window));
        }
        catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or TargetInvocationException)
        {
            // Shell.Application unavailable: try again in a minute, log once.
            _pausedUntil = DateTime.UtcNow.AddMinutes(1);
            if (!_errorLogged)
            {
                _errorLogged = true;
                PluginLog.Warning(e, "Cannot read the File Explorer window. Set followFinder to false to stop trying.");
            }

            return null;
        }
        finally
        {
            if (shell != null && OperatingSystem.IsWindows())
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string ClassName(IntPtr window)
    {
        var buffer = new StringBuilder(64);
        return GetClassName(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    private static string? WindowTitle(IntPtr window)
    {
        var buffer = new StringBuilder(512);
        return GetWindowText(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
