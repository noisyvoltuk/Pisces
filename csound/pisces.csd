<CsoundSynthesizer>

; =============================================================================
;  Pisces — master orchestra (starter)
;
;  Signal flow:  VCO -> VCF -> VCA -> FX -> OUT
;  Control:      OSC on UDP 7770  (see the OSC convention in CLAUDE.md)
;  Notes:        USB MIDI straight into instr 2 (massign 0, 2)
;
;  This is a starting point, not the final orchestra. What's real:
;    - full OSC handshake (param / toggle / module / ping-pong / patch bracket)
;    - one VCO (saw + square blend + sub), Moog ladder VCF, filter + amp ADSRs
;    - two LFOs (rate / depth / shape) summed into pitch and cutoff
;    - reverb + delay send, filter bypass toggle
;  What's stubbed (commented TODO):
;    - vco_lfo_fade / vcf_lfo_retrig  (need a note-on gate)
;    - vco_saw module is the only VCO/VCF/FX variant; /pisces/module is logged only
; =============================================================================

<CsOptions>
; Audio out + no display. Add MIDI and tune the device on the command line —
; it's platform-specific:
;   Linux/ALSA : -Ma                 (all MIDI, merged)
;   Windows    : -M0  or  -Mm        (PortMIDI wants a number, not a name)
;   Pi + DAC   : -odac:hw:0,0
;   OSC-only   : replace -odac with -n  (no sound)
-odac -d
</CsOptions>

<CsInstruments>

sr     = 48000
ksmps  = 32
nchnls = 2
0dbfs  = 1

massign 0, 2                     ; all MIDI channels -> instr 2 (voice)

gi_osc   OSCinit 7770            ; .NET -> csound
giReplyPort = 7771               ; csound -> .NET  (must match Csound:OscListenPort)

ga_fxL   init 0
ga_fxR   init 0

; ------------------------------------------------------------------ helpers ---

; Multi-shape LFO, bipolar -1..1, live-switchable shape.
;   kshape: 0 sine | 1 triangle | 2 square | 3 saw
opcode pi_lfo, k, kk
  krate, kshape xin
  kph    phasor krate
  ksine  = sin(kph * 2 * 3.14159265)
  ktri   = 2 * abs(2 * kph - 1) - 1
  ksqr   = (kph < 0.5 ? 1 : -1)
  ksaw   = 2 * kph - 1
  kout   = (kshape < 0.5 ? ksine : kshape < 1.5 ? ktri : kshape < 2.5 ? ksqr : ksaw)
  xout kout
endop

; =========================================================== instr 99: init ==
; Seed every control channel with the module_map.json defaults so the synth
; makes sound before the .NET app has pushed anything.
instr 99
  chnset 0,     "vco_saw_tune"
  chnset 0,     "vco_saw_fine"
  chnset 0.5,   "vco_saw_shape"
  chnset 0,     "vco_saw_sub"

  chnset 1200,  "vcf_cutoff"
  chnset 0.2,   "vcf_resonance"
  chnset 0.3,   "flt_env_amt"
  chnset 0,     "vcf_keytrack"

  chnset 5,     "flt_att"
  chnset 200,   "flt_dec"
  chnset 0.4,   "flt_sus"
  chnset 300,   "flt_rel"

  chnset 5,     "amp_att"
  chnset 400,   "amp_dec"
  chnset 0.8,   "amp_sus"
  chnset 500,   "amp_rel"

  chnset 5,     "vco_lfo_rate"
  chnset 0,     "vco_lfo_depth"
  chnset 0,     "vco_lfo_shape"

  chnset 1,     "vcf_lfo_rate"
  chnset 0,     "vcf_lfo_depth"
  chnset 0,     "vcf_lfo_shape"

  chnset 0.25,  "fx_reverb_mix"
  chnset 0.6,   "fx_reverb_size"
  chnset 300,   "fx_delay_time"
  chnset 0.15,  "fx_delay_mix"

  chnset 0,     "vcf_bypass"
endin

; ==================================================== instr 1: OSC control ===
instr 1
  ; The string channel-name vars are seeded with REAL channel names (created by
  ; instr 99) so the i-time pass of chnset has a valid name to bind to. The
  ; k-rate chnset variant re-resolves the channel by name when a message updates
  ; the string.
  ; --- /pisces/param  (name, value) -> named control channel ---
  Sp    init "vco_saw_tune"
  kpv   init 0
  km1   init 1
  while km1 == 1 do
    km1 OSClisten gi_osc, "/pisces/param", "sf", Sp, kpv
    if km1 == 1 then
      chnset kpv, Sp
    endif
  od

  ; --- /pisces/toggle  (name, 0|1) ---
  St    init "vcf_bypass"
  ktv   init 0
  km2   init 1
  while km2 == 1 do
    km2 OSClisten gi_osc, "/pisces/toggle", "si", St, ktv
    if km2 == 1 then
      chnset ktv, St
    endif
  od

  ; --- /pisces/module  (role, moduleId) — logged only in the starter ---
  Srole init "vco"
  Smod  init "vco_saw"
  km3   OSClisten gi_osc, "/pisces/module", "ss", Srole, Smod
  if km3 == 1 then
    printf "module: %s -> %s\n", km3, Srole, Smod
  endif

  ; --- /pisces/patch/begin | /pisces/patch/end  — drained, no-op for now ---
  Spid  init "none"
  kb    OSClisten gi_osc, "/pisces/patch/begin", "s", Spid
  ke    OSClisten gi_osc, "/pisces/patch/end",   "s", Spid

  ; --- /pisces/ping (nonce, replyPort) -> /pisces/pong (nonce) ---
  knonce init 0
  kport  init 0
  kpg    OSClisten gi_osc, "/pisces/ping", "ii", knonce, kport
  ; reply port is i-rate for OSCsend, so we use the configured constant
  OSCsend knonce, "127.0.0.1", giReplyPort, "/pisces/pong", "i", knonce
  if kpg == 1 then
    printf "ping %d -> pong\n", knonce, knonce
  endif
