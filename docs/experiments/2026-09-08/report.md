# RF LC board: guided tuning and delay comparison — 8 September 2026

## Result and scope of the conclusion

The ten-coil, nine-capacitor board produced a measured transmission phase change and band-average group delay close to the ideal LC calculation in the intended 10–20 MHz band. At the final saved checkpoint, measured delay was **96.29 ns**, versus **91.66 ns** calculated: a **5.05%** difference. Measured phase change was **-343.16°**, versus **-326.67°** calculated. These are closely agreeing end-to-end delay results, consistent with the intended LC slow-wave/delay behavior.

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
| 1 | 1.000000 | 70 |
| 2 | 1.011111 | 70 |
| 3 | 1.022222 | 70 |
| 4 | 1.033333 | 70 |
| 5 | 1.044444 | 70 |
| 6 | 1.055556 | 70 |
| 7 | 1.066667 | 70 |
| 8 | 1.077778 | 70 |
| 9 | 1.088889 | 70 |
| 10 | 1.100000 | None — output port |

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

| Quantity, selected 10–20 MHz samples | Starting board | Final checkpoint | Ideal target |
|---|---:|---:|---:|
| Complex response error | 1.7916 | 0.1861 | 0.0000 |
| Mean reflected power | 58.4055% | 47.2407% | 35.8279% |
| Mean transmitted power | 24.2277% | 31.0710% | 64.1721% |
| S21 phase change | -229.2490° | -343.1599° | -326.6656° |
| Band-average group delay | 64.3235 ns | 96.2850 ns | 91.6570 ns |

The complex error decreased **89.61%** from the starting board. This should not be written as “the board is 89.61% accurate.” The delay agreement is quantified independently above. Transmission remains materially below the ideal target despite close delay agreement.

Final analyzed checkpoint: `e54487beff424d418a1c4080ee5982df`, acquired at **2026-09-08 12:35:38 -04:00**. Baseline: `b53d8fe647534abcb9b600f3acea7624`, acquired at **10:30:46 -04:00**. The later screenshot displayed a separate acquisition at about 12:36:56 and a comparison snapshot with **20 elements**. Its numerical curves cannot be substituted for this archived 19-element calculation. The screenshot and photographs are conversation evidence and are not embedded in this archive; their original image files were not available in the workspace.

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

### Step 01 — 2026-09-08T10:30:46.9559387-04:00

ID: `b53d8fe647534abcb9b600f3acea7624`; state: **complete**.

> Baseline: leave all capacitors and coils unchanged. Keep the board and cables stationary, remove your hand and adjustment tool, then click Done - measure.

Target error: **1.791628**; mean reflected power: **58.406%**; mean transmitted power: **24.228%**; phase change: **-229.249°**; delay: **64.324 ns**.

[Recorded result](evidence/b53d8fe647534abcb9b600f3acea7624/result.json) · [Corrected sweep](evidence/b53d8fe647534abcb9b600f3acea7624/corrected.s2p) · [Raw sweep](evidence/b53d8fe647534abcb9b600f3acea7624/raw.s2p)

### Step 02 — 2026-09-08T10:43:29.4226367-04:00

ID: `7c3b58fcd7474b28848b9a7e30eac5ad`; state: **complete**.

> C2 trial: mark or note the exact starting screw position. Turn only the first capacitor after TP1 (C2) approximately 1/16 turn clockwise, viewed from the adjustment side. Do not force an end stop. Leave all other capacitors, ferrites, cables and sweep settings unchanged. Remove hand/tool, then click Done - measure. If the adjustment cannot be made, click Skip.

Target error: **1.796700**; mean reflected power: **59.364%**; mean transmitted power: **23.691%**; phase change: **-224.409°**; delay: **62.965 ns**.

[Recorded result](evidence/7c3b58fcd7474b28848b9a7e30eac5ad/result.json) · [Corrected sweep](evidence/7c3b58fcd7474b28848b9a7e30eac5ad/corrected.s2p) · [Raw sweep](evidence/7c3b58fcd7474b28848b9a7e30eac5ad/raw.s2p)

### Step 03 — 2026-09-08T10:44:40.1476931-04:00

ID: `900b350974e2404a935187de468f34d4`; state: **complete**.

> C2 return check: return the first capacitor after TP1 to its exact marked starting position (undo only the previous approximately 1/16 clockwise turn). Leave all other components and cables unchanged. Remove hand/tool and click Done - measure. This checks whether the baseline is repeatable.

Target error: **1.792544**; mean reflected power: **58.428%**; mean transmitted power: **24.165%**; phase change: **-228.803°**; delay: **64.198 ns**.

[Recorded result](evidence/900b350974e2404a935187de468f34d4/result.json) · [Corrected sweep](evidence/900b350974e2404a935187de468f34d4/corrected.s2p) · [Raw sweep](evidence/900b350974e2404a935187de468f34d4/raw.s2p)

### Step 04 — 2026-09-08T10:48:40.4236141-04:00

ID: `ad693bc37ee0493ca846602d035e4514`; state: **complete**.

> C2 counterclockwise trial: from the marked original baseline position, turn only C2 (first capacitor after TP1) approximately 1/16 turn counterclockwise, viewed from the adjustment side. Do not force an end stop; Skip if unavailable. Keep all other components, ferrites, cables and sweep settings unchanged. Remove hand/tool, then click Done - measure.

Target error: **1.789031**; mean reflected power: **60.794%**; mean transmitted power: **23.063%**; phase change: **-219.854°**; delay: **61.687 ns**.

[Recorded result](evidence/ad693bc37ee0493ca846602d035e4514/result.json) · [Corrected sweep](evidence/ad693bc37ee0493ca846602d035e4514/corrected.s2p) · [Raw sweep](evidence/ad693bc37ee0493ca846602d035e4514/raw.s2p)

### Step 05 — 2026-09-08T10:53:36.4136304-04:00

ID: `7f6c08d56ff948a3894127a159a31bf5`; state: **complete**.

> Measure adjustment ALREADY MADE: user returned C2 to its marked original position and slightly withdrew L1 ferrite (exact distance unconfirmed). Do NOT adjust further. Leave all other components, cables and sweep settings unchanged. Remove hand/tool, then click Done - measure.

Target error: **1.911011**; mean reflected power: **57.968%**; mean transmitted power: **24.261%**; phase change: **-225.313°**; delay: **63.219 ns**.

[Recorded result](evidence/7f6c08d56ff948a3894127a159a31bf5/result.json) · [Corrected sweep](evidence/7f6c08d56ff948a3894127a159a31bf5/corrected.s2p) · [Raw sweep](evidence/7f6c08d56ff948a3894127a159a31bf5/raw.s2p)

