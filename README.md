# LoupixDeck.Plugin.ForzaHorizon6

A [LoupixDeck](https://github.com/) plugin that displays **Forza Horizon Data-Out** telemetry live on the touch buttons of a Loupedeck device.

Run the **Show Forza HUD** command to take the touch grid over in *Exclusive Mode* and show a HUD with speed, gear, RPM, grip, drift and tire-temperature readouts — until the user taps `EXIT`. The HUD is never entered automatically; it appears only when you run the command. It claims **only the touch buttons**, so the dials, the hardware buttons and the side displays keep working normally while it is up.

## Features

- **Live HUD** on the 5×3 grid (designed for the Loupedeck Live S):
  - Speed (km/h), gear, RPM
  - **Grip warning** with axle detection (`FRONT SLIP` / `REAR SLIP`) and a blinking critical tier
  - **Drift angle** in degrees with a color ramp (green → yellow → red)
  - **Tire temperatures** for all four corners as a 2×2 block, color-coded (cold = blue, optimal = green, hot = red)
- **Command-started takeover** (`ForzaHorizon6.Activate`, "Show Forza HUD"): bind it to a button and run it to enter Exclusive Mode and show the HUD. It never starts on its own, and does not reappear after an exit or a profile switch — run the command again to bring it back. If another plugin owns the display, the command is a no-op and logs a warning.
- **Touch-buttons-only takeover** (`ExclusiveControlScope.TouchButtons`, SDK 1.19.0): the HUD occupies the grid and nothing else. Rotary turns and presses, the hardware buttons and the side displays stay on the user's own page assignments — adjust the volume or switch rotary pages without leaving the HUD.
- **Manual exit:** By tapping the `EXIT` tile (slot 0). The hardware buttons are no longer claimed, so they run their normal commands instead.
- **Efficient rendering** via `DirtyTiles`: only the tiles whose content actually changed are re-sent.

## How it works

- **`Udp/ForzaUdpListener.cs`** – Receives the UDP datagrams on a background task, parses them, and forwards valid packets throttled to ~20 Hz (Forza sends at 60 Hz).
- **`Telemetry/ForzaPacket.cs`** – Parses the Forza "Car Dash V2" packet. Accounts for the 12-byte `HorizonPlaceholder` that shifts the dash section relative to the FM7 layout (e.g. Speed 244 → 256, Gear 307 → 319). Rejects packets while in a menu/paused (`IsRaceOn == 0`) and sanitizes faulty secondary values instead of dropping the whole packet.
- **`Mode/ForzaExclusiveProvider.cs`** – Builds the HUD layout from the current packet and handles button/touch input.
- **`Commands/ActivateCommand.cs`** – The "Show Forza HUD" start command (`ForzaHorizon6.Activate`).
- **`ForzaHorizon6Plugin.cs`** – Entry point; wires up the listener, provider and command.

## Requirements

- .NET SDK **9.0**
- LoupixDeck host with `LoupixDeck.PluginSdk` **1.19**
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
