"""Rebuild the September 8 bench report from archived, read-only sweep evidence."""
import cmath
import csv
import hashlib
import json
import math
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]
SESSION = ROOT / 'tuning-20260908-102922-2310dc74a8e6452fb131b5ea5d0a2841'
OUT = ROOT / 'docs/experiments/2026-09-08'
OUT.mkdir(parents=True, exist_ok=True)
ARCHIVE = OUT / 'evidence'
ARCHIVE.mkdir(exist_ok=True)
# Archive completed evidence, excluding mutable observer and command files.
if SESSION.exists():
    for name in ('session.json', 'target-10L-9C.json'):
        shutil.copy2(SESSION / name, ARCHIVE / name)
    for folder in SESSION.iterdir():
        if not folder.is_dir() or not (folder / 'result.json').exists():
            continue
        dest = ARCHIVE / folder.name
        dest.mkdir(exist_ok=True)
        for name in ('instruction.json', 'result.json', 'raw.s2p', 'corrected.s2p'):
            if (folder / name).exists():
                shutil.copy2(folder / name, dest / name)
    shutil.copy2(ROOT / 'calibration2.cal', ARCHIVE / 'calibration2.cal')
    shutil.copy2(ROOT / 'board-10-cells-gentle-gradient.json', ARCHIVE / 'original-target-10L-10C.json')

target = json.loads((ARCHIVE / 'target-10L-9C.json').read_text(encoding='utf-8-sig'))

def read_sweep(path):
    rows = []
    for line in path.read_text().splitlines():
        if not line.strip() or line.startswith(('!', '#')):
            continue
        v = list(map(float, line.split()))
        rows.append((v[0], complex(v[1], v[2]), complex(v[3], v[4])))
    assert all(b[0] > a[0] for a, b in zip(rows, rows[1:]))
    return rows

def simulate(f):
    a, b, c, d = 1, 0, 0, 1
    for e in target['Structure']:
        x = 2j * math.pi * f * e['Value']
        if e['Type'] == 'SeriesL':
            b, d = a*x+b, c*x+d
        elif e['Type'] == 'ShuntC':
            a, c = a+b*x, c+d*x
        else:
            raise ValueError(e['Type'])
    den = a+b/50+c*50+d
    return (a+b/50-c*50-d)/den, 2/den

def phase(rows):
    values = [cmath.phase(rows[0][2])]
    for a, b in zip(rows, rows[1:]):
        values.append(values[-1] + cmath.phase(b[2]/a[2]))
    return values

def metrics(rows):
    band = [r for r in rows if 10e6 <= r[0] <= 20e6]
    phases = phase(band)
    span = phases[-1]-phases[0]
    return dict(error=sum(abs(s-simulate(f)[0])**2+abs(t-simulate(f)[1])**2 for f,s,t in band)/len(band),
                reflected=100*sum(abs(s)**2 for f,s,t in band)/len(band),
                transmitted=100*sum(abs(t)**2 for f,s,t in band)/len(band),
                phase_deg=span*180/math.pi,
                delay_ns=-span/(2*math.pi*(band[-1][0]-band[0][0]))*1e9)

steps = []
for folder in ARCHIVE.iterdir():
    if not folder.is_dir():
        continue
    result = json.loads((folder/'result.json').read_text())
    instruction = json.loads((folder/'instruction.json').read_text())
    entry = dict(id=folder.name, timestamp=result['timestamp'], state=result['state'], instruction=instruction['Message'])
    if result['state'] == 'complete':
        entry.update(metrics(read_sweep(folder/'corrected.s2p')))
    steps.append(entry)
steps.sort(key=lambda s:s['timestamp'])
with (OUT/'tuning_steps.csv').open('w', newline='', encoding='utf-8') as stream:
    writer=csv.DictWriter(stream, fieldnames=['id','timestamp','state','error','reflected','transmitted','phase_deg','delay_ns','instruction'])
    writer.writeheader(); writer.writerows(steps)

baseline_id='b53d8fe647534abcb9b600f3acea7624'
final_id='e54487beff424d418a1c4080ee5982df'
baseline=read_sweep(ARCHIVE/baseline_id/'corrected.s2p')
final=read_sweep(ARCHIVE/final_id/'corrected.s2p')
ideal=[(f,*simulate(f)) for f,s,t in final]
bm,fm,tm=map(metrics,(baseline,final,ideal))
assert len(target['Structure'])==19
assert sum(e['Type']=='SeriesL' for e in target['Structure'])==10
assert sum(e['Type']=='ShuntC' for e in target['Structure'])==9
assert len(final)==401
assert abs(fm['delay_ns']-96.2850363969)<1e-7
assert abs(tm['delay_ns']-91.6570066188)<1e-7