### Step 06 — 2026-09-08T10:56:13.1408607-04:00

ID: `b0c647fbb3194a049b3569011e485c2b`; state: **complete**.

> L1 return verification: user has already returned L1 ferrite to its original position; C2 remains at its original baseline. Do NOT adjust anything further. Keep cables and sweep settings unchanged. Remove hand/tool, then click Done - measure.

Target error: **1.777168**; mean reflected power: **58.213%**; mean transmitted power: **24.155%**; phase change: **-229.726°**; delay: **64.457 ns**.

[Recorded result](evidence/b0c647fbb3194a049b3569011e485c2b/result.json) · [Corrected sweep](evidence/b0c647fbb3194a049b3569011e485c2b/corrected.s2p) · [Raw sweep](evidence/b0c647fbb3194a049b3569011e485c2b/raw.s2p)

### Step 07 — 2026-09-08T10:57:49.4500445-04:00

ID: `a52e9d89c97241de98b06faed75cdad3`; state: **complete**.

> Measure adjustment ALREADY MADE: user moved L1 ferrite farther inside the coil from the restored baseline (requested approximately 0.5 mm; actual inductance not measured). C2 remains at baseline. Do NOT adjust further. Keep all other components, cables and sweep settings unchanged. Remove hand/tool, then click Done - measure.

Target error: **1.600738**; mean reflected power: **59.819%**; mean transmitted power: **23.787%**; phase change: **-234.745°**; delay: **65.866 ns**.

[Recorded result](evidence/a52e9d89c97241de98b06faed75cdad3/result.json) · [Corrected sweep](evidence/a52e9d89c97241de98b06faed75cdad3/corrected.s2p) · [Raw sweep](evidence/a52e9d89c97241de98b06faed75cdad3/raw.s2p)

### Step 08 — 2026-09-08T11:04:40.8397349-04:00

ID: `ec9f3feef4344eabaf50bb9775c32a0f`; state: **complete**.

> Repeatability check of L1 inward position: do NOT move any capacitor, ferrite, cable, or board. Keep sweep settings unchanged. Remove hand/tool and click Done - measure. This repeats the last measurement before deciding on any further adjustment.

Target error: **1.597115**; mean reflected power: **59.813%**; mean transmitted power: **23.805%**; phase change: **-234.604°**; delay: **65.826 ns**.

[Recorded result](evidence/ec9f3feef4344eabaf50bb9775c32a0f/result.json) · [Corrected sweep](evidence/ec9f3feef4344eabaf50bb9775c32a0f/corrected.s2p) · [Raw sweep](evidence/ec9f3feef4344eabaf50bb9775c32a0f/raw.s2p)

### Step 09 — 2026-09-08T11:06:30.494881-04:00

ID: `131c2b80233848e6b2211b531481cd95`; state: **complete**.

> Measure adjustment ALREADY MADE: user moved L1 ferrite another small step inward from the previous marked position (requested approximately 0.25 mm). Do NOT adjust further. Leave all other components, cables and sweep settings unchanged. Remove hand/tool, then click Done - measure.

Target error: **1.568062**; mean reflected power: **60.148%**; mean transmitted power: **23.726%**; phase change: **-235.992°**; delay: **66.216 ns**.

[Recorded result](evidence/131c2b80233848e6b2211b531481cd95/result.json) · [Corrected sweep](evidence/131c2b80233848e6b2211b531481cd95/corrected.s2p) · [Raw sweep](evidence/131c2b80233848e6b2211b531481cd95/raw.s2p)

### Step 10 — 2026-09-08T11:09:26.1369122-04:00

ID: `a344c828fb8c44bcbc40199e7d18e089`; state: **complete**.

> Second physical coil from TP1 trial (not schematic label L2): mark its current ferrite position, then move that ferrite approximately 0.25 mm inward toward the coil center; do not pass the center or force it. Keep the first physical coil centered, all capacitors and remaining coils unchanged. Keep cables and sweep settings unchanged. Remove hand/tool and click Done - measure. Skip if the coil cannot be identified or adjusted.

Target error: **1.573792**; mean reflected power: **59.156%**; mean transmitted power: **24.136%**; phase change: **-240.748°**; delay: **67.550 ns**.

[Recorded result](evidence/a344c828fb8c44bcbc40199e7d18e089/result.json) · [Corrected sweep](evidence/a344c828fb8c44bcbc40199e7d18e089/corrected.s2p) · [Raw sweep](evidence/a344c828fb8c44bcbc40199e7d18e089/raw.s2p)

### Step 11 — 2026-09-08T11:10:58.2388356-04:00

ID: `66fb6f2bcd4c49e2afed40cca9936f93`; state: **complete**.

> Second physical coil return verification: user has ALREADY returned its ferrite to the marked starting position. Keep the first physical coil centered and all capacitors unchanged. Do NOT adjust further. Keep cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **1.569431**; mean reflected power: **59.957%**; mean transmitted power: **23.806%**; phase change: **-236.893°**; delay: **66.468 ns**.

[Recorded result](evidence/66fb6f2bcd4c49e2afed40cca9936f93/result.json) · [Corrected sweep](evidence/66fb6f2bcd4c49e2afed40cca9936f93/corrected.s2p) · [Raw sweep](evidence/66fb6f2bcd4c49e2afed40cca9936f93/raw.s2p)

### Step 12 — 2026-09-08T11:12:02.9060689-04:00

ID: `7984638161c6450186fdc5094cc2b41f`; state: **complete**.

> Measure adjustment ALREADY MADE: user withdrew the SECOND PHYSICAL COIL from TP1 ferrite outward from its marked baseline (requested approximately 0.25 mm). Keep the first coil centered and all capacitors unchanged. Do NOT adjust further. Keep cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **1.563435**; mean reflected power: **61.084%**; mean transmitted power: **23.263%**; phase change: **-231.100°**; delay: **64.843 ns**.

[Recorded result](evidence/7984638161c6450186fdc5094cc2b41f/result.json) · [Corrected sweep](evidence/7984638161c6450186fdc5094cc2b41f/corrected.s2p) · [Raw sweep](evidence/7984638161c6450186fdc5094cc2b41f/raw.s2p)

### Step 13 — 2026-09-08T11:15:00.2128905-04:00

ID: `7ecac00832e447e6bbed2aa7dc350b58`; state: **complete**.

