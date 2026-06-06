using LoupixDeck.Plugin.ForzaHorizon6.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.ForzaHorizon6.Mode;

/// <summary>
/// Drives the Forza HUD while exclusive mode is active. Top row (slots 0–4):
/// EXIT hint (also simple button 0), speed, gear, RPM, grip warning. Slots 5/6
/// and 10/11 form a 2x2 tire-corner block (FL/FR over RL/RR). Other inputs are
/// no-ops. The layout targets the 5x3 Loupedeck Live S grid.
/// </summary>
public sealed class ForzaExclusiveProvider : IExclusiveModeProvider
{
    // SimpleButton index 0 acts as the manual exit. Matches the device's
    // first physical side button, which the user agreed on as the convention.
    private const int ExitButtonIndex = 0;
    private const int ExitSlotIndex = 0;
    private const int SpeedSlotIndex = 1;
    private const int GearSlotIndex = 2;
    private const int RpmSlotIndex = 3;
    private const int GripSlotIndex = 4;
    private const int TireFlSlot = 5;
    private const int TireFrSlot = 6;
    private const int DriftSlot = 7;
    private const int TireRlSlot = 10;
    private const int TireRrSlot = 11;

    // Tire-temperature color ramp thresholds (°C).
    private const float TempColdC = 70f;
    private const float TempOptimalC = 90f;
    private const float TempHotC = 110f;

    // Drift-angle color ramp thresholds (degrees). At/below "calm" the car is
    // tracking straight (green); at/above "hot" it is fully sideways (red).
    private const float DriftCalmDeg = 10f;
    private const float DriftHotDeg = 40f;

    // Combined-slip thresholds for the grip warning tiers. Forza reports ~1.0
    // at the limit of adhesion; beyond that the tire is sliding.
    private const float SlipWarn = 0.9f;
    private const float SlipRisk = 1.4f;
    private const float SlipLow = 2.0f;
    // An axle is called out by name once its slip clearly dominates the other.
    private const float SlipAxleBias = 0.3f;

    private static readonly PluginColor ExitBack = PluginColor.FromRgb(0x80, 0x10, 0x10);
    private static readonly PluginColor HudBack = PluginColor.FromRgb(0x10, 0x10, 0x18);

    private static readonly PluginColor GripOk = PluginColor.FromRgb(0x00, 0xFF, 0x99);
    private static readonly PluginColor GripWarn = PluginColor.FromRgb(0xFF, 0xD4, 0x00);
    private static readonly PluginColor GripRisk = PluginColor.FromRgb(0xFF, 0x7A, 0x00);
    private static readonly PluginColor GripLow = PluginColor.FromRgb(0xFF, 0x1E, 0x1E);

    private readonly IPluginHost _host;
    private readonly Action _onUserDisable;
    private ForzaPacket _latest;

    public ForzaExclusiveProvider(IPluginHost host, Action onUserDisable)
    {
        _host = host;
        _onUserDisable = onUserDisable;
    }

    public string Title => "Forza Horizon";

    // The HUD changes only a few tiles per packet (speed/gear/rpm/grip flicker,
    // tire temps drift slowly) — most stay identical frame to frame. DirtyTiles
    // makes the host re-send only the slots whose content actually changed, so a
    // ~20 Hz update costs a handful of 90x90 framebuffer writes instead of a full
    // 480x270 blit. The grip blink still redraws because its BackColor toggles.
    public ExclusiveRenderMode RenderMode => ExclusiveRenderMode.DirtyTiles;

    public event EventHandler? EntriesChanged;

    public void OnEnter() { /* nothing to wire — packets drive RaiseChanged */ }

    public void OnExit() { /* nothing to release */ }

