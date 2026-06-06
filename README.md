# LoupixDeck.Plugin.ForzaHorizon6

A [LoupixDeck](https://github.com/) plugin that displays **Forza Horizon Data-Out** telemetry live on the touch buttons of a Loupedeck device.

As soon as Forza starts sending telemetry packets, the plugin takes over the device in *Exclusive Mode* and shows a HUD with speed, gear, RPM, grip, drift and tire-temperature readouts — until the user exits via button.

## Features

- **Live HUD** on the 5×3 grid (designed for the Loupedeck Live S):
  - Speed (km/h), gear, RPM
  - **Grip warning** with axle detection (`FRONT SLIP` / `REAR SLIP`) and a blinking critical tier
  - **Drift angle** in degrees with a color ramp (green → yellow → red)
  - **Tire temperatures** for all four corners as a 2×2 block, color-coded (cold = blue, optimal = green, hot = red)
- **Automatic takeover:** On the first valid packet the plugin requests Exclusive Mode. If another plugin still owns the device, the sample is dropped silently and retried on the next packet.
- **Manual exit:** Via the `EXIT` hardware button (SimpleButton 0) or by tapping the `EXIT` tile.
- **Re-arm command** (`ForzaHorizon6.Activate`): Re-enables the automatic takeover after a manual exit — the next telemetry packet then triggers the transition again.
- **Efficient rendering** via `DirtyTiles`: only the tiles whose content actually changed are re-sent.

## How it works

- **`Udp/ForzaUdpListener.cs`** – Receives the UDP datagrams on a background task, parses them, and forwards valid packets throttled to ~20 Hz (Forza sends at 60 Hz).
- **`Telemetry/ForzaPacket.cs`** – Parses the Forza "Car Dash V2" packet. Accounts for the 12-byte `HorizonPlaceholder` that shifts the dash section relative to the FM7 layout (e.g. Speed 244 → 256, Gear 307 → 319). Rejects packets while in a menu/paused (`IsRaceOn == 0`) and sanitizes faulty secondary values instead of dropping the whole packet.
- **`Mode/ForzaExclusiveProvider.cs`** – Builds the HUD layout from the current packet and handles button/touch input.
- **`Commands/ActivateCommand.cs`** – The re-arm command.
- **`ForzaHorizon6Plugin.cs`** – Entry point; wires up the listener, provider and command.

## Requirements

- .NET SDK **9.0**
- LoupixDeck host with `LoupixDeck.PluginSdk` **1.6**
- Forza Horizon with **Data Out** enabled (Settings → HUD/Gameplay → Data Out):
  - Data output **ON**
  - IP of the machine running the LoupixDeck host
  - Port (default: **5607**)

## Build

```bash
dotnet build -c Release
```

The output lands without a TFM suffix in `bin\Release\` (`AppendTargetFrameworkToOutputPath=false`). The SDK DLL is **not** shipped with the plugin (`<ExcludeAssets>runtime</ExcludeAssets>`) — the host provides it.

## Release / Deploy

The release script publishes and copies only the required files into a clean plugin directory:

```powershell
./release.ps1
```

The result is placed under `dist\forzahorizon6\` (`*.dll`, `*.deps.json`, `plugin.json`).

To test, copy the contents to `<LoupixDeck>\plugins\forzahorizon6\` — `plugin.json` must sit next to the DLL.

## Configuration

| Setting | Default | Description                          |
|---------|---------|--------------------------------------|
| `port`  | `5607`  | UDP port the telemetry is received on |

The port is read from the host settings (`host.Settings`).

## HUD layout (Loupedeck Live S, 5×3)

```
┌──────┬──────┬──────┬──────┬──────┐
│ EXIT │ km/h │ GEAR │ rpm  │ GRIP │   Slots 0–4
├──────┼──────┼──────┼──────┼──────┤
│ FL°C │ FR°C │ DRIFT│      │      │   Slots 5–9
├──────┼──────┼──────┼──────┼──────┤
│ RL°C │ RR°C │      │      │      │   Slots 10–14
└──────┴──────┴──────┴──────┴──────┘
```

The tire temperatures (FL/FR over RL/RR) form a 2×2 block that spatially maps the car's corners.

## Notes

- The packet offsets follow the documented FH5 / FM "Car Dash V2" layout and assume FH6 backwards compatibility. Should the format change, the offsets in `Telemetry/ForzaPacket.cs` are the first place to look.
- `CommandName` values (e.g. `ForzaHorizon6.Activate`) are stable public API and are not renamed after release.

## License

Not yet defined.