comparison=[]
for name,rows in [('baseline',baseline),('final',final),('target',ideal)]:
    band=[r for r in rows if 10e6<=r[0]<=20e6]
    ph=phase(band)
    for (f,s,t),angle in zip(band,ph):
        comparison.append(dict(source=name,frequency_hz=f,s11_real=s.real,s11_imag=s.imag,s21_real=t.real,s21_imag=t.imag,s11_db=20*math.log10(abs(s)),s21_db=20*math.log10(abs(t)),s21_phase_relative_deg=(angle-ph[0])*180/math.pi))
with (OUT/'band_comparison.csv').open('w',newline='',encoding='utf-8') as stream:
    writer=csv.DictWriter(stream,fieldnames=list(comparison[0]));writer.writeheader();writer.writerows(comparison)

report=f'''# RF LC board: guided tuning and delay comparison — 8 September 2026

## Result and scope of the conclusion

The ten-coil, nine-capacitor board produced a measured transmission phase change and band-average group delay close to the ideal LC calculation in the intended 10–20 MHz band. At the final saved checkpoint, measured delay was **{fm['delay_ns']:.2f} ns**, versus **{tm['delay_ns']:.2f} ns** calculated: a **{100*(fm['delay_ns']/tm['delay_ns']-1):.2f}%** difference. Measured phase change was **{fm['phase_deg']:.2f}°**, versus **{tm['phase_deg']:.2f}°** calculated. These are closely agreeing end-to-end delay results, consistent with the intended LC slow-wave/delay behavior.

The evidence supports the practical research objective of obtaining substantial RF signal delay from a compact LC ladder. It does not, by itself, establish a local electromagnetic field velocity, a spatially gentle internal gradient, or actual individual component values. Those require additional reference-plane validation and spatial/component information. The agreement is strongest for band-average phase slope/delay; it is not equally close for transmission magnitude or dissipative behavior.

## Research purpose

The project investigates artificial RF transmission media made from cascaded inductors and capacitors. The aim is meaningful phase accumulation and delay with controlled reflection and transmission loss. The bench tuning sought to bring the real board's complex S11/S21 response toward a selected ideal calculation. Maximizing transmitted power alone was not the tuning objective. Some accepted adjustments improved the complex response comparison while slightly reducing transmitted power.

## Board, ports, and hardware information

- User-confirmed installed topology: **10 physical coils and 9 shunt capacitors**, starting and ending with a series coil.
- NanoVNA port 1 connects to **TP1**; port 2 connects to **TP23**.
- The user reported no fixed component values installed; adjustable capacitors span approximately **40–100 pF**.
- Each coil was reported to span approximately **0.5 µH without ferrite to 1.3 µH with ferrite centered**. These ranges were supplied by the user, not measured during this session.
- Ferrites initially were inserted; the first and second coils were described as partly inserted from one end. Capacitor starting values were unknown.
- All physical trimmers are accessible, as confirmed by the user and board photograph.
- Component numbering in the instructions means **physical order counted from TP1**. It must not be confused with schematic reference numbers: the first physical capacitor was discussed as schematic C2; the second physical coil is not necessarily schematic L2.
- The schematic contains additional lower rows. Their electrical isolation was asked about but not explicitly confirmed in the conversation. The analysis assumes the stated ten-coil/nine-capacitor path is the entire active network.
- Actual active structure length and cell pitch were not measured. The JSON's 5 mm spacing is a modeling setting, not verified board geometry.

## Calculation target and topology correction

The user supplied `board-10-cells-gentle-gradient.json`: 10–20 MHz, 401 points, 50 Ω, ten series inductors increasing linearly from 1.000 to 1.100 µH, and ten 70 pF shunt capacitors. The last capacitor was removed in a separate session target to match the physical board. The original file was not edited.

The comparison uses [the corrected target](evidence/target-10L-9C.json). The supplied original is archived as [original-target-10L-10C.json](evidence/original-target-10L-10C.json).

| Physical coil from TP1 | Target inductance (µH) | Following capacitor (pF) |
|---|---:|---:|
'''
for i in range(10):
    report+=f"| {i+1} | {1+i/90:.6f} | {'70' if i<9 else 'None — output port'} |\n"
