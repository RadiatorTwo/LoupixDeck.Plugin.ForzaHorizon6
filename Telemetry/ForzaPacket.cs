using System.Buffers.Binary;

namespace LoupixDeck.Plugin.ForzaHorizon6.Telemetry;

/// <summary>
/// Single Forza Data-Out telemetry sample, reduced to what the v1 HUD shows.
/// Field offsets follow the publicly documented FH5 / FM "Car Dash" V2 layout —
/// FH6 is expected to be backwards-compatible. Sled-only packets (shorter than
/// 311 bytes or with <c>IsRaceOn == 0</c>) are rejected.
/// </summary>
public readonly record struct ForzaPacket(
    float Rpm,
    float SpeedMs,
    byte Gear,
    float TireTempFl,
    float TireTempFr,
    float TireTempRl,
    float TireTempRr,
    float SlipFl,
    float SlipFr,
    float SlipRl,
    float SlipRr,
    float VelocityX,
    float VelocityZ)
{
    // Horizon Dash packet length is 324 bytes; we require enough for Gear@319.
    private const int MinLength = 320;

    // Offsets into the Car Dash packet. Forza Horizon inserts a 12-byte
    // HorizonPlaceholder between the Sled and Dash sections, which shifts every
    // dash-section field 12 bytes from the FM7 layout (e.g. Speed 244 -> 256,
    // Gear 307 -> 319). Speed working but Gear stuck at 0 was the giveaway.
    private const int OffsetIsRaceOn = 0;
    private const int OffsetRpm = 16;
    private const int OffsetSpeedMs = 256;
    private const int OffsetGear = 319;

    // Tire temps live in the Dash section, so they carry the same +12 shift
    // (FM7 256/260/264/268 -> 268/272/276/280). Forza reports them in °F.
    private const int OffsetTireTempFl = 268;
    private const int OffsetTireTempFr = 272;
    private const int OffsetTireTempRl = 276;
    private const int OffsetTireTempRr = 280;

    // Combined slip lives in the Sled section (before the placeholder), so it
    // is NOT shifted — FM7 offsets apply unchanged.
    private const int OffsetSlipFl = 180;
    private const int OffsetSlipFr = 184;
    private const int OffsetSlipRl = 188;
    private const int OffsetSlipRr = 192;

    // Velocity (m/s) lives in the Sled section, so it keeps its FM7 offsets.
    // Forza reports it in the car's BODY frame — Vx is the lateral component,
    // Vz the forward component — which gives the sideslip / drift angle directly.
    // Confirmed against live FH6 data: Vx stays ~0 while accelerating straight
    // even as yaw sweeps through the corner. VelocityY (vertical) is not used.
    private const int OffsetVelocityX = 32;
    private const int OffsetVelocityZ = 40;

    // Below this ground speed the velocity vector is mostly noise, so atan2 would
    // jitter the drift angle wildly at a standstill — we report 0 instead.
    private const float DriftMinSpeedMs = 2f;

    public float SpeedKmh => SpeedMs * 3.6f;

    /// <summary>
    /// Car sideslip (drift) angle in degrees, magnitude only. Because Forza's
    /// velocity is already body-frame, the angle of the velocity vector IS the
    /// sideslip: 0° means the car tracks straight ahead, larger values mean it is
    /// moving sideways (rear stepping out).
    /// </summary>
    public float DriftAngleDeg
    {
        get
        {
            var groundSpeed = MathF.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
            if (groundSpeed < DriftMinSpeedMs) return 0f;

            return MathF.Abs(MathF.Atan2(VelocityX, VelocityZ)) * (180f / MathF.PI);
        }
    }

    public float TempFlC => FToC(TireTempFl);
    public float TempFrC => FToC(TireTempFr);
    public float TempRlC => FToC(TireTempRl);
    public float TempRrC => FToC(TireTempRr);

    public float FrontSlip => MathF.Max(SlipFl, SlipFr);
    public float RearSlip => MathF.Max(SlipRl, SlipRr);

    private static float FToC(float f) => (f - 32f) * 5f / 9f;

    public static bool TryParse(ReadOnlySpan<byte> buf, out ForzaPacket packet)
    {
        packet = default;
        if (buf.Length < MinLength) return false;

        // IsRaceOn is a 32-bit boolean (s32). Zero means paused / in menu — we
        // ignore those so the HUD doesn't latch on idle frames.
        if (BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(OffsetIsRaceOn, 4)) == 0)
            return false;

        var rpm = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetRpm, 4));
        var speedMs = BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetSpeedMs, 4));
        var gear = buf[OffsetGear];

        // Sanity bounds — kills malformed packets and protects the UI from NaN.
        if (float.IsNaN(rpm) || rpm < 0f || rpm > 20000f) return false;
        if (float.IsNaN(speedMs) || speedMs < 0f || speedMs > 200f) return false;

        // Tire data is secondary — a bad sample must not drop a packet that has
        // valid speed/gear/RPM, so we clamp NaN/out-of-range values to 0.
        var tempFl = SanitizeTemp(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetTireTempFl, 4)));
        var tempFr = SanitizeTemp(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetTireTempFr, 4)));
        var tempRl = SanitizeTemp(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetTireTempRl, 4)));
        var tempRr = SanitizeTemp(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetTireTempRr, 4)));

        var slipFl = SanitizeSlip(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetSlipFl, 4)));
        var slipFr = SanitizeSlip(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetSlipFr, 4)));
        var slipRl = SanitizeSlip(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetSlipRl, 4)));
        var slipRr = SanitizeSlip(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetSlipRr, 4)));

        // Drift inputs are secondary like the tire data — a bad sample clamps to
        // 0 (yields 0° drift) rather than dropping an otherwise-valid packet.
        var velocityX = SanitizeFloat(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetVelocityX, 4)));
        var velocityZ = SanitizeFloat(BinaryPrimitives.ReadSingleLittleEndian(buf.Slice(OffsetVelocityZ, 4)));

        packet = new ForzaPacket(
            rpm, speedMs, gear,
            tempFl, tempFr, tempRl, tempRr,
            slipFl, slipFr, slipRl, slipRr,
            velocityX, velocityZ);
        return true;
    }

    private static float SanitizeTemp(float f) =>
        float.IsNaN(f) || f < 0f || f > 500f ? 0f : f;

    private static float SanitizeSlip(float f) =>
        float.IsNaN(f) || f < 0f ? 0f : f;

    // Velocity components can legitimately be negative, so we only guard against
    // NaN and absurd magnitudes that would poison the drift trig.
    private static float SanitizeFloat(float f) =>
        float.IsNaN(f) || MathF.Abs(f) > 1000f ? 0f : f;
}
