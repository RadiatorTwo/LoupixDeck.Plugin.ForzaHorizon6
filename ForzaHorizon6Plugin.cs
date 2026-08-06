using LoupixDeck.Plugin.ForzaHorizon6.Commands;
using LoupixDeck.Plugin.ForzaHorizon6.Mode;
using LoupixDeck.Plugin.ForzaHorizon6.Telemetry;
using LoupixDeck.Plugin.ForzaHorizon6.Udp;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.ForzaHorizon6;

/// <summary>
/// Forza Horizon Data-Out plugin. Listens on UDP and shows live speed/gear/RPM in a full-device HUD.
/// The HUD is started explicitly by the <c>ForzaHorizon6.Activate</c> command (never automatically):
/// while active it takes the device over via exclusive mode and renders incoming telemetry; the user
/// leaves it with the EXIT tile, and it does not reappear on its own. The UDP listener runs for the
/// whole plugin lifetime, but its packets are only rendered while the HUD is active.
/// </summary>
public sealed class ForzaHorizon6Plugin : LoupixPlugin
{
    private const int DefaultPort = 5607;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "forzahorizon6",
        Name = "Forza Horizon 6",
        Version = new Version(0, 1, 0),
        SdkVersion = new Version(1, 16, 0),
        Author = "RadiatorTwo",
        Description = "Displays Forza Horizon telemetry on the touch buttons via the Data Out UDP stream."
    };

    private IPluginHost? _host;
    private ForzaExclusiveProvider? _provider;
    private ForzaUdpListener? _listener;
    private ActivateCommand? _activateCommand;

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _provider = new ForzaExclusiveProvider(host);
        _activateCommand = new ActivateCommand(StartHud);

        var port = host.Settings.Get("port", DefaultPort);
        _listener = new ForzaUdpListener(port, OnPacket, msg => host.Logger.Error(msg));
        _listener.Start();

        host.Logger.Info($"Forza Horizon plugin listening on UDP {port}.");
    }

    /// <summary>Starts the HUD on demand (the ForzaHorizon6.Activate command). Enters exclusive mode
    /// so telemetry is rendered; no-op if it is already showing, warns if another exclusive takeover
    /// owns the display.</summary>
    private void StartHud()
    {
        if (_host == null || _provider == null) return;
        if (_provider.IsActive) return;

        if (!_host.RequestExclusiveMode(_provider))
            _host.Logger.Warn("Forza HUD: the display is already in use by another exclusive mode.");
    }

    private void OnPacket(ForzaPacket pkt)
    {
        // Only render telemetry while the HUD is showing. The command (not a packet) starts it, so a
        // packet arriving never triggers a takeover.
        if (_provider is { IsActive: true })
            _provider.PushPacket(pkt);
    }

    public override IEnumerable<IPluginCommand> GetCommands()
    {
        if (_activateCommand != null) yield return _activateCommand;
    }

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = "ForzaHorizon6",
            Description = "Live race telemetry",
            Icon = "\U000F0297",
            Section = CommandGroupSection.Plugins
        }
    ];

    public override void Shutdown()
    {
        try { _listener?.Dispose(); } catch { /* best effort */ }

        if (_host != null && _provider is { IsActive: true })
        {
            try { _host.ReleaseExclusiveMode(_provider); } catch { /* best effort */ }
        }

        _listener = null;
        _provider = null;
        _host = null;
    }
}
