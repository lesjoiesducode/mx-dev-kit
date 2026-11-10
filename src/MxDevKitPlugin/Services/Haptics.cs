namespace Loupedeck.MxDevKitPlugin;

using System.Net;
using System.Net.Sockets;

// MX Master 4 haptics. "sdk" mode raises the plugin's own events (package/events), "http" mode
// goes through the Haptic Web Plugin like the shell scripts. Both stay silent while haptics.off exists.
internal sealed class Haptics
{
    private readonly Lazy<HttpClient> _http = new(CreateClient);
    private Plugin? _plugin;

    public void Register(Plugin plugin, Strings strings)
    {
        _plugin = plugin;
        foreach (var name in HapticEvents.DefaultWaveforms.Keys)
        {
            try
            {
                plugin.PluginEvents.AddEvent(name, HapticEvents.DisplayName(name, strings), HapticEvents.DisplayName(name, strings));
            }
            catch (Exception e)
            {
                PluginLog.Warning(e, $"Could not register haptic event {name}");
            }
        }
    }

    public void Raise(string eventName)
    {
        var config = DevKit.Config.Haptics;
        if (config.Mode == "off" || File.Exists(DevKitPaths.MuteFile))
        {
            return;
        }

        if (config.Mode == "http")
        {
            var waveform = HapticEvents.WaveformFor(eventName, config.Waveforms);
            var url = config.Url.TrimEnd('/') + "/" + waveform;
            _ = this.PostAsync(url);
            PluginLog.Info($"Haptic {eventName} -> {waveform} (http)");
            return;
        }

        try
        {
            _plugin?.PluginEvents.RaiseEvent(eventName);
            PluginLog.Info($"Haptic {eventName} (sdk)");
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, $"Could not raise haptic event {eventName}");
        }
    }

    private async Task PostAsync(string url)
    {
        try
        {
            using var response = await _http.Value.PostAsync(url, new ByteArrayContent(Array.Empty<byte>()));
            if (!response.IsSuccessStatusCode)
            {
                PluginLog.Warning($"Haptic Web Plugin answered {(int)response.StatusCode} for {url}");
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            PluginLog.Warning($"Haptic Web Plugin unreachable ({url}): {e.Message}");
        }
    }

    // local.jmw.nz is a public name for 127.0.0.1 (it lets the Haptic Web Plugin serve a valid certificate).
    // Connecting to the loopback directly keeps vibrations working without DNS.
    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ConnectCallback = async (context, token) =>
            {
                var endpoint = context.DnsEndPoint;
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    if (endpoint.Host.Equals("local.jmw.nz", StringComparison.OrdinalIgnoreCase))
                    {
                        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, endpoint.Port), token);
                    }
                    else
                    {
                        await socket.ConnectAsync(endpoint, token);
                    }

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }
}
