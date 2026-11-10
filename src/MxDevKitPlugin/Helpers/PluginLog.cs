namespace Loupedeck.MxDevKitPlugin;

// Plugin logs: ~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/MxDevKit.log (macOS)
// or %LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\MxDevKit.log (Windows).
internal static class PluginLog
{
    private static PluginLogFile? _log;

    public static void Init(PluginLogFile log) => _log = log;

    public static void Info(string text) => _log?.Info(text);

    public static void Warning(string text) => _log?.Warning(text);

    public static void Warning(Exception e, string text) => _log?.Warning(e, text);

    public static void Error(Exception e, string text) => _log?.Error(e, text);
}