> Second physical capacitor from TP1 trial (not the first capacitor labelled C2): mark its exact starting position. Turn ONLY this second capacitor approximately 1/16 turn clockwise viewed from its adjustment side. Do not force an end stop; Skip if unavailable. Keep first coil centered, second coil at restored baseline, all other components and cables unchanged. Remove hand/tool and click Done - measure.

Target error: **1.368832**; mean reflected power: **60.798%**; mean transmitted power: **23.460%**; phase change: **-235.898°**; delay: **66.189 ns**.

[Recorded result](evidence/7ecac00832e447e6bbed2aa7dc350b58/result.json) · [Corrected sweep](evidence/7ecac00832e447e6bbed2aa7dc350b58/corrected.s2p) · [Raw sweep](evidence/7ecac00832e447e6bbed2aa7dc350b58/raw.s2p)

### Step 14 — 2026-09-08T11:16:53.9638283-04:00

ID: `00fe4bb394614eaf9295020f20fa28ca`; state: **complete**.

> Repeat current measurement WITHOUT any adjustment. Keep second physical capacitor at its latest marked clockwise position, first coil centered, and all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **1.365506**; mean reflected power: **60.762%**; mean transmitted power: **23.450%**; phase change: **-236.061°**; delay: **66.235 ns**.

[Recorded result](evidence/00fe4bb394614eaf9295020f20fa28ca/result.json) · [Corrected sweep](evidence/00fe4bb394614eaf9295020f20fa28ca/corrected.s2p) · [Raw sweep](evidence/00fe4bb394614eaf9295020f20fa28ca/raw.s2p)

### Step 15 — 2026-09-08T11:18:20.6227211-04:00

ID: `757c02f41b6d4be9bb24a3dee4abe999`; state: **complete**.

> Second physical capacitor from TP1: preserve the current mark so you can return to this verified position. Turn ONLY this capacitor another approximately 1/32 turn clockwise viewed from the adjustment side. Do not force an end stop; Skip if unavailable. Leave all coils, other capacitors, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **1.246299**; mean reflected power: **61.526%**; mean transmitted power: **23.350%**; phase change: **-236.136°**; delay: **66.256 ns**.

[Recorded result](evidence/757c02f41b6d4be9bb24a3dee4abe999/result.json) · [Corrected sweep](evidence/757c02f41b6d4be9bb24a3dee4abe999/corrected.s2p) · [Raw sweep](evidence/757c02f41b6d4be9bb24a3dee4abe999/raw.s2p)

### Step 16 — 2026-09-08T11:19:43.1320827-04:00

ID: `e96ab7b44e394f91971ebd1e72978814`; state: **complete**.

> Second physical capacitor from TP1: preserve the current marked position. Turn ONLY this capacitor another approximately 1/32 turn clockwise viewed from the adjustment side. Do not force an end stop; Skip if unavailable. Leave all coils, other capacitors, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **1.162574**; mean reflected power: **61.943%**; mean transmitted power: **23.180%**; phase change: **-236.242°**; delay: **66.286 ns**.

[Recorded result](evidence/e96ab7b44e394f91971ebd1e72978814/result.json) · [Corrected sweep](evidence/e96ab7b44e394f91971ebd1e72978814/corrected.s2p) · [Raw sweep](evidence/e96ab7b44e394f91971ebd1e72978814/raw.s2p)

### Step 17 — 2026-09-08T11:21:15.2837585-04:00

ID: `a0d9dc0c9aa741669d24657cc650edae`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the SECOND PHYSICAL CAPACITOR from TP1 another approximately 1/64 turn clockwise from the last marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **1.147891**; mean reflected power: **62.000%**; mean transmitted power: **23.143%**; phase change: **-236.246°**; delay: **66.287 ns**.

[Recorded result](evidence/a0d9dc0c9aa741669d24657cc650edae/result.json) · [Corrected sweep](evidence/a0d9dc0c9aa741669d24657cc650edae/corrected.s2p) · [Raw sweep](evidence/a0d9dc0c9aa741669d24657cc650edae/raw.s2p)

### Step 18 — 2026-09-08T11:28:04.9679547-04:00

ID: `b9df4d2c133e4240b3df5d52c572f875`; state: **complete**.

> THIRD PHYSICAL CAPACITOR counted from TP1: mark its exact starting position, then turn ONLY this capacitor approximately 1/32 turn clockwise viewed from the adjustment side. Preserve the current second-capacitor setting; leave all other capacitors and coils unchanged. Do not force an end stop; Skip if unavailable. Keep board, cables and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **1.072020**; mean reflected power: **61.186%**; mean transmitted power: **23.874%**; phase change: **-240.699°**; delay: **67.536 ns**.

[Recorded result](evidence/b9df4d2c133e4240b3df5d52c572f875/result.json) · [Corrected sweep](evidence/b9df4d2c133e4240b3df5d52c572f875/corrected.s2p) · [Raw sweep](evidence/b9df4d2c133e4240b3df5d52c572f875/raw.s2p)

### Step 19 — 2026-09-08T11:29:12.6117017-04:00

ID: `4f045888c8e14f8cb7f3fe1dd5923f57`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the THIRD PHYSICAL CAPACITOR counted from TP1 another approximately 1/32 turn clockwise. Do NOT adjust further. Preserve all other capacitor and coil positions, cables, board and sweep settings. Remove hand/tool and click Done - measure.

Target error: **1.014045**; mean reflected power: **60.327%**; mean transmitted power: **24.342%**; phase change: **-245.148°**; delay: **68.785 ns**.

[Recorded result](evidence/4f045888c8e14f8cb7f3fe1dd5923f57/result.json) · [Corrected sweep](evidence/4f045888c8e14f8cb7f3fe1dd5923f57/corrected.s2p) · [Raw sweep](evidence/4f045888c8e14f8cb7f3fe1dd5923f57/raw.s2p)

### Step 20 — 2026-09-08T11:30:13.925525-04:00

ID: `917e7bf5adeb41aba171ce12aed8cef7`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the THIRD PHYSICAL CAPACITOR counted from TP1 another approximately 1/32 turn clockwise. Do NOT adjust further. Leave all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.976100**; mean reflected power: **59.856%**; mean transmitted power: **24.650%**; phase change: **-248.979°**; delay: **69.859 ns**.

[Recorded result](evidence/917e7bf5adeb41aba171ce12aed8cef7/result.json) · [Corrected sweep](evidence/917e7bf5adeb41aba171ce12aed8cef7/corrected.s2p) · [Raw sweep](evidence/917e7bf5adeb41aba171ce12aed8cef7/raw.s2p)

