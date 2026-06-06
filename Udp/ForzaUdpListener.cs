using System.Net;
using System.Net.Sockets;
using LoupixDeck.Plugin.ForzaHorizon6.Telemetry;

namespace LoupixDeck.Plugin.ForzaHorizon6.Udp;

/// <summary>
/// Background UDP receiver for the Forza Data-Out stream. Parses each datagram
/// into a <see cref="ForzaPacket"/> and forwards valid samples to the host
/// callback, rate-limited to ~20 Hz so the touch screen stays readable.
/// </summary>
public sealed class ForzaUdpListener : IDisposable
{
    // Forza sends 60 Hz; 50 ms throttling => up to 20 redraws/sec, which the
    // touch screen can keep up with comfortably.
    private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromMilliseconds(50);

    private readonly int _port;
    private readonly Action<ForzaPacket> _onPacket;
    private readonly Action<string>? _logError;

    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private DateTime _lastForwardedUtc = DateTime.MinValue;

    public ForzaUdpListener(int port, Action<ForzaPacket> onPacket, Action<string>? logError = null)
    {
        _port = port;
        _onPacket = onPacket;
        _logError = logError;
    }

    public void Start()
    {
        if (_client != null) return;

        _client = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(() => ReceiveLoop(token), token);
    }

    private void ReceiveLoop(CancellationToken token)
    {
        var endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (!token.IsCancellationRequested)
        {
            byte[] buffer;
            try
            {
                buffer = _client!.Receive(ref endpoint);
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException ex) when (token.IsCancellationRequested)
            {
                _logError?.Invoke($"UDP receive cancelled: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                _logError?.Invoke($"UDP receive failed: {ex.Message}");
                return;
            }

            if (!ForzaPacket.TryParse(buffer, out var pkt))
                continue;

            var now = DateTime.UtcNow;
            if (now - _lastForwardedUtc < MinUpdateInterval)
                continue;
            _lastForwardedUtc = now;

            try { _onPacket(pkt); }
            catch (Exception ex) { _logError?.Invoke($"Packet callback threw: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* best effort */ }
        try { _client?.Dispose(); } catch { /* best effort */ }
        _cts?.Dispose();
        _client = null;
        _cts = null;
    }
}