endin

; ================================================= instr 20: VCO pitch LFO ===
instr 20
  krate  chnget "vco_lfo_rate"
  kdepth chnget "vco_lfo_depth"        ; cents
  kshape chnget "vco_lfo_shape"
  klfo   pi_lfo krate, kshape
  ; -> semitones on the shared pitch-mod bus
  chnset klfo * kdepth / 100, "mod_vco_pitch"
  ; TODO: vco_lfo_fade — ramp depth in over N ms after note-on
endin

; ================================================ instr 21: VCF cutoff LFO ===
instr 21
  krate  chnget "vcf_lfo_rate"
  kdepth chnget "vcf_lfo_depth"        ; semitones
  kshape chnget "vcf_lfo_shape"
  klfo   pi_lfo krate, kshape
  chnset klfo * kdepth, "mod_vcf_cutoff"
  ; TODO: vcf_lfo_retrig — reset phase on note-on when set
endin

; ======================================================= instr 2: MIDI voice =
instr 2
  inote  notnum
  ivel   veloc 0, 1

  ; ---- pitch: tune + fine + pitch-LFO ----
  ktune  chnget "vco_saw_tune"
  kfine  chnget "vco_saw_fine"
  kpmod  chnget "mod_vco_pitch"                 ; semitones
  kcps   = cpsmidinn(inote + ktune + kfine/100 + kpmod)

  ; ---- oscillators: saw + (shape)*square + (sub)*square@-1oct ----
  kshape chnget "vco_saw_shape"
  ksub   chnget "vco_saw_sub"
  asaw   vco2 0.5,           kcps,      0
  asqr   vco2 0.5 * kshape,  kcps,      2
  asb    vco2 0.5 * ksub,    kcps * 0.5, 2
  avco   = asaw + asqr + asb

  ; ---- filter envelope (channel times are in ms) ----
  iatt   chnget "flt_att"
  idec   chnget "flt_dec"
  islv   chnget "flt_sus"
  irel   chnget "flt_rel"
  kfenv  madsr iatt/1000, idec/1000, islv, irel/1000

  ; ---- cutoff in the octave domain: base + env + LFO + keytrack ----
  kbase  chnget "vcf_cutoff"
  kamt   chnget "flt_env_amt"
  ktrk   chnget "vcf_keytrack"
  kcmod  chnget "mod_vcf_cutoff"                ; semitones
  koct   = octcps(kbase) + kfenv * kamt * 5 + kcmod/12 + ((inote - 60)/12) * ktrk
  kcf    limit cpsoct(koct), 20, 20000
  kres   chnget "vcf_resonance"

  afilt  moogladder avco, kcf, kres
  kbyp   chnget "vcf_bypass"
  kwet   = (kbyp > 0.5 ? 0 : 1)                 ; a-rate ?: isn't portable — blend instead
  avcf   = afilt * kwet + avco * (1 - kwet)

  ; ---- amp envelope + VCA ----
  iaatt  chnget "amp_att"
  iadec  chnget "amp_dec"
  iasus  chnget "amp_sus"
  iarel  chnget "amp_rel"
  kaenv  madsr iaatt/1000, iadec/1000, iasus, iarel/1000

  aout   = avcf * kaenv * ivel * 0.3

  ; dry out + FX send
  outs   aout, aout
  ga_fxL += aout
  ga_fxR += aout
endin

; ======================================================= instr 30: FX bus ====
instr 30
  krsize chnget "fx_reverb_size"
  krmix  chnget "fx_reverb_mix"
  kdtime chnget "fx_delay_time"                 ; ms
  kdmix  chnget "fx_delay_mix"

  ; --- feedback delay ---
  afb    init 0
  a_unused delayr 2.1
  adel   deltapi kdtime/1000
         delayw ga_fxL + afb * 0.35
  afb    = adel

  ; --- reverb ---
  kfb    limit krsize * 0.88 + 0.1, 0, 0.97
  arL, arR reverbsc ga_fxL, ga_fxR, kfb, 12000

  outs   adel * kdmix + arL * krmix, adel * kdmix + arR * krmix

  ga_fxL = 0
  ga_fxR = 0
endin

</CsInstruments>

<CsScore>
i 99  0   0          ; seed channel defaults (i-time only, runs once)
i 1   0   z          ; OSC control listener
i 20  0   z          ; VCO pitch LFO
i 21  0   z          ; VCF cutoff LFO
i 30  0   z          ; FX bus
f 0   z              ; keep the performance alive indefinitely
</CsScore>

</CsoundSynthesizer>
