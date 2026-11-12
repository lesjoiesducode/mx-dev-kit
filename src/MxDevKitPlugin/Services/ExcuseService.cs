namespace Loupedeck.MxDevKitPlugin;

// Dev excuses: writes a random excuse in the app in front. The excuse goes through the clipboard
// (copy, Cmd+V or Ctrl+V), and what the clipboard held before comes back a moment later.
// When the keystroke cannot be sent, the excuse stays in the clipboard and a notification shows it.
internal sealed class ExcuseService
{
    // The ring closes and the app in front gets the focus back before the keystroke.
    private static readonly TimeSpan FocusDelay = TimeSpan.FromMilliseconds(200);

    // The app reads the clipboard when it handles the keystroke: give it time before restoring.
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(800);

    private readonly ExcusePicker _picker = new();
    private Plugin? _plugin;
    private int _busy;

    public void Register(Plugin plugin) => _plugin = plugin;

    public void Write()
    {
        // A second click while the first excuse is being written would save that excuse as "the user's clipboard".
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        var excuse = _picker.Next(DevExcuses.For(DevKit.Strings));
        ProcessRunner.Detach(() =>
        {
            try
            {
                this.Paste(excuse);
            }
            catch (Exception e)
            {
                PluginLog.Warning(e, "Dev excuse failed");
                Notifier.Show(DevKit.Strings.ExcuseName, excuse);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        });
    }

    private void Paste(string excuse)
    {
        var clipboard = Clipboard.For(_plugin);
        var saved = clipboard.Save();
        clipboard.Set(excuse);

        if (OperatingSystem.IsMacOS() && !MacAccessibility.Trusted())
        {
            PluginLog.Info("Dev excuse: Logi Plugin Service has no Accessibility permission, the excuse stays in the clipboard");
            MacAccessibility.AskOnce();
            Notifier.Show(DevKit.Strings.ExcuseCopied, excuse);
            return;
        }

        Thread.Sleep(FocusDelay);
        if (!this.SendPaste())
        {
            Notifier.Show(DevKit.Strings.ExcuseCopied, excuse);
            return;
        }

        Thread.Sleep(RestoreDelay);
        if (!clipboard.StillOurs(excuse))
        {
            PluginLog.Info("Dev excuse: the clipboard changed meanwhile, not restored");
            return;
        }

        clipboard.Restore(saved);
    }

    private bool SendPaste()
    {
        var modifier = OperatingSystem.IsMacOS() ? ModifierKey.Command : ModifierKey.Control;
        try
        {
            var keyboard = _plugin?.NativeApi?.GetNativeKeyboard();
            if (keyboard != null && keyboard.SendKeyboardShortcut(VirtualKeyCode.KeyV, modifier, keyboard.GetActiveKeyboardLayout()))
            {
                PluginLog.Info("Dev excuse: pasted with Logi keyboard API");
                return true;
            }

            PluginLog.Info("Dev excuse: Logi keyboard API did not send the shortcut");
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Dev excuse: Logi keyboard API failed");
        }

        if (OperatingSystem.IsMacOS())
        {
            MacAccessibility.PasteWithQuartz();
            PluginLog.Info("Dev excuse: pasted with Quartz");
            return true;
        }

        return false;
    }

    // The system clipboard: everything on macOS (NSPasteboard), text through Logi's API elsewhere.
    private abstract class Clipboard
    {
        public static Clipboard For(Plugin? plugin) => MacPasteboard.Available ? new Mac() : new Logi(plugin);

        public abstract object? Save();

        public abstract void Set(string text);

        public abstract bool StillOurs(string text);

        public abstract void Restore(object? saved);

        private sealed class Mac : Clipboard
        {
            private long _written;

            public override object? Save() => MacPasteboard.Save();

            public override void Set(string text) => _written = MacPasteboard.SetText(text);

            public override bool StillOurs(string text) => MacPasteboard.ChangeCount() == _written;

            public override void Restore(object? saved) => MacPasteboard.Restore((MacPasteboard.Snapshot)saved!);
        }

        private sealed class Logi : Clipboard
        {
            private readonly INativeMethods _native;

            public Logi(Plugin? plugin)
            {
                _native = plugin?.NativeApi?.GetNativeMethods() ?? throw new InvalidOperationException("No clipboard API");
            }

            public override object? Save() => _native.ClipboardGetText();

            public override void Set(string text) => _native.ClipboardSetText(text);

            public override bool StillOurs(string text) => _native.ClipboardGetText() == text;

            // Text only: an image or files copied before are not brought back.
            public override void Restore(object? saved) => _native.ClipboardSetText(saved as string ?? "");
        }
    }
}
