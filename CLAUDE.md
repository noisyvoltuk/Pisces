# Pisces — Project Context for Claude Code

## What is Pisces?

Pisces is a modular CSound synthesizer running on a Raspberry Pi 4, controlled via physical hardware
controls (rotary encoders, toggle switches, a rotary selector) and a Blazor Server web interface.
It is designed to be open and extensible — new CSound modules (VCOs, VCFs, effects) can be added
without touching core code.

## Hardware

- **Raspberry Pi 4** (target deployment platform)
- **HiFiBerry DAC+ Light** — I2S audio output (hw:0,0)
- **5 × SSD1306 OLED displays** (128x64) via I2C + TCA9548A multiplexer
- **1 × ST7789 or ILI9341 colour TFT** (320x240) via SPI — module selector display
- **5 × rotary encoders with push button** — 1 selector + 4 parameter encoders
- **1 × rotary selector switch** — waveform selection
- **2 × toggle switches** — on/off parameters (filter bypass, OSC sync)
- **2 × momentary buttons** — patch up/down
- **USB MIDI** — note input direct to CSound via -Ma flag

## Solution Structure

```
Pisces.sln
├── Pisces.Core          — domain models and interfaces, zero external dependencies
├── Pisces.Web           — Blazor Server UI + SignalR hub, port 5000
├── Pisces.Hardware      — GPIO, I2C, SPI drivers — Pi only, Linux/ARM
├── Pisces.CSound        — CSound integration via OSC
├── Pisces.Infrastructure — event bus, state service, JSON repositories, config
└── Pisces.Simulator     — virtual hardware panel for Windows development
```

## Dependency Rules — STRICTLY ENFORCED

```
Pisces.Web           → Pisces.Core, Pisces.Infrastructure
Pisces.Hardware      → Pisces.Core
Pisces.CSound        → Pisces.Core
Pisces.Infrastructure → Pisces.Core
Pisces.Simulator     → Pisces.Core
```

- `Pisces.Core` has **zero** external NuGet dependencies and **zero** references to other Pisces projects
- Nothing outside `Pisces.Core` may reference another non-Core Pisces project directly
- All inter-service communication goes through `IEventBus` — services never call each other directly
- Hardware pin numbers live **only** in `appsettings.json` and `HardwareConfig` — never hardcoded

## Key Interfaces (Pisces.Core)

| Interface | Purpose | Implementations |
|---|---|---|
| `ICsoundEngine` | Start/stop patches, set channels | `CsoundOscClient`, `SimulatedCsoundEngine` |
| `IDisplayDriver` | Write to a single display | `OledDisplay`, `TftDisplay`, `SimulatedDisplay` |
| `IControlInput` | Hardware control events | `EncoderBank`, `SimulatedControlInput` |
| `IPatchRepository` | Patch persistence | `JsonPatchRepository` |
| `IModuleMap` | Module definition read/write | `JsonModuleMapRepository` |
| `ISynthStateService` | Shared runtime state | `SynthStateService` |
| `IEventBus` | In-process pub/sub | `InProcessEventBus` |

## Events (Pisces.Core.Events)

All defined in `SynthEvents.cs`. Use these — do not add direct service calls.

| Event | Published by | Consumed by |
|---|---|---|
| `ParameterChangedEvent` | `ControlDaemonService` | `CsoundEngine`, `DisplayDaemonService`, SignalR hub |
| `ModuleSelectedEvent` | `ControlDaemonService` | `DisplayDaemonService`, SignalR hub |
| `PatchLoadedEvent` | Patch service | All |
| `ToggleChangedEvent` | `ControlDaemonService` | `CsoundEngine`, `DisplayDaemonService` |
| `ButtonPressedEvent` | `ControlDaemonService` | Patch service |
| `WaveSelectedEvent` | `ControlDaemonService` | `CsoundEngine`, `DisplayDaemonService` |
| `PatchSwitchingEvent` | Patch service | `DisplayDaemonService`, SignalR hub |
| `SelectorPressedEvent` | `ControlDaemonService` | Module selection service |

## Three-Layer Control Mapping

Physical control → abstract slot → CSound channel

1. **Layer 1 — Physical to slot** (fixed, in `appsettings.json`)
   Enc1 always drives `param1`, Enc2 drives `param2` etc. Never changes.

2. **Layer 2 — Slot to channel** (per module, in `data/module_map.json`)
   `param1` means `vcf_cutoff` when VCF is selected, `vco_fm_ratio` when FM VCO is selected.

3. **Layer 3 — Value scaling** (in `ParameterSlot.Min/Max/Logarithmic`)
   Normalised encoder value (0.0–1.0) scaled to CSound units.

## CSound Architecture

CSound runs as a **separate systemd service** (`pisces-csound.service`), independent of the .NET app.
The .NET app communicates with it via **OSC** (`OscClient`) — never by owning the process.

