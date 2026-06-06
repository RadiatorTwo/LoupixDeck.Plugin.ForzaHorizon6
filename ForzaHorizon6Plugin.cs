using LoupixDeck.Plugin.ForzaHorizon6.Commands;
using LoupixDeck.Plugin.ForzaHorizon6.Mode;
using LoupixDeck.Plugin.ForzaHorizon6.Telemetry;
using LoupixDeck.Plugin.ForzaHorizon6.Udp;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.ForzaHorizon6;

/// <summary>
/// Forza Horizon Data-Out plugin. Listens on UDP, takes over the device via
/// exclusive mode when packets arrive, and shows live speed/gear/RPM until the
/// user pressed the exit button. The activate command re-arms the auto-enter.
/// </summary>
public sealed class ForzaHorizon6Plugin : LoupixPlugin
{
    private const int DefaultPort = 5607;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "forzahorizon6",
        Name = "Forza Horizon 6",
        Version = new Version(0, 1, 0),
        SdkVersion = SdkInfo.Version,
        Author = "RadiatorTwo",
        Description = "Displays Forza Horizon telemetry on the touch buttons via the Data Out UDP stream."
    };

    private IPluginHost? _host;
    private ForzaExclusiveProvider? _provider;
    private ForzaUdpListener? _listener;
    private ActivateCommand? _activateCommand;
    private volatile bool _userDisabled;

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _provider = new ForzaExclusiveProvider(host, () => _userDisabled = true);
        _activateCommand = new ActivateCommand(() => _userDisabled = false);

        var port = host.Settings.Get("port", DefaultPort);
        _listener = new ForzaUdpListener(port, OnPacket, msg => host.Logger.Error(msg));
        _listener.Start();

        host.Logger.Info($"Forza Horizon plugin listening on UDP {port}.");
    }

    private void OnPacket(ForzaPacket pkt)
    {
        if (_host == null || _provider == null) return;
        if (_userDisabled) return;

        if (!_host.IsInExclusiveMode)
        {
            // Request the takeover lazily on the first packet. If another
            // plugin already owns the device, drop the sample silently —
            // the next packet will retry.
            if (!_host.RequestExclusiveMode(_provider))
                return;
        }

        _provider.PushPacket(pkt);
    }

    public override IEnumerable<IPluginCommand> GetCommands()
    {
        if (_activateCommand != null) yield return _activateCommand;
    }

    public override void Shutdown()
    {
        try { _listener?.Dispose(); } catch { /* best effort */ }

        if (_host != null && _provider != null && _host.IsInExclusiveMode)
        {
            try { _host.ReleaseExclusiveMode(_provider); } catch { /* best effort */ }
        }

        _listener = null;
        _provider = null;
        _host = null;
    }
}