### Step 21 — 2026-09-08T11:31:24.4191288-04:00

ID: `c36659889668428b8a7128a898ac20db`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the THIRD PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn clockwise. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.960899**; mean reflected power: **59.603%**; mean transmitted power: **24.720%**; phase change: **-250.557°**; delay: **70.302 ns**.

[Recorded result](evidence/c36659889668428b8a7128a898ac20db/result.json) · [Corrected sweep](evidence/c36659889668428b8a7128a898ac20db/corrected.s2p) · [Raw sweep](evidence/c36659889668428b8a7128a898ac20db/raw.s2p)

### Step 22 — 2026-09-08T11:32:26.7548461-04:00

ID: `389266392ccb4a1c886357e25a71ac08`; state: **complete**.

> Repeatability check: do NOT adjust any capacitor or coil. Keep the third physical capacitor at its latest marked position and preserve all other component, cable, board and sweep settings. Remove hand/tool and click Done - measure.

Target error: **0.960056**; mean reflected power: **59.597%**; mean transmitted power: **24.727%**; phase change: **-250.628°**; delay: **70.322 ns**.

[Recorded result](evidence/389266392ccb4a1c886357e25a71ac08/result.json) · [Corrected sweep](evidence/389266392ccb4a1c886357e25a71ac08/corrected.s2p) · [Raw sweep](evidence/389266392ccb4a1c886357e25a71ac08/raw.s2p)

### Step 23 — 2026-09-08T11:33:43.3068134-04:00

ID: `e33077dd1ce2459ca168e173436a9ba3`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 approximately 1/32 turn clockwise from its marked starting position. Do NOT adjust further. Leave all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.960151**; mean reflected power: **60.749%**; mean transmitted power: **24.177%**; phase change: **-244.992°**; delay: **68.741 ns**.

[Recorded result](evidence/e33077dd1ce2459ca168e173436a9ba3/result.json) · [Corrected sweep](evidence/e33077dd1ce2459ca168e173436a9ba3/corrected.s2p) · [Raw sweep](evidence/e33077dd1ce2459ca168e173436a9ba3/raw.s2p)

### Step 24 — 2026-09-08T11:35:20.5692351-04:00

ID: `b896819f4ba84fffb933f4263144ed48`; state: **complete**.

> Fourth physical capacitor return verification: user has ALREADY returned the FOURTH PHYSICAL CAPACITOR counted from TP1 to its marked starting position. Do NOT adjust further. Preserve all other capacitor and coil settings, cables, board and sweep settings. Remove hand/tool and click Done - measure.

Target error: **0.959357**; mean reflected power: **59.787%**; mean transmitted power: **24.626%**; phase change: **-249.562°**; delay: **70.023 ns**.

[Recorded result](evidence/b896819f4ba84fffb933f4263144ed48/result.json) · [Corrected sweep](evidence/b896819f4ba84fffb933f4263144ed48/corrected.s2p) · [Raw sweep](evidence/b896819f4ba84fffb933f4263144ed48/raw.s2p)

### Step 25 — 2026-09-08T11:36:31.664124-04:00

ID: `ead8813426fb4e948a567c31a07d467f`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 approximately 1/32 turn counterclockwise from its restored marked starting position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.945880**; mean reflected power: **55.561%**; mean transmitted power: **26.565%**; phase change: **-272.667°**; delay: **76.506 ns**.

[Recorded result](evidence/ead8813426fb4e948a567c31a07d467f/result.json) · [Corrected sweep](evidence/ead8813426fb4e948a567c31a07d467f/corrected.s2p) · [Raw sweep](evidence/ead8813426fb4e948a567c31a07d467f/raw.s2p)

### Step 26 — 2026-09-08T11:37:44.7430365-04:00

ID: `65a2cb80cca94d9d82f668df0bf25e0e`; state: **complete**.

> Repeat WITHOUT adjustment: keep fourth physical capacitor at its latest marked counterclockwise position. Do NOT move any capacitor, ferrite, cable or board; preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.944267**; mean reflected power: **55.521%**; mean transmitted power: **26.555%**; phase change: **-272.862°**; delay: **76.561 ns**.

[Recorded result](evidence/65a2cb80cca94d9d82f668df0bf25e0e/result.json) · [Corrected sweep](evidence/65a2cb80cca94d9d82f668df0bf25e0e/corrected.s2p) · [Raw sweep](evidence/65a2cb80cca94d9d82f668df0bf25e0e/raw.s2p)

### Step 27 — 2026-09-08T11:39:19.8582879-04:00

ID: `b4f252117bdc41eabbb4b0425a99a041`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Leave all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.928924**; mean reflected power: **54.006%**; mean transmitted power: **27.233%**; phase change: **-282.285°**; delay: **79.205 ns**.

[Recorded result](evidence/b4f252117bdc41eabbb4b0425a99a041/result.json) · [Corrected sweep](evidence/b4f252117bdc41eabbb4b0425a99a041/corrected.s2p) · [Raw sweep](evidence/b4f252117bdc41eabbb4b0425a99a041/raw.s2p)

### Step 28 — 2026-09-08T11:40:42.4961167-04:00

ID: `d6ae0fea19c14099b96d8176b10828f1`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Preserve all other component positions, cables, board and sweep settings. Remove hand/tool and click Done - measure.

Target error: **0.903403**; mean reflected power: **52.452%**; mean transmitted power: **27.916%**; phase change: **-292.726°**; delay: **82.134 ns**.

[Recorded result](evidence/d6ae0fea19c14099b96d8176b10828f1/result.json) · [Corrected sweep](evidence/d6ae0fea19c14099b96d8176b10828f1/corrected.s2p) · [Raw sweep](evidence/d6ae0fea19c14099b96d8176b10828f1/raw.s2p)

### Step 29 — 2026-09-08T11:42:01.1231311-04:00

ID: `0a9892d2cfdd43f6a2359116aa31141e`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Preserve all other component positions, cables, board and sweep settings. Remove hand/tool and click Done - measure.

Target error: **0.859236**; mean reflected power: **50.950%**; mean transmitted power: **28.560%**; phase change: **-304.080°**; delay: **85.320 ns**.

[Recorded result](evidence/0a9892d2cfdd43f6a2359116aa31141e/result.json) · [Corrected sweep](evidence/0a9892d2cfdd43f6a2359116aa31141e/corrected.s2p) · [Raw sweep](evidence/0a9892d2cfdd43f6a2359116aa31141e/raw.s2p)

### Step 30 — 2026-09-08T11:43:18.7541941-04:00

