namespace Loupedeck.MxDevKitPlugin;

using System.Runtime.InteropServices;

// macOS: a simulated keystroke reaches the app in front only when Logi Plugin Service has the
// Accessibility permission. Without it macOS drops the event without a word, so the plugin asks first.
internal static class MacAccessibility
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private static bool _asked;

    public static bool Trusted()
    {
        try
        {
            return AXIsProcessTrusted();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            PluginLog.Warning(e, "AXIsProcessTrusted unavailable");
            return false;
        }
    }

    // Once per session: macOS shows its dialog that leads to Privacy & Security > Accessibility.
    public static void AskOnce()
    {
        if (_asked)
        {
            return;
        }

        _asked = true;
        try
        {
            var services = NativeLibrary.Load(ApplicationServices);
            var cf = NativeLibrary.Load(CoreFoundation);
            var key = Marshal.ReadIntPtr(NativeLibrary.GetExport(services, "kAXTrustedCheckOptionPrompt"));
            var yes = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFBooleanTrue"));
            var keys = new[] { key };
            var values = new[] { yes };
            var options = CFDictionaryCreate(IntPtr.Zero, keys, values, 1, NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks"));
            AXIsProcessTrustedWithOptions(options);
            CFRelease(options);
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Could not ask for the Accessibility permission");
        }
    }

    // Cmd+V through Quartz, when Logi's keyboard API is not there or fails.
    public static void PasteWithQuartz()
    {
        const ushort keyV = 9; // kVK_ANSI_V
        const ulong command = 1UL << 20; // kCGEventFlagMaskCommand
        foreach (var down in new[] { true, false })
        {
            var e = CGEventCreateKeyboardEvent(IntPtr.Zero, keyV, down);
            CGEventSetFlags(e, command);
            CGEventPost(0, e); // kCGHIDEventTap
            CFRelease(e);
        }
    }

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, long count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CoreGraphics)]
    private static extern void CGEventSetFlags(IntPtr e, ulong flags);

    [DllImport(CoreGraphics)]
    private static extern void CGEventPost(uint tap, IntPtr e);
}