report+='''
The desired gentle gradient is in inductance; target capacitance is constant. Physical ferrite insertion was not converted to a calibrated inductance value. In particular, retaining the first ferrite at the center does not prove its target value of 1.000 µH was achieved. Response matching may compensate for other component or fixture differences.

## Instrument and acquisition procedure

The saved results identify COM3, firmware 0.6.0, NanoVNA F V2 profile, with software correction using `calibration2.cal`. The calibration table is archived with the evidence. Software calibration assumes ideal short/open/50 Ω load and zero-delay through standards. Instrument calibration state remained unverified. Raw and corrected data were both retained; only corrected data were used for the reported tuning metrics.

Every guided acquisition used 5–50 MHz, 401 points, 50 Ω. The application acquires four overlapping 101-point segments: 5–16.25, 16.25–27.5, 27.5–38.75, and 38.75–50 MHz. Removing the repeated boundary points gives 401 points. The NanoVNA display remains on the final 38.75–50 MHz segment; this is expected and does not indicate an incomplete saved sweep.

The analysis selects the existing measured samples within 10–20 MHz: **89 points from 10.0625 to 19.9625 MHz**, spaced by 112.5 kHz. It evaluates the ideal target at exactly those frequencies. No interpolation or invented 10/20 MHz endpoint samples are used.

## Human/application/assistant adjustment loop

1. The user opened the device, selected calibration, and started a guided session in the application.
2. The assistant submitted one plain-text instruction with `rf_sim_cli --tuning-submit SESSION_FOLDER "INSTRUCTION"`.
3. The application displayed the instruction and waited. Instructions did not physically move a component or automatically initiate RF acquisition.
4. The user marked the starting position, made a single small adjustment, removed the hand/tool, and clicked **Done — measure**. When the user had already adjusted in response to chat, the subsequent application instruction explicitly said **ALREADY MADE; do NOT adjust further**.
5. The application acquired the full sweep, applied the selected calibration once, and saved `raw.s2p`, `corrected.s2p`, and terminal `result.json` in the step folder.
6. The assistant actively observed completion using `--tuning-wait SESSION_FOLDER STEP_ID 600`, then compared the corrected sweep with the prior step and the target.
7. Favorable positions were marked and retained. Unfavorable trials were undone to the preceding mark. Repeated measurements without touching anything checked short-term stability; return measurements checked mechanical reproducibility.

Clockwise/counterclockwise was viewed from the adjustment side. Nominal turns were 1/16, 1/32, and 1/64 (22.5°, 11.25°, and 5.625°). These were hand-adjustment requests, not encoder readings or calibrated capacitance changes. Direction was established experimentally; clockwise must not be assumed to increase capacitance. Coil movements were approximate 0.25–0.5 mm requests. End stops were not to be forced.

An intermittent observer-file sharing error interrupted some CLI waits. The same step ID was waited on again; no duplicate measurement was submitted for these observer retries. This is a software robustness issue, distinct from the hardware measurement result. The observer heartbeat indicates active waiting, not autonomous analysis after a chat turn ends.

## Comparison mathematics and acceptance logic

The ideal solver cascades the series impedance matrix `[1 Z; 0 1]` and shunt admittance matrix `[1 0; Y 1]`, with Z=jωL and Y=jωC. For total ABCD and Z0=50 Ω:

```
D = A + B/Z0 + C*Z0 + D_matrix
S11 = (A + B/Z0 - C*Z0 - D_matrix) / D
S21 = 2 / D
E = mean(|S11_measured - S11_target|² + |S21_measured - S21_target|²)
```

E is a dimensionless equal-weight complex response error. A smaller value indicates closer agreement in this specific metric; it is not a percentage accuracy, a component-value fit, or proof of a unique internal circuit. The quoted step improvement is `100*(E_before-E_after)/E_before`. No fitted cable delay, reference-plane shift, amplitude scaling, or loss correction is applied to improve the fit.

Reported reflection and transmission are arithmetic band averages of `100*|S11|²` and `100*|S21|²`. They are power ratios at the measurement reference planes, not radiated efficiency. The ideal L/C network is lossless, so its two ratios sum to 100%; a measured deficit cannot be uniquely assigned to coil loss, dielectric loss, radiation, or calibration error from these data alone.

Phase is unwrapped using the principal phase difference of adjacent complex S21 samples within the selected band. Band-average group delay is:

```
tau = -(phase_last - phase_first) / (2*pi*(f_last-f_first))
```

This is the interval average of phase slope, not a claim that delay is constant at every frequency. It is not absolute phase delay and is not a time-domain pulse measurement. Group delay as phase slope is described in [Keysight's group-delay tutorial](https://helpfiles.keysight.com/csg/e5080b/Tutorials/Group_Delay6_5.htm). Repeatability uses RMS of the pointwise S21 magnitude differences in dB, which characterizes magnitude stability; the target error also includes complex phase information.

## Overall measured results

'''
report+='| Quantity, selected 10–20 MHz samples | Starting board | Final checkpoint | Ideal target |\n|---|---:|---:|---:|\n'
for title,key,unit in [('Complex response error','error',''),('Mean reflected power','reflected','%'),('Mean transmitted power','transmitted','%'),('S21 phase change','phase_deg','°'),('Band-average group delay','delay_ns',' ns')]:
    report+=f'| {title} | {bm[key]:.4f}{unit} | {fm[key]:.4f}{unit} | {tm[key]:.4f}{unit} |\n'
