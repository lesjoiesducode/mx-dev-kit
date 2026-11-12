namespace Loupedeck.MxDevKitPlugin;

using System.Runtime.InteropServices;

// macOS: the general pasteboard through the Objective-C runtime. Save keeps every item in every
// type (text, image, files...), so that Restore puts back exactly what the user had copied.
internal static class MacPasteboard
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string TextType = "public.utf8-plain-text";

    private static readonly Lazy<bool> Loaded = new(() => NativeLibrary.TryLoad(AppKit, out _) && objc_getClass("NSPasteboard") != IntPtr.Zero);

    public static bool Available => OperatingSystem.IsMacOS() && Loaded.Value;

    // What the pasteboard holds now: its items, each as (type, bytes).
    public static Snapshot Save() => Pooled(() =>
    {
        var board = General();
        var items = new List<List<(string Type, byte[] Data)>>();
        var array = Send(board, Sel("pasteboardItems"));
        var count = array == IntPtr.Zero ? 0 : (int)SendUInt(array, Sel("count"));
        for (var i = 0; i < count; i++)
        {
            var item = SendIndex(array, Sel("objectAtIndex:"), (nuint)i);
            var types = Send(item, Sel("types"));
            var typeCount = types == IntPtr.Zero ? 0 : (int)SendUInt(types, Sel("count"));
            var entries = new List<(string, byte[])>();
            for (var j = 0; j < typeCount; j++)
            {
                var type = SendIndex(types, Sel("objectAtIndex:"), (nuint)j);
                var data = Send(item, Sel("dataForType:"), type);
                if (data == IntPtr.Zero)
                {
                    continue;
                }

                var length = (int)SendUInt(data, Sel("length"));
                var bytes = new byte[length];
                if (length > 0)
                {
                    Marshal.Copy(Send(data, Sel("bytes")), bytes, 0, length);
                }

                entries.Add((ToString(type), bytes));
            }

            if (entries.Count > 0)
            {
                items.Add(entries);
            }
        }

        return new Snapshot(items);
    });

    // Replaces the pasteboard with this text. Returns the change count that marks our write.
    public static long SetText(string text) => Pooled(() =>
    {
        var board = General();
        SendLong(board, Sel("clearContents"));
        Send(board, Sel("setString:forType:"), NSString(text), NSString(TextType));
        return SendLong(board, Sel("changeCount"));
    });

    public static long ChangeCount() => Pooled(() => SendLong(General(), Sel("changeCount")));

    public static void Restore(Snapshot snapshot) => Pooled(() =>
    {
        var board = General();
        SendLong(board, Sel("clearContents"));
        if (snapshot.Items.Count == 0)
        {
            return 0;
        }

        var objects = new IntPtr[snapshot.Items.Count];
        var itemClass = objc_getClass("NSPasteboardItem");
        var dataClass = objc_getClass("NSData");
        for (var i = 0; i < objects.Length; i++)
        {
            var item = Send(Send(Send(itemClass, Sel("alloc")), Sel("init")), Sel("autorelease"));
            foreach (var (type, bytes) in snapshot.Items[i])
            {
                var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    var data = SendBytes(dataClass, Sel("dataWithBytes:length:"), handle.AddrOfPinnedObject(), (nuint)bytes.Length);
                    Send(item, Sel("setData:forType:"), data, NSString(type));
                }
                finally
                {
                    handle.Free();
                }
            }

            objects[i] = item;
        }

        var pinned = GCHandle.Alloc(objects, GCHandleType.Pinned);
        try
        {
            var array = SendBytes(objc_getClass("NSArray"), Sel("arrayWithObjects:count:"), pinned.AddrOfPinnedObject(), (nuint)objects.Length);
            Send(board, Sel("writeObjects:"), array);
        }
        finally
        {
            pinned.Free();
        }

        return 0;
    });

    private static T Pooled<T>(Func<T> body)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            return body();
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static IntPtr General() => Available
        ? Send(objc_getClass("NSPasteboard"), Sel("generalPasteboard"))
        : throw new PlatformNotSupportedException("NSPasteboard unavailable");

    private static IntPtr Sel(string name) => sel_registerName(name);

    private static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static string ToString(IntPtr nsString)
    {
        var utf8 = Send(nsString, Sel("UTF8String"));
        return utf8 == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(utf8) ?? "";
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendUInt(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern long SendLong(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    public sealed record Snapshot(IReadOnlyList<List<(string Type, byte[] Data)>> Items);
}