ID: `0af584b70fa14abd996759fac2b8ee5f`; state: **complete**.

> Repeat WITHOUT adjustment: keep fourth physical capacitor at its latest marked position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.858019**; mean reflected power: **50.903%**; mean transmitted power: **28.582%**; phase change: **-304.403°**; delay: **85.411 ns**.

[Recorded result](evidence/0af584b70fa14abd996759fac2b8ee5f/result.json) · [Corrected sweep](evidence/0af584b70fa14abd996759fac2b8ee5f/corrected.s2p) · [Raw sweep](evidence/0af584b70fa14abd996759fac2b8ee5f/raw.s2p)

### Step 31 — 2026-09-08T11:51:29.443368-04:00

ID: `6bd34ea4456446af876710a8c5675185`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.821767**; mean reflected power: **50.163%**; mean transmitted power: **28.875%**; phase change: **-310.603°**; delay: **87.150 ns**.

[Recorded result](evidence/6bd34ea4456446af876710a8c5675185/result.json) · [Corrected sweep](evidence/6bd34ea4456446af876710a8c5675185/corrected.s2p) · [Raw sweep](evidence/6bd34ea4456446af876710a8c5675185/raw.s2p)

### Step 32 — 2026-09-08T11:52:41.9240083-04:00

ID: `bc8225f381b14792ab72f0b30b767ead`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.748231**; mean reflected power: **49.203%**; mean transmitted power: **29.217%**; phase change: **-319.561°**; delay: **89.664 ns**.

[Recorded result](evidence/bc8225f381b14792ab72f0b30b767ead/result.json) · [Corrected sweep](evidence/bc8225f381b14792ab72f0b30b767ead/corrected.s2p) · [Raw sweep](evidence/bc8225f381b14792ab72f0b30b767ead/raw.s2p)

### Step 33 — 2026-09-08T11:53:50.454149-04:00

ID: `dda423025c9644c1ae6b767fe63321f6`; state: **complete**.

> Repeat WITHOUT adjustment: keep fourth physical capacitor at its latest marked position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.747196**; mean reflected power: **49.201%**; mean transmitted power: **29.213%**; phase change: **-320.685°**; delay: **89.979 ns**.

[Recorded result](evidence/dda423025c9644c1ae6b767fe63321f6/result.json) · [Corrected sweep](evidence/dda423025c9644c1ae6b767fe63321f6/corrected.s2p) · [Raw sweep](evidence/dda423025c9644c1ae6b767fe63321f6/raw.s2p)

### Step 34 — 2026-09-08T11:55:15.028508-04:00

ID: `6c2e1df993e54642a55eb939fd19e1f7`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.654707**; mean reflected power: **48.631%**; mean transmitted power: **29.283%**; phase change: **-326.548°**; delay: **91.624 ns**.

[Recorded result](evidence/6c2e1df993e54642a55eb939fd19e1f7/result.json) · [Corrected sweep](evidence/6c2e1df993e54642a55eb939fd19e1f7/corrected.s2p) · [Raw sweep](evidence/6c2e1df993e54642a55eb939fd19e1f7/raw.s2p)

### Step 35 — 2026-09-08T11:56:38.3686246-04:00

ID: `86b7d4abb6eb46e1b34aa5bd4632ded7`; state: **complete**.

> FOURTH PHYSICAL CAPACITOR counted from TP1: preserve current position mark, then turn ONLY this capacitor another approximately 1/64 turn counterclockwise viewed from adjustment side. Do not force an end stop; Skip if unavailable. Leave all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.543874**; mean reflected power: **48.465%**; mean transmitted power: **29.055%**; phase change: **-331.933°**; delay: **93.135 ns**.

[Recorded result](evidence/86b7d4abb6eb46e1b34aa5bd4632ded7/result.json) · [Corrected sweep](evidence/86b7d4abb6eb46e1b34aa5bd4632ded7/corrected.s2p) · [Raw sweep](evidence/86b7d4abb6eb46e1b34aa5bd4632ded7/raw.s2p)

### Step 36 — 2026-09-08T11:57:47.5412783-04:00

ID: `af849569702648c19b38683d988f6dda`; state: **complete**.

> Repeat WITHOUT adjustment: keep fourth physical capacitor at its latest marked position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.541438**; mean reflected power: **48.445%**; mean transmitted power: **29.038%**; phase change: **-332.143°**; delay: **93.194 ns**.

[Recorded result](evidence/af849569702648c19b38683d988f6dda/result.json) · [Corrected sweep](evidence/af849569702648c19b38683d988f6dda/corrected.s2p) · [Raw sweep](evidence/af849569702648c19b38683d988f6dda/raw.s2p)

### Step 37 — 2026-09-08T11:59:01.2031505-04:00

ID: `44b67bbc9eda4037a12b61c139a6fbad`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.509220**; mean reflected power: **48.492%**; mean transmitted power: **28.924%**; phase change: **-333.371°**; delay: **93.538 ns**.

[Recorded result](evidence/44b67bbc9eda4037a12b61c139a6fbad/result.json) · [Corrected sweep](evidence/44b67bbc9eda4037a12b61c139a6fbad/corrected.s2p) · [Raw sweep](evidence/44b67bbc9eda4037a12b61c139a6fbad/raw.s2p)

### Step 38 — 2026-09-08T12:00:20.2310661-04:00

ID: `4609723a154e47b49523d402e0c9c67a`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FOURTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.537447**; mean reflected power: **48.423%**; mean transmitted power: **29.008%**; phase change: **-332.266°**; delay: **93.228 ns**.

[Recorded result](evidence/4609723a154e47b49523d402e0c9c67a/result.json) · [Corrected sweep](evidence/4609723a154e47b49523d402e0c9c67a/corrected.s2p) · [Raw sweep](evidence/4609723a154e47b49523d402e0c9c67a/raw.s2p)

### Step 39 — 2026-09-08T12:01:30.9667175-04:00

ID: `06e5bea675274ba884f266d3de70b641`; state: **complete**.

> Fourth capacitor return verification: user has ALREADY returned the FOURTH PHYSICAL CAPACITOR from TP1 to its previous mark, undoing only the last approximately 1/64-turn counterclockwise adjustment. Do NOT adjust further. Preserve all other components, cables, board and sweep settings. Remove hand/tool and click Done - measure.

Target error: **0.503683**; mean reflected power: **48.490%**; mean transmitted power: **28.880%**; phase change: **-333.447°**; delay: **93.560 ns**.