report+=f'''
The complex error decreased **{100*(1-fm['error']/bm['error']):.2f}%** from the starting board. This should not be written as “the board is {100*(1-fm['error']/bm['error']):.2f}% accurate.” The delay agreement is quantified independently above. Transmission remains materially below the ideal target despite close delay agreement.

Final analyzed checkpoint: `{final_id}`, acquired at **2026-09-08 12:35:38 -04:00**. Baseline: `{baseline_id}`, acquired at **10:30:46 -04:00**. The later screenshot displayed a separate acquisition at about 12:36:56 and a comparison snapshot with **20 elements**. Its numerical curves cannot be substituted for this archived 19-element calculation. The screenshot and photographs are conversation evidence and are not embedded in this archive; their original image files were not available in the workspace.

## Adjustment progression and retained physical state

- **First capacitor:** clockwise and counterclockwise trials did not provide a useful combined improvement; it was returned to the original mark. The first return reproduced S21 within about 0.043 dB RMS.
- **First coil:** withdrawal worsened target error about 6.7%. After restoration, inward movement improved it about 9.9%, confirmed by a repeat. A further inward move improved it about 1.8%. The user reported the ferrite was then centered, and it was retained there.
- **Second coil:** inward and outward trials produced small response/power tradeoffs. It was restored to its starting position. Other coils were not deliberately tuned in this session.
- **Second capacitor:** clockwise trials improved target agreement substantially, with some transmission penalty. Retained approximately 1/16 + 1/32 + 1/32 + 1/64 = **9/64 turn clockwise** from its initial mark, based on requested movements.
- **Third capacitor:** clockwise adjustments improved both target agreement and power transfer. Retained approximately 1/32 + 1/32 + 1/32 + 1/64 = **7/64 turn clockwise**. A no-touch repeat differed by about 0.009 dB RMS in S21.
- **Fourth capacitor:** clockwise was unhelpful and was undone. Counterclockwise improved the response over successive small steps. The last extra step worsened target error by about 5.5% and was undone. Retained nominally **10/64 turn counterclockwise** from the initial mark (initial 1/32 plus eight retained 1/64 steps). Mechanical return error means this is only an adjustment history, not an exact final setting.
- **Fifth capacitor:** clockwise worsened target error and was undone. The returned response differed by about 0.20 dB RMS but was stable on repeat. Four counterclockwise 1/64 steps were retained from that returned mark; a fifth worsened error and was undone. Final reference is the actual marked return position, not an assumed capacitance.
- **Sixth capacitor:** clockwise worsened target agreement and was undone. Two counterclockwise 1/64 steps were retained; they improved target error while slightly worsening power transfer.
- **Seventh capacitor:** both tested directions worsened target agreement. It was returned to its starting mark.
- **Eighth capacitor:** two clockwise 1/64 steps were retained; the first had a substantial effect, the second only a small effect.
- **Ninth capacitor:** the first clockwise 1/64 step gave a small repeatable improvement. A second step was essentially unchanged and was undone. The final return was verified.

These retained settings describe a tuned response, not a verified 70 pF-per-capacitor or 1.000–1.100 µH-per-coil distribution. A second tuning pass could change previous optima because the network elements interact.

## Slow-wave interpretation and limitations

The measured ~96 ns versus calculated ~92 ns band-average delay is evidence consistent with the intended artificial-line behavior. Inductors and capacitors provide electromagnetic energy storage and a frequency-dependent transmission phase. To quantify an effective speed from length/delay, the physical path length and excess delay relative to a verified reference must be established. End-to-end group delay includes the consequences of finite-network reflections and resonances; it is not automatically the local phase velocity or a Bloch propagation constant.

The evidence does not establish that electromagnetic waves in surrounding free space have slowed, nor that a high internal slow-wave factor proves useful radiation efficiency. No near-field scan, internal node-phase measurement, calibrated component measurement, or time-domain pulse comparison was performed.

Calibration limitations are material: corrected S11 exceeded 0 dB around 7.6–7.7 MHz in later steps; the anomaly was also present in raw data. It lies outside the selected analysis band but prevents treating repeatability as proof of absolute calibration accuracy. The precise cause was not resolved. Full-band 5–50 MHz delay summaries fluctuated dramatically while in-band repeats remained stable. Very weak transmission and phase unwrapping make those full-band summaries unsuitable evidence for the claimed delay agreement. The final report deliberately uses the selected transmitting band and does not interpret deep-stopband dB minima as calibrated dynamic-range measurements.

## Recommended next validation, without losing the tuned state

1. Preserve all physical marks and archive the current measurement before changing any connections.
2. Verify calibration with known standards and a direct-through/reference measurement at the intended board reference planes; document the instrument calibration state and investigate S11 above 0 dB.
3. Acquire a dedicated 10–20 MHz sweep and compare it against the corrected 19-element target at identical frequencies. Verify actual GUI model topology before using its comparison button.
4. Measure the active board length/cell pitch. With reference delay removed, report excess group delay and an explicitly defined effective velocity if appropriate.
5. If confirming the gentle internal gradient is required, measure individual components or internal node phase with a method that accounts for probe loading. End-to-end fitting alone is insufficient.

## Evidence and reproducibility

All recorded step instructions, terminal results, and available raw/corrected sweeps are archived under [evidence](evidence/). No additional RF acquisition is performed by the report builder. The original session remains untouched. [tuning_steps.csv](tuning_steps.csv) contains all chronological step metrics and exact instructions; [band_comparison.csv](band_comparison.csv) contains starting, final, and calculated complex responses and relative phase at each selected frequency. The [SHA-256 manifest](evidence_sha256.csv) supports later integrity checks.

Rebuild from the repository root with `python docs/build_tuning_report.py`. If the original session exists, the builder refreshes the evidence copies; otherwise it uses the archive. The calibration copy is the file present at documentation time; acquisition metadata names its path but does not record a historical content hash, so unchanged calibration contents during the entire experiment cannot be independently proven by the log.

## Complete chronological adjustment record

The instruction is reproduced verbatim as historical experiment data, not as a new instruction to operate the board. All metrics below use the corrected 19-element target and selected 10–20 MHz samples. Times retain the recorded local UTC offset in the CSV. “Delay” is the selected-band phase-slope delay recalculated for this report, not the application's full-band summary.

'''
for i,s in enumerate(steps,1):
    report+=f"### Step {i:02d} — {s['timestamp']}\n\nID: `{s['id']}`; state: **{s['state']}**.\n\n"
    report+='> '+s['instruction'].replace('\n','\n> ')+'\n\n'
    if 'error' in s:
        report+=f"Target error: **{s['error']:.6f}**; mean reflected power: **{s['reflected']:.3f}%**; mean transmitted power: **{s['transmitted']:.3f}%**; phase change: **{s['phase_deg']:.3f}°**; delay: **{s['delay_ns']:.3f} ns**.\n\n"
    report+=f"[Recorded result](evidence/{s['id']}/result.json)"
    if 'error' in s:
        report+=f" · [Corrected sweep](evidence/{s['id']}/corrected.s2p) · [Raw sweep](evidence/{s['id']}/raw.s2p)"
    report+='\n\n'
addendum = OUT/'screenshot-addendum.md'
if addendum.exists():
    report = report.replace('## Complete chronological adjustment record', addendum.read_text(encoding='utf-8').rstrip()+'\n\n## Complete chronological adjustment record', 1)
(OUT/'report.md').write_text(report.rstrip()+'\n',encoding='utf-8')
with (OUT/'evidence_sha256.csv').open('w',newline='',encoding='utf-8') as stream:
    writer=csv.writer(stream);writer.writerow(['file','bytes','sha256'])
    for path in sorted(ARCHIVE.rglob('*')):
        if path.is_file():
            data=path.read_bytes();writer.writerow([path.relative_to(OUT).as_posix(),len(data),hashlib.sha256(data).hexdigest()])
print(f'Report generated: {len(steps)} steps; final delay {fm["delay_ns"]:.6f} ns; target {tm["delay_ns"]:.6f} ns')