    /// <summary>Called by the listener with each accepted packet.</summary>
    public void PushPacket(ForzaPacket pkt)
    {
        _latest = pkt;
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<FolderEntry> BuildTouchEntries()
    {
        var p = _latest;
        var gearText = p.Gear switch
        {
            0 => "R",
            _ => p.Gear.ToString()
        };

        var (gripText, gripColor) = BuildGrip(p);

        return new[]
        {
            new FolderEntry
            {
                SlotIndex = ExitSlotIndex,
                Text = "EXIT",
                BackColor = ExitBack,
                TextSize = 22,
                Bold = true
            },
            new FolderEntry
            {
                SlotIndex = SpeedSlotIndex,
                Text = $"{p.SpeedKmh:F0}\nkm/h",
                BackColor = HudBack,
                TextSize = 22,
                Bold = true
            },
            new FolderEntry
            {
                SlotIndex = GearSlotIndex,
                Text = gearText,
                BackColor = HudBack,
                TextSize = 48,
                Bold = true
            },
            new FolderEntry
            {
                SlotIndex = RpmSlotIndex,
                Text = $"{p.Rpm:F0}\nrpm",
                BackColor = HudBack,
                TextSize = 22,
                Bold = true
            },
            new FolderEntry
            {
                SlotIndex = GripSlotIndex,
                Text = gripText,
                BackColor = gripColor,
                TextColor = PluginColor.Black,
                TextSize = 18,
                Bold = true
            },
            DriftEntry(p.DriftAngleDeg),
            TireEntry(TireFlSlot, p.TempFlC),
            TireEntry(TireFrSlot, p.TempFrC),
            TireEntry(TireRlSlot, p.TempRlC),
            TireEntry(TireRrSlot, p.TempRrC)
        };
    }

    private static FolderEntry DriftEntry(float deg) => new()
    {
        SlotIndex = DriftSlot,
        Text = $"DRIFT\n{deg:F0}°",
        BackColor = DriftColor(deg),
        TextColor = PluginColor.Black,
        TextSize = 18,
        Bold = true
    };

    private static PluginColor DriftColor(float deg)
    {
        // Reuse the grip palette so the HUD reads consistently: green = calm,
        // yellow at the midpoint, red once fully sideways.
        if (deg <= DriftCalmDeg) return GripOk;
        if (deg >= DriftHotDeg) return GripLow;

        var mid = (DriftCalmDeg + DriftHotDeg) / 2f;
        return deg < mid
            ? Lerp(GripOk, GripWarn, (deg - DriftCalmDeg) / (mid - DriftCalmDeg))
            : Lerp(GripWarn, GripLow, (deg - mid) / (DriftHotDeg - mid));
    }

    private static FolderEntry TireEntry(int slot, float tempC) => new()
    {
        SlotIndex = slot,
        Text = $"{tempC:F0}\n°C",
        BackColor = TempColor(tempC),
        TextSize = 22,
        Bold = true
    };

    private static PluginColor TempColor(float c)
    {
        // Cold -> blue, optimal -> green, hot -> red. Below "cold" stays blue,
        // above "hot" stays red; in between we lerp through the bands.
        if (c <= TempColdC) return PluginColor.FromRgb(0x20, 0x60, 0xC0);
        if (c < TempOptimalC)
            return Lerp(PluginColor.FromRgb(0x20, 0x60, 0xC0), PluginColor.FromRgb(0x20, 0xA0, 0x40),
                (c - TempColdC) / (TempOptimalC - TempColdC));
        if (c < TempHotC)
            return Lerp(PluginColor.FromRgb(0x20, 0xA0, 0x40), PluginColor.FromRgb(0xD0, 0x20, 0x20),
                (c - TempOptimalC) / (TempHotC - TempOptimalC));
        return PluginColor.FromRgb(0xD0, 0x20, 0x20);
    }

    private static PluginColor Lerp(PluginColor a, PluginColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return PluginColor.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static (string Text, PluginColor Color) BuildGrip(ForzaPacket p)
    {
        var front = p.FrontSlip;
        var rear = p.RearSlip;
        var eff = MathF.Max(front, rear);

        if (eff < SlipWarn) return ("GRIP\nOK", GripOk);

        // Name the axle that is letting go when one clearly dominates.
        var label = (front - rear) switch
        {
            > SlipAxleBias => "FRONT\nSLIP",
            < -SlipAxleBias => "REAR\nSLIP",
            _ when eff >= SlipLow => "LOW\nGRIP",
            _ when eff >= SlipRisk => "GRIP\nRISK",
            _ => "GRIP\nWARN"
        };

        var color = eff switch
        {
            >= SlipLow => GripLow,
            >= SlipRisk => GripRisk,
            _ => GripWarn
        };

        // Blink the critical tier so it grabs attention. The host redraws on
        // every accepted packet (~20 Hz), fast enough to animate a 160 ms blink.
        if (eff >= SlipLow && (Environment.TickCount / 160) % 2 == 0)
            color = HudBack;

        return (label, color);
    }

    public void OnSimpleButtonPressed(int index)
    {
        if (index == ExitButtonIndex) RequestExit();
    }

    public void OnTouchPressed(int slotIndex)
    {
        // Touching the EXIT slot mirrors pressing the exit hardware button —
        // helpful when the user remembers the visual hint before the button.
        if (slotIndex == ExitSlotIndex) RequestExit();
    }

    public void OnRotaryPressed(int index) { /* v1: no-op */ }

    public void OnRotated(int index, int delta) { /* v1: no-op */ }

    private void RequestExit()
    {
        _onUserDisable();
        _host.ReleaseExclusiveMode(this);
    }
}