[Recorded result](evidence/06e5bea675274ba884f266d3de70b641/result.json) · [Corrected sweep](evidence/06e5bea675274ba884f266d3de70b641/corrected.s2p) · [Raw sweep](evidence/06e5bea675274ba884f266d3de70b641/raw.s2p)

### Step 40 — 2026-09-08T12:02:41.2426141-04:00

ID: `32b1373bc03041d8a108fddfc39ff990`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FIFTH PHYSICAL CAPACITOR counted from TP1 approximately 1/32 turn clockwise from its marked starting position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.582920**; mean reflected power: **49.151%**; mean transmitted power: **28.042%**; phase change: **-330.434°**; delay: **92.714 ns**.

[Recorded result](evidence/32b1373bc03041d8a108fddfc39ff990/result.json) · [Corrected sweep](evidence/32b1373bc03041d8a108fddfc39ff990/corrected.s2p) · [Raw sweep](evidence/32b1373bc03041d8a108fddfc39ff990/raw.s2p)

### Step 41 — 2026-09-08T12:03:52.913833-04:00

ID: `e50cc58773754ccdbc0558c4634cacda`; state: **complete**.

> Fifth capacitor return verification: user has ALREADY returned the FIFTH PHYSICAL CAPACITOR counted from TP1 to its marked starting position, undoing the last approximately 1/32-turn clockwise adjustment. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.467095**; mean reflected power: **48.040%**; mean transmitted power: **29.130%**; phase change: **-334.704°**; delay: **93.912 ns**.

[Recorded result](evidence/e50cc58773754ccdbc0558c4634cacda/result.json) · [Corrected sweep](evidence/e50cc58773754ccdbc0558c4634cacda/corrected.s2p) · [Raw sweep](evidence/e50cc58773754ccdbc0558c4634cacda/raw.s2p)

### Step 42 — 2026-09-08T12:05:01.7554962-04:00

ID: `3b1e4f4ab7f044a39184731fdd476555`; state: **complete**.

> Repeat WITHOUT adjustment: keep fifth physical capacitor at its latest marked returned position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.465774**; mean reflected power: **48.062%**; mean transmitted power: **29.148%**; phase change: **-334.876°**; delay: **93.961 ns**.

[Recorded result](evidence/3b1e4f4ab7f044a39184731fdd476555/result.json) · [Corrected sweep](evidence/3b1e4f4ab7f044a39184731fdd476555/corrected.s2p) · [Raw sweep](evidence/3b1e4f4ab7f044a39184731fdd476555/raw.s2p)

### Step 43 — 2026-09-08T12:06:07.0493491-04:00

ID: `85e4b49e93c74ca7b7968079414846db`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FIFTH PHYSICAL CAPACITOR counted from TP1 approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.427982**; mean reflected power: **47.917%**; mean transmitted power: **29.593%**; phase change: **-336.200°**; delay: **94.332 ns**.

[Recorded result](evidence/85e4b49e93c74ca7b7968079414846db/result.json) · [Corrected sweep](evidence/85e4b49e93c74ca7b7968079414846db/corrected.s2p) · [Raw sweep](evidence/85e4b49e93c74ca7b7968079414846db/raw.s2p)

### Step 44 — 2026-09-08T12:07:22.0150035-04:00

ID: `b4c6e829bec245be98b014a9f42b81ed`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FIFTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.378519**; mean reflected power: **47.559%**; mean transmitted power: **30.066%**; phase change: **-337.617°**; delay: **94.730 ns**.

[Recorded result](evidence/b4c6e829bec245be98b014a9f42b81ed/result.json) · [Corrected sweep](evidence/b4c6e829bec245be98b014a9f42b81ed/corrected.s2p) · [Raw sweep](evidence/b4c6e829bec245be98b014a9f42b81ed/raw.s2p)

### Step 45 — 2026-09-08T12:08:26.7214014-04:00

ID: `bb07eb6b45d64d1c8c166a21fe3826a0`; state: **complete**.

> Repeat WITHOUT adjustment: keep fifth physical capacitor at its latest marked position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.377476**; mean reflected power: **47.544%**; mean transmitted power: **30.079%**; phase change: **-337.592°**; delay: **94.723 ns**.

[Recorded result](evidence/bb07eb6b45d64d1c8c166a21fe3826a0/result.json) · [Corrected sweep](evidence/bb07eb6b45d64d1c8c166a21fe3826a0/corrected.s2p) · [Raw sweep](evidence/bb07eb6b45d64d1c8c166a21fe3826a0/raw.s2p)

### Step 46 — 2026-09-08T12:09:45.7023955-04:00

ID: `a92e56518a334140a99454cafe2cb3a9`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FIFTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.299431**; mean reflected power: **46.887%**; mean transmitted power: **30.801%**; phase change: **-340.603°**; delay: **95.568 ns**.

[Recorded result](evidence/a92e56518a334140a99454cafe2cb3a9/result.json) · [Corrected sweep](evidence/a92e56518a334140a99454cafe2cb3a9/corrected.s2p) · [Raw sweep](evidence/a92e56518a334140a99454cafe2cb3a9/raw.s2p)

### Step 47 — 2026-09-08T12:10:56.7952314-04:00

ID: `c1762c6d67f34595a82a4948b438d8a4`; state: **complete**.

> Repeat WITHOUT adjustment: keep fifth physical capacitor at its latest marked position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.298464**; mean reflected power: **46.843%**; mean transmitted power: **30.791%**; phase change: **-340.611°**; delay: **95.570 ns**.

[Recorded result](evidence/c1762c6d67f34595a82a4948b438d8a4/result.json) · [Corrected sweep](evidence/c1762c6d67f34595a82a4948b438d8a4/corrected.s2p) · [Raw sweep](evidence/c1762c6d67f34595a82a4948b438d8a4/raw.s2p)

### Step 48 — 2026-09-08T12:12:23.8141584-04:00

ID: `802148c4325343c1b2fcd561aaabf445`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FIFTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.262928**; mean reflected power: **46.955%**; mean transmitted power: **31.387%**; phase change: **-342.637°**; delay: **96.138 ns**.

[Recorded result](evidence/802148c4325343c1b2fcd561aaabf445/result.json) · [Corrected sweep](evidence/802148c4325343c1b2fcd561aaabf445/corrected.s2p) · [Raw sweep](evidence/802148c4325343c1b2fcd561aaabf445/raw.s2p)

### Step 49 — 2026-09-08T12:13:42.2241227-04:00

ID: `ac97869fe40f414a842b367648b3ce94`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the FIFTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.273589**; mean reflected power: **46.923%**; mean transmitted power: **31.206%**; phase change: **-341.979°**; delay: **95.954 ns**.

