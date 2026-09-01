# Pisces — CSound engine

`pisces.csd` is the **master orchestra**. It is always loaded; module selection
happens inside it over OSC — it is never swapped or reloaded at runtime.

The .NET app never owns this process. It talks to it only over OSC (UDP loopback):
`/pisces/param`, `/pisces/toggle`, `/pisces/module`, `/pisces/ping` → `/pisces/pong`
on ports 7770 (csound listens) / 7771 (.NET listens). See CLAUDE.md for the full table.

## Prerequisites

```bash
sudo apt install csound csound-utils liblo-tools
csound -z1 2>&1 | grep -i osc      # must list OSCinit / OSClisten / OSCsend
```

If the OSC opcodes are missing your csound was built without liblo — install a
build that has it.

## Test in stages

**1. OSC handshake only — no audio.**

```bash
csound pisces.csd -n -d            # -n = no sound out
dotnet run --project ../Pisces.Web -- --Pisces:UseSimulator=false --Csound:LogUnit=
```

The web monitor should flip to **CSound engine — OSC / online**. The csound
terminal prints `ping N -> pong`.

**Check param routing works.** Add a debug line in instr 1 (inside the param
`while` loop, after `chnset`):

```csound
printf "param %s = %f\n", km1, Sp, kpv
```

Move **Cutoff** on the Patch workbench — you should see `param vcf_cutoff = ...`
and nothing else. If every slider prints the same channel name, your csound's
k-rate `chnset` isn't re-resolving the dynamic name; tell me and I'll switch
instr 1 to an explicit per-channel dispatch.

**2. Add audio.**

```bash
csound pisces.csd -odac -d                     # default (PipeWire / Pulse / ALSA)
csound pisces.csd -+rtaudio=jack -odac -d      # JACK
csound --devices                               # list output devices
```

**3. Add MIDI.** `-Ma` grabs all ALSA MIDI. No controller? use a virtual port:

```bash
sudo modprobe snd-virmidi
csound pisces.csd -odac -M hw:VirMIDI -d
# then play it:  vmpk   (or  aplaymidi -p VirMIDI:0 something.mid)
```

**4. Run as a service** (enables the journalctl log tail in the web UI):

```bash
sudo cp pisces-csound.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now pisces-csound
```

Then drop `--Csound:LogUnit=` so the app tails `journalctl -u pisces-csound`.

## Channels

Every control channel name comes from `data/module_map.json` (`ParameterSlot.Channel`).
`instr 99` seeds them all with the map's defaults so the synth makes sound before
the app pushes anything. Modulation buses: `mod_vco_pitch`, `mod_vcf_cutoff`
(semitones), summed by the VCO / VCF sections alongside the ADSRs.

## Not done yet in the starter

- `/pisces/module` is logged, not acted on — there's only one VCO/VCF/FX variant.
- `vco_lfo_fade` / `vcf_lfo_retrig` need a note-on gate.
- No `PatchRenderer`; patches are applied as live channel values, not rendered `.csd`.
- The reply port for `/pisces/pong` is the constant `giReplyPort` in the `.csd`
  (OSCsend needs an i-rate port). Keep it equal to `Csound:OscListenPort`.
- instr 1 routes `/pisces/param` by writing `chnset kval, Svar` with a runtime
  channel name. This relies on the k-rate `chnset` re-resolving the name — fine
  on Csound 6.13+. The string vars are seeded with real channel names so the
  i-time bind is valid.