Each CSound module is a **UDO** (User Defined Opcode) in `csound/udos/`.
Standard UDO channel naming convention:
- VCO channels: `vco_{modulevariant}_{param}` e.g. `vco_fm_ratio`, `vco_fm_index`
- VCF channels: `vcf_{param}` e.g. `vcf_cutoff`, `vcf_resonance`
- Filter ADSR: `flt_att`, `flt_dec`, `flt_sus`, `flt_rel`, `flt_env_amt`
- Amp ADSR: `amp_att`, `amp_dec`, `amp_sus`, `amp_rel`
- LFO: `lfo_rate`, `lfo_depth`, `lfo_shape`
- FX: `fx_reverb_mix`, `fx_reverb_size`, `fx_delay_time`, `fx_delay_mix`
- Toggles: `vcf_bypass`, `vco_sync`

Signal flow: `VCO → VCF → VCA → FX → OUT`
Both VCF and VCA have independent ADSRs.
MIDI drives notes directly — no Python or .NET in the audio path.

## Display Layout

```
[TFT — module selector]          ← SPI, top of panel, colour display
[OLED 0]  [OLED 1]  [OLED 2]  [OLED 3]   ← I2C via TCA9548A
param1+2  param3+4  waveform   toggles
[enc1][enc2]  [enc3][enc4]  [wave sw]  [tog1][tog2]
```

Each OLED shows 2 rows — one per encoder or control it sits above.
Active parameter (being turned) expands to show a value bar.
TFT shows all module roles, currently selected role highlighted, key parameter summary.

## Display Roles

| Display | Index | Role | Content |
|---|---|---|---|
| OLED | 0 | `params_1_2` | param1 label+value, param2 label+value |
| OLED | 1 | `params_3_4` | param3 label+value, param4 label+value |
| OLED | 2 | `wave_selector` | current waveform, available options |
| OLED | 3 | `toggles` | toggle 1 label+state, toggle 2 label+state |
| TFT | — | `module_selector` | all module roles, selected role, param summary |

## Development on Windows

Set `"UseSimulator": true` in `appsettings.json`.
The `Pisces.Simulator` project provides `SimulatedControlInput` and `SimulatedDisplay`
which are registered via DI instead of the real hardware implementations.
The Blazor UI includes a virtual panel page (`/simulator`) when simulator mode is active.
CSound is not required in simulator mode — `SimulatedCsoundEngine` logs channel changes.

## Naming Conventions

- Interfaces: `IFoo` in `Pisces.Core.Interfaces`
- Events: `FooEvent` records in `Pisces.Core.Events.SynthEvents`
- Background services: `FooService : BackgroundService` in `Pisces.Infrastructure.Services`
- Hardware drivers: `FooDriver` or `FooDisplay` in `Pisces.Hardware`
- Config classes: `FooConfig` bound from `appsettings.json` via `IOptions<FooConfig>`
- Repositories: `JsonFooRepository` in `Pisces.Infrastructure.Repositories`

## Deployment

Target runtime: `linux-arm64` (Pi 4, 64-bit Pi OS)

```bash
dotnet publish Pisces.Web -r linux-arm64 -c Release --self-contained
scp -r bin/Release/net8.0/linux-arm64/publish/ pi@raspberrypi.local:~/pisces/app/
ssh pi@raspberrypi.local 'sudo systemctl restart pisces-web'
```

Systemd services on the Pi:
- `pisces-csound.service` — CSound engine, starts first
- `pisces-web.service` — .NET Blazor app (controls + display + web UI)

## What to Build Next

In priority order:
1. `DisplayDaemonService` — `IHostedService` that subscribes to events and drives OLEDs + TFT
2. `JsonModuleMapRepository` — load/save module definitions from `data/module_map.json`
3. `JsonPatchRepository` — load/save patches from `data/patches/`
4. `CsoundOscClient` — implement `ICsoundEngine` via OSC
5. Blazor pages — Home (monitor), Modules (module map editor), Patches (patch builder)
6. `EncoderBank` — real GPIO implementation of `IControlInput` (Pi only)
7. `OledDisplay` — SSD1306 via I2C + TCA9548A
8. `TftDisplay` — colour TFT via SPI
9. Simulator virtual panel Blazor page

## What NOT to Do

- Do not add logic to `Pisces.Core` that depends on hardware, file I/O, or frameworks
- Do not call services directly — always use `IEventBus`
- Do not hardcode GPIO pin numbers — always read from `HardwareConfig`
- Do not put CSound channel names as string literals scattered in code —
  they must come from `ParameterSlot.Channel` resolved via `IModuleMap`
- Do not block async code with `.Result` or `.GetAwaiter().GetResult()` except
  where absolutely unavoidable in GPIO interrupt handlers