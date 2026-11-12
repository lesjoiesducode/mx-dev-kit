namespace Loupedeck.MxDevKitPlugin;

using System.Runtime.InteropServices;

// macOS: bundle identifier of the active app, asked to NSWorkspace through the Objective-C runtime,
// so that following the Finder starts no process at every tick. Null when the runtime cannot answer.
internal static class MacFrontApp
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private static readonly Lazy<Selectors?> Runtime = new(Load);

    public static string? BundleId()
    {
        if (!OperatingSystem.IsMacOS() || Runtime.Value is not { } r)
        {
            return null;
        }

        // The objects below are autoreleased: a pool per call, or the plugin thread would keep them all.
        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = objc_msgSend(r.Workspace, r.SharedWorkspace);
            var app = workspace == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(workspace, r.FrontmostApplication);
            var id = app == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(app, r.BundleIdentifier);
            var utf8 = id == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(id, r.Utf8String);
            return utf8 == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(utf8);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static Selectors? Load()
    {
        try
        {
            // NSWorkspace lives in AppKit, which the plugin service may not have loaded.
            NativeLibrary.TryLoad(AppKit, out _);
            var workspace = objc_getClass("NSWorkspace");
            if (workspace == IntPtr.Zero)
            {
                return null;
            }

            return new Selectors(
                workspace,
                sel_registerName("sharedWorkspace"),
                sel_registerName("frontmostApplication"),
                sel_registerName("bundleIdentifier"),
                sel_registerName("UTF8String"));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            PluginLog.Warning(e, "Objective-C runtime unavailable: the Finder is checked with lsappinfo");
            return null;
        }
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    private sealed record Selectors(IntPtr Workspace, IntPtr SharedWorkspace, IntPtr FrontmostApplication, IntPtr BundleIdentifier, IntPtr Utf8String);
}