[Recorded result](evidence/ac97869fe40f414a842b367648b3ce94/result.json) · [Corrected sweep](evidence/ac97869fe40f414a842b367648b3ce94/corrected.s2p) · [Raw sweep](evidence/ac97869fe40f414a842b367648b3ce94/raw.s2p)

### Step 50 — 2026-09-08T12:15:02.5919435-04:00

ID: `1ef3107d357b4e948159b44164e88967`; state: **complete**.

> Fifth capacitor return verification: user has ALREADY returned the FIFTH PHYSICAL CAPACITOR counted from TP1 to its previous mark, undoing only the last approximately 1/64-turn counterclockwise adjustment. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.258475**; mean reflected power: **46.780%**; mean transmitted power: **31.394%**; phase change: **-342.978°**; delay: **96.234 ns**.

[Recorded result](evidence/1ef3107d357b4e948159b44164e88967/result.json) · [Corrected sweep](evidence/1ef3107d357b4e948159b44164e88967/corrected.s2p) · [Raw sweep](evidence/1ef3107d357b4e948159b44164e88967/raw.s2p)

### Step 51 — 2026-09-08T12:16:18.0063191-04:00

ID: `abbea7a30a75456abbbe423e12be1008`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the SIXTH PHYSICAL CAPACITOR counted from TP1 approximately 1/64 turn clockwise from its marked starting position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.274801**; mean reflected power: **46.431%**; mean transmitted power: **31.711%**; phase change: **-342.977°**; delay: **96.234 ns**.

[Recorded result](evidence/abbea7a30a75456abbbe423e12be1008/result.json) · [Corrected sweep](evidence/abbea7a30a75456abbbe423e12be1008/corrected.s2p) · [Raw sweep](evidence/abbea7a30a75456abbbe423e12be1008/raw.s2p)

### Step 52 — 2026-09-08T12:17:39.9433576-04:00

ID: `3ca714b2a6a14220817ca732e6e354b0`; state: **complete**.

> Sixth capacitor return verification: user has ALREADY returned the SIXTH PHYSICAL CAPACITOR counted from TP1 to its marked starting position, undoing only the last approximately 1/64-turn clockwise adjustment. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.254423**; mean reflected power: **46.859%**; mean transmitted power: **31.297%**; phase change: **-342.911°**; delay: **96.215 ns**.

[Recorded result](evidence/3ca714b2a6a14220817ca732e6e354b0/result.json) · [Corrected sweep](evidence/3ca714b2a6a14220817ca732e6e354b0/corrected.s2p) · [Raw sweep](evidence/3ca714b2a6a14220817ca732e6e354b0/raw.s2p)

### Step 53 — 2026-09-08T12:19:09.6833145-04:00

ID: `29f0c09b1b4a4f36b5f1b17969fae11d`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the SIXTH PHYSICAL CAPACITOR counted from TP1 approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.240237**; mean reflected power: **47.223%**; mean transmitted power: **30.911%**; phase change: **-342.951°**; delay: **96.226 ns**.

[Recorded result](evidence/29f0c09b1b4a4f36b5f1b17969fae11d/result.json) · [Corrected sweep](evidence/29f0c09b1b4a4f36b5f1b17969fae11d/corrected.s2p) · [Raw sweep](evidence/29f0c09b1b4a4f36b5f1b17969fae11d/raw.s2p)

### Step 54 — 2026-09-08T12:20:13.5492301-04:00

ID: `fceb112e10234dc6b8b158429000434c`; state: **complete**.

> Repeat WITHOUT adjustment: keep sixth physical capacitor at its latest marked counterclockwise position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.239889**; mean reflected power: **47.208%**; mean transmitted power: **30.921%**; phase change: **-343.043°**; delay: **96.252 ns**.

[Recorded result](evidence/fceb112e10234dc6b8b158429000434c/result.json) · [Corrected sweep](evidence/fceb112e10234dc6b8b158429000434c/corrected.s2p) · [Raw sweep](evidence/fceb112e10234dc6b8b158429000434c/raw.s2p)

### Step 55 — 2026-09-08T12:21:21.4193835-04:00

ID: `2e126d1d38884451ae8ea58e1c98f240`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the SIXTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.235663**; mean reflected power: **47.349%**; mean transmitted power: **30.795%**; phase change: **-342.882°**; delay: **96.207 ns**.

[Recorded result](evidence/2e126d1d38884451ae8ea58e1c98f240/result.json) · [Corrected sweep](evidence/2e126d1d38884451ae8ea58e1c98f240/corrected.s2p) · [Raw sweep](evidence/2e126d1d38884451ae8ea58e1c98f240/raw.s2p)

### Step 56 — 2026-09-08T12:22:47.0141347-04:00

ID: `ce5973799d884625b7853ae6c0b78db7`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the SEVENTH PHYSICAL CAPACITOR counted from TP1 approximately 1/64 turn clockwise from its marked starting position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.262783**; mean reflected power: **46.632%**; mean transmitted power: **31.738%**; phase change: **-344.044°**; delay: **96.533 ns**.

[Recorded result](evidence/ce5973799d884625b7853ae6c0b78db7/result.json) · [Corrected sweep](evidence/ce5973799d884625b7853ae6c0b78db7/corrected.s2p) · [Raw sweep](evidence/ce5973799d884625b7853ae6c0b78db7/raw.s2p)

### Step 57 — 2026-09-08T12:24:01.6379445-04:00

ID: `3baa060cff6e433f8886d74b3b35194a`; state: **complete**.

> Seventh capacitor return verification: user has ALREADY returned the SEVENTH PHYSICAL CAPACITOR counted from TP1 to its marked starting position, undoing only the last approximately 1/64-turn clockwise adjustment. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.236869**; mean reflected power: **47.282%**; mean transmitted power: **30.841%**; phase change: **-342.976°**; delay: **96.233 ns**.

[Recorded result](evidence/3baa060cff6e433f8886d74b3b35194a/result.json) · [Corrected sweep](evidence/3baa060cff6e433f8886d74b3b35194a/corrected.s2p) · [Raw sweep](evidence/3baa060cff6e433f8886d74b3b35194a/raw.s2p)

### Step 58 — 2026-09-08T12:25:06.9768707-04:00

ID: `e38dfc0f4e204132b880daeb6ad86115`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the SEVENTH PHYSICAL CAPACITOR counted from TP1 approximately 1/64 turn counterclockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.286284**; mean reflected power: **46.177%**; mean transmitted power: **32.383%**; phase change: **-343.223°**; delay: **96.303 ns**.

[Recorded result](evidence/e38dfc0f4e204132b880daeb6ad86115/result.json) · [Corrected sweep](evidence/e38dfc0f4e204132b880daeb6ad86115/corrected.s2p) · [Raw sweep](evidence/e38dfc0f4e204132b880daeb6ad86115/raw.s2p)

### Step 59 — 2026-09-08T12:26:14.0620422-04:00

ID: `f64bab30550d4b998496f640adb9fca4`; state: **complete**.

> Seventh capacitor return verification: user has ALREADY returned the SEVENTH PHYSICAL CAPACITOR counted from TP1 to its marked starting position, undoing only the last approximately 1/64-turn counterclockwise adjustment. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.231024**; mean reflected power: **47.446%**; mean transmitted power: **30.614%**; phase change: **-343.064°**; delay: **96.258 ns**.

[Recorded result](evidence/f64bab30550d4b998496f640adb9fca4/result.json) · [Corrected sweep](evidence/f64bab30550d4b998496f640adb9fca4/corrected.s2p) · [Raw sweep](evidence/f64bab30550d4b998496f640adb9fca4/raw.s2p)

### Step 60 — 2026-09-08T12:27:47.7421601-04:00

ID: `d8e06d83d1f849948570c52dd5b98782`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the EIGHTH PHYSICAL CAPACITOR counted from TP1 approximately 1/64 turn clockwise from its marked starting position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.188938**; mean reflected power: **47.159%**; mean transmitted power: **31.150%**; phase change: **-343.521°**; delay: **96.386 ns**.

[Recorded result](evidence/d8e06d83d1f849948570c52dd5b98782/result.json) · [Corrected sweep](evidence/d8e06d83d1f849948570c52dd5b98782/corrected.s2p) · [Raw sweep](evidence/d8e06d83d1f849948570c52dd5b98782/raw.s2p)

### Step 61 — 2026-09-08T12:28:50.7870546-04:00

ID: `62b9226cc2e5429d8742faed387ce053`; state: **complete**.

> Repeat WITHOUT adjustment: keep eighth physical capacitor at its latest marked clockwise position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.187890**; mean reflected power: **47.189%**; mean transmitted power: **31.175%**; phase change: **-343.187°**; delay: **96.293 ns**.

[Recorded result](evidence/62b9226cc2e5429d8742faed387ce053/result.json) · [Corrected sweep](evidence/62b9226cc2e5429d8742faed387ce053/corrected.s2p) · [Raw sweep](evidence/62b9226cc2e5429d8742faed387ce053/raw.s2p)

### Step 62 — 2026-09-08T12:30:20.5146265-04:00

ID: `fe3075e171cb45f2b8f33358c1deca94`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the EIGHTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn clockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.184823**; mean reflected power: **47.179%**; mean transmitted power: **31.207%**; phase change: **-343.131°**; delay: **96.277 ns**.

[Recorded result](evidence/fe3075e171cb45f2b8f33358c1deca94/result.json) · [Corrected sweep](evidence/fe3075e171cb45f2b8f33358c1deca94/corrected.s2p) · [Raw sweep](evidence/fe3075e171cb45f2b8f33358c1deca94/raw.s2p)

### Step 63 — 2026-09-08T12:32:03.0297452-04:00

ID: `38744343a8ec4514ac3b788750688e53`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the NINTH PHYSICAL CAPACITOR counted from TP1 approximately 1/64 turn clockwise from its marked starting position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.183336**; mean reflected power: **47.052%**; mean transmitted power: **31.353%**; phase change: **-343.228°**; delay: **96.304 ns**.

[Recorded result](evidence/38744343a8ec4514ac3b788750688e53/result.json) · [Corrected sweep](evidence/38744343a8ec4514ac3b788750688e53/corrected.s2p) · [Raw sweep](evidence/38744343a8ec4514ac3b788750688e53/raw.s2p)

### Step 64 — 2026-09-08T12:32:59.3960995-04:00

ID: `9fb04136c4764b8db3b14062bf26356c`; state: **complete**.

> Repeat WITHOUT adjustment: keep ninth physical capacitor at its latest marked clockwise position. Do NOT move any capacitor, ferrite, cable or board. Preserve sweep settings. Keep hand/tool clear and click Done - measure.

Target error: **0.183246**; mean reflected power: **47.046%**; mean transmitted power: **31.361%**; phase change: **-343.262°**; delay: **96.314 ns**.

[Recorded result](evidence/9fb04136c4764b8db3b14062bf26356c/result.json) · [Corrected sweep](evidence/9fb04136c4764b8db3b14062bf26356c/corrected.s2p) · [Raw sweep](evidence/9fb04136c4764b8db3b14062bf26356c/raw.s2p)

### Step 65 — 2026-09-08T12:34:23.0770822-04:00

ID: `1adf98d00f7b4210ac9bf42fe1eedbdc`; state: **complete**.

> Measure adjustment ALREADY MADE: user turned the NINTH PHYSICAL CAPACITOR counted from TP1 another approximately 1/64 turn clockwise from its latest marked position. Do NOT adjust further. Keep all other components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.183450**; mean reflected power: **47.055%**; mean transmitted power: **31.345%**; phase change: **-343.149°**; delay: **96.282 ns**.

[Recorded result](evidence/1adf98d00f7b4210ac9bf42fe1eedbdc/result.json) · [Corrected sweep](evidence/1adf98d00f7b4210ac9bf42fe1eedbdc/corrected.s2p) · [Raw sweep](evidence/1adf98d00f7b4210ac9bf42fe1eedbdc/raw.s2p)

### Step 66 — 2026-09-08T12:35:38.8280323-04:00

ID: `e54487beff424d418a1c4080ee5982df`; state: **complete**.

> Ninth capacitor return and overall checkpoint: user has ALREADY returned the NINTH PHYSICAL CAPACITOR from TP1 to the previous mark, undoing only the last approximately 1/64-turn clockwise adjustment. Do NOT adjust further. Keep all components, cables, board and sweep settings unchanged. Remove hand/tool and click Done - measure.

Target error: **0.186095**; mean reflected power: **47.241%**; mean transmitted power: **31.071%**; phase change: **-343.160°**; delay: **96.285 ns**.

[Recorded result](evidence/e54487beff424d418a1c4080ee5982df/result.json) · [Corrected sweep](evidence/e54487beff424d418a1c4080ee5982df/corrected.s2p) · [Raw sweep](evidence/e54487beff424d418a1c4080ee5982df/raw.s2p)
