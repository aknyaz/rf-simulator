# High-power measurement log — 2026-10-18

Project: **Small Antenna / Gradient Artificial Medium with Embedded Radiator**.
Status: first two operator-reported power points; archived NanoVNA data independently recalculated from local files. No new instrument acquisition, physical retuning, or simulator code/model changes were performed for this log.

## Setup and reference planes

```text
FY6900 generator → RF amplifier (15 V supply) → LPF (both switches: 20m)
→ SW-28HF forward/reflected-power meter → 10-section slow-wave board
→ 50-ohm calibration load
```

The SW-28HF measures at the board input, subject to intervening leads/adapters. Accepted power is the net power entering the downstream board/load system at that plane. It includes board dissipation and power reaching the load; it does not measure radiation efficiency or output load power.

Historical board documentation describes 10 series coils and 9 shunt capacitors, TP1 to TP23, approximately 17 cm active length. The exact present component settings, cable lengths, load rating/temperature, meter range, generator amplitude convention (RMS/peak/Vpp), amplifier current with the board, and calibration uncertainty were not supplied for today's points. Historical topology/settings are not independently reverified by today's power readings.

### Control without board

Same stated chain with the board omitted and the 50-ohm load connected downstream of the meter:

| Frequency (MHz) | Generator setting (V) | Forward (W) | Reflected (W) | Display SWR | Amplifier supply | Amplifier current |
|---:|---:|---:|---:|---:|---:|---:|
| 10.000 | 1.0 | 1.3 | Not recorded | approximately 1.0 | 15 V | approximately 0.2 A |

Do not infer a measured zero reflected power or exact accepted power from approximate SWR alone. The control used a different generator setting from the board tests; it is not an insertion-loss or gain comparison. Forward power under mismatch can depend on amplifier/load interaction, so compare forward/reflected readings within each point.

## Power measurement table

All power readings below are supplied by the operator, not imported from instrument files. Voltage entries are generator display settings, not measured board input voltages. Blank rows are reserved for future observations; a blank is not zero.

| Point | Frequency (MHz) | Generator (V) | Forward Pf (W) | Reflected display | Interpreted Pr (W) | Display SWR | Accepted Pf−Pr (W) | Reflected (%) | Accepted (%) | Calculated SWR |
|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|
| 1 | 10.000 | 0.2 | 1.64 | 1.14 W reported | 1.14 | 11 | 0.5000 | 69.5122 | 30.4878 | 11.0293 |
| 2 | 11.800 | 0.2 | 0.71 | `R0055` | 0.0055 (provisional) | 1.19 | 0.7045 | 0.77465 | 99.22535 | 1.19302 |
| 3 | | | | | | | | | | |
| 4 | | | | | | | | | | |
| 5 | | | | | | | | | | |
| 6 | | | | | | | | | | |
| 7 | | | | | | | | | | |
| 8 | | | | | | | | | | |
| 9 | | | | | | | | | | |
| 10 | | | | | | | | | | |

`R0055` is preserved verbatim. Its interpretation as 0.0055 W is provisional until the display units/decimal convention are confirmed. It is internally consistent with the reported SWR 1.19, but that consistency is not independent calibration evidence. The initially supplied approximately 99.25% accepted becomes **99.23%** when calculated from 0.71 W and 0.0055 W. Extra calculation digits do not imply corresponding meter accuracy.

## Calculations

For power readings at the same reference plane:

```text
reflected fraction r = Pr / Pf
accepted power Pacc = Pf − Pr
accepted fraction = 1 − r
|Gamma| = sqrt(r)
SWR = (1 + |Gamma|) / (1 − |Gamma|)
equivalent S11(dB) = 10 log10(r)
```

- **10.000 MHz:** r = 1.14/1.64 = 0.69512195; Pacc = 0.50 W; accepted = 30.4878%; |Gamma| = 0.833740; calculated SWR = 11.0293; equivalent S11 = −1.57939 dB.
- **11.800 MHz:** r = 0.0055/0.71 = 0.00774648; Pacc = 0.7045 W; accepted = 99.22535%; |Gamma| = 0.088014; calculated SWR = 1.19302; equivalent S11 = −21.10896 dB. All derived quantities in this point depend on the provisional reflected-power interpretation.

## Archived NanoVNA comparison: source separation

The October 4 root measurements are present locally but were **untracked** at the start of this review. They are valid local evidence, not files from the latest committed September report. Headers identify NanoVNA F V2, COM3, Saver correction using `calibration2.cal`, ideal standards assumed, and device calibration unverified. They contain 401 samples from 7 to 15 MHz with 20 kHz spacing. Do not apply the Saver calibration a second time.

### 50-ohm terminated one-port measurement

Source: [`slowwave_board_output_50OHm_7-15MHz.s2p`](../slowwave_board_output_50OHm_7-15MHz.s2p).

- Minimum measured S11: **−17.652584 dB at 11.960 MHz**; calculated SWR **1.301575**.
- At 10.000 MHz: S11 **−1.752470 dB**, SWR **9.946343**.
- At 11.800 MHz: S11 **−14.903572 dB**, SWR **1.438469**; reflected fraction approximately **3.23%**, accepted approximately **96.77%**.
- Its S21 at 11.960 MHz is approximately **−82.89 dB**, consistent with an unused/noise-level receiving channel in this terminated setup. That trace cannot support board insertion loss, meaningful transmission phase, or group delay.

### Direct two-port board and THRU reference

Board: [`slowwave_board_no_antenna_7-15MHz_1.s2p`](../slowwave_board_no_antenna_7-15MHz_1.s2p). Reference: [`thru_reference_7-15MHz.s2p`](../thru_reference_7-15MHz.s2p).

Define H = S21(board)/S21(THRU). These files have identical frequency grids. Magnitude normalization uses 20 log10|H|; positive insertion loss is its negative. Local group delay is −d(arg H)/d(2πf), using unwrapped phase and adjacent samples (central difference internally, one-sided at endpoints), matching the documented repository method. This is reference normalization, not full two-port fixture de-embedding.

| Frequency (MHz) | Board S11 (dB) | Board S21 (dB) | THRU S21 (dB) | H magnitude (dB) | Direct delay (ns) | THRU delay (ns) | Excess delay (ns) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 10.000 | −1.825781 | −6.700374 | −0.274561 | −6.425813 | 59.9886 | 8.7922 | 51.1964 |
| 11.800 | −15.631231 | −1.820753 | −0.306964 | −1.513789 | 179.2257 | 10.1240 | 169.1018 |
| 11.960 | −18.513916 | −1.708902 | −0.312343 | −1.396558 | 182.7049 | 11.4078 | 171.2970 |
| 12.000 | −18.004320 | −1.712836 | −0.303834 | −1.409002 | 183.3853 | 14.5097 | 168.8756 |

Thus the historical **approximately −1.7 dB direct S21 and 1.4 dB THRU-corrected loss are verified near 11.96 MHz**. The **approximately 169 ns** result is verified at **11.80 MHz (169.10 ns)** and **12.00 MHz (168.88 ns)**; the unsmoothed value at exactly **11.96 MHz is 171.30 ns**, not exactly 169 ns. These transmission results and the −17.65 dB one-port minimum come from different acquisitions. Direct two-port S11 minimum is −18.5931 dB at 11.940 MHz.

At 11.960 MHz, direct S21 phase is −46.6712° and THRU phase −57.9448°; the ratio phase is +11.2736° modulo 360°. Its equivalent −348.7264° branch is equally valid. The sweep starts at 7 MHz and crosses very weak transmission; it does not independently fix accumulated phase from DC. Local delay in the transmitting window is useful, whereas absolute phase delay inferred from an arbitrary unwrapped branch is not established.

At 11.96 MHz, direct power transmission |S21|² is approximately 67.47% of incident power; normalized |H|² is approximately 72.50%. Neither is today's measured output power. The approximately 99.23% input acceptance at 11.8 MHz must not be interpreted as 99.23% through-board efficiency.

### Open, short, reverse, and antenna controls

The open/short files describe the **board with its output open/shorted**, not standalone ideal input calibration standards. Their input response includes the board and its losses. S12/S22 are placeholders throughout these NanoVNA exports, not reverse measurements.

| Local file | Minimum S11 in 7–15 MHz | Interpretation |
|---|---|---|
| `slowwave_board_output_open_7-15MHz.s2p` | −4.1336 dB at 10.060 MHz | Board output open; no meaningful S21/delay from noise channel |
| `slowwave_board_output_short_7-15MHz.s2p` | −4.5922 dB at 12.020 MHz | Board output short; no meaningful S21/delay from noise channel |
| `slowwave_board_output_50OHm_7-15MHz.s2p` | −17.6526 dB at 11.960 MHz | Board with actual 50-ohm termination; S11 comparison |
| `thru_reference_7-15MHz.s2p` | −31.0627 dB at 14.780 MHz | Direct transmission reference; S21 −0.31234 dB at 11.960 MHz |
| `slowwave_board_no_antenna_7-15MHz_1.s2p` | −18.5931 dB at 11.940 MHz | Direct two-port board measurement |
| `slowwave_board_reverse_no_antenna_7-15MHz.s2p` | −8.0197 dB at 11.940 MHz | Separately measured reverse orientation; S21 −2.4241 dB at 11.960 MHz |
| `reference_loading_coil_30cm_10p87MHz.s2p` | −13.8630 dB at 10.860 MHz | Separate 30-cm antenna/loading-coil reference |
| `slowwave_board_30cm_7-15MHz.s2p` | −4.1923 dB at 9.980 MHz | Separate board/30-cm antenna configuration |
| `slowwave_board_30cm_7-15MHz_1.s2p` | −4.1765 dB at 9.980 MHz | Repeat antenna configuration |
| `slowwave_board_coil_30cm_7-15MHz_1.s2p` | −12.3546 dB at 11.220 MHz | Separate board/coil/30-cm antenna configuration |

The reported **10.87 MHz** belongs exclusively to the **30-cm antenna with matching/loading coil**. The corresponding named reference file samples 10.86 and 10.88 MHz, with its sampled minimum at **10.86 MHz**. Thus 10.87 MHz is an approximate earlier marker value, not an exact sampled minimum, and is not the board/50-ohm resonance.

### Earlier committed September measurements and reports

[`RF_PROJECT_MEASUREMENT_UPDATE_2026-09-08.md`](RF_PROJECT_MEASUREMENT_UPDATE_2026-09-08.md), the [detailed tuning report](experiments/2026-09-08/report.md), its [screenshot addendum](experiments/2026-09-08/screenshot-addendum.md), and the [README](../README.md) describe the earlier hardware checkpoint, not today's power tests.

At 11.975 MHz, independently recalculated `Saver measured 12.s2p` gives S11 **−17.605276 dB**, direct S21 **−1.527716 dB**, direct local delay **167.8076 ns**. `Saver measured 11.s2p` is that session's direct/THRU reference (not a short-circuit load); at the same point its S21 is **−0.029043 dB**, delay **1.4714 ns**, giving excess delay **166.3362 ns**. The report's **168.3 ns at 12.0875 MHz** is a different frequency; its **94.907 ns** is an average over 10.0625–19.9625 MHz. These must not replace October point values.

The earlier report also records calibration anomalies (including S11 above 0 dB outside the selected transmitting band), weak-stopband phase sensitivity, and the distinction between measured response matching and inferred physical component values. The later file headers still mark device calibration unverified. Reference division improves comparison but does not establish absolute accuracy.

## Conclusions and next observations

1. At 10 MHz, the reported board input strongly reflects power: approximately 69.51% reflected, 30.49% accepted. Calculated SWR 11.03 agrees with the displayed approximately 11; archived terminated-board SWR at 10 MHz is approximately 9.95, qualitatively consistent but not identical.
2. At 11.8 MHz, provisional reflected power 0.0055 W implies approximately 0.7045 W accepted and 99.23% input acceptance. Calculated SWR 1.193 agrees with displayed 1.19. The archived one-port SWR at the same frequency is 1.438, so today's apparent match is better than the archived value. Different setups, reference planes, power levels, drift, and instrument uncertainties prevent asserting exact agreement or a proven resonance shift.
3. Two power points do not locate the high-power optimum. The 11.8 MHz observation falls near the archived 11.96 MHz matched window, but does not establish that the minimum shifted there. Record additional points on both sides, retaining the raw reflected display and actual generator setting for each.
4. Low reflection demonstrates input acceptance. Board loss, output load power, heating, and radiation remain unseparated without additional measurements. Today's data do not establish amplifier efficiency, board power-handling limit, high-power S21, phase, or delay.
5. For subsequent rows record meter range/units, amplifier current, settling time, board/load temperatures or observed heating, unchanged/changed connections, and repeat readings. A control with the same generator setting and a repeat of the initial board point will improve comparisons. No physical retuning or simulator changes are authorized by this log.

## Repository review record

Reviewed the repository layout (desktop application, CLI, tests, archived before/after artifacts, saved models, guided-session folders), documentation and experimental reports, calibration assumptions, Touchstone import/export conventions, and RF phase/group-delay calculation method. All **291 local `.s2p` files** were parsed across root, tests, archived evidence, and ignored session folders: **158 distinct byte contents**; nine-number Touchstone records were complete. Duplicate captures are not independent measurements. The test S2P is simulation data. Earlier Saver exports include calibration-dependent/unphysical values and must not be pooled as equivalent board captures.

Latest five commits examined (all September 8, 2026):

| Commit | Subject |
|---|---|
| `ea46f9b` | Add project README with setup, workflows, and measurement evidence |
| `b576206` | Document reference-corrected RF measurements and archive sweeps |
| `a12eac9` | Archive corrected nine-capacitor model and comparison screenshot |
| `27e8e3f` | Archive remaining NanoVNA measurements and Saver calibration |
| `6c2098a` | Document RF board tuning experiment and measured delay agreement |

At review start the ten named October/antenna root S2P files above were untracked, with no tracked modifications reported. They were read only and left in place. This task adds only this measurement log; no code, calibration, model, or archived measurement files are changed.


## Follow-up: output-side power measurement

Operator reported after moving SW-28HF between the board and the 50-ohm load:

```text
FY6900 → amplifier (15 V) → LPF (both switches: 20m)
→ board → SW-28HF → 50-ohm load
```

The frequency and drive are carried forward from the immediately preceding instructions: **11.800 MHz, generator 0.2 V**. The operator supplied the readings below without explicitly restating those settings; comparison is conditional on them remaining unchanged.

| Frequency (MHz) | Generator (V) | Power display (W) | Forward display | Reflected display | SWR |
|---:|---:|---:|---|---|---:|
| 11.800 (assumed unchanged) | 0.2 (assumed unchanged) | 0.61 | `F0.61` | `R 0000` | 1.01 |

`R 0000` means no resolved reflected power on the displayed scale, not a verified physical zero. With this limitation, net power toward the load is approximately **0.61 W**. Output SWR 1.01 characterizes the downstream load/connection, not the board input match.

Conditional comparison with the preceding input-side readings (Pf = 0.71 W, provisional Pr = 0.0055 W, Pacc = 0.7045 W):

- Output / accepted input = 0.61 / 0.7045 = **86.59%**.
- Accepted input minus output = 0.7045 − 0.61 = **0.0945 W**, approximately **13.41%** of accepted input.
- Accepted-input-to-output attenuation = 10 log10(0.7045/0.61) = **0.626 dB**.
- Output / forward input = 0.61 / 0.71 = **85.92%**, equivalent attenuation **0.659 dB**. This incident-power ratio is the more directly comparable quantity to a matched-load S21 power ratio; it still uses sequential meter placements and an assumed unchanged operating state.

These are preliminary system estimates. The difference includes board and connection/meter effects and measurement uncertainty; it is not a separate measurement of board heat or radiation. Meter relocation changes the fixture and can change the amplifier operating point. The approximately 0.66 dB incident-to-output attenuation is lower than the archived THRU-normalized approximately 1.51 dB at 11.8 MHz, so agreement with historical NanoVNA loss is not established. Repeat the input-side reading at unchanged settings after the output reading before assigning a firm board loss.

## Follow-up: input-side repeat after output measurement

Operator reported after returning SW-28HF to the board input: power display **0.7 W**, forward **0.7 W**, reflected display **`R0055`**, SWR **1.19**. Frequency 11.800 MHz and generator setting 0.2 V are assumed unchanged from the preceding sequence; they were not restated. Continue to treat `R0055` as provisionally 0.0055 W.

| Placement / sequence | Forward (W) | Reflected (W) | Display SWR | Net power (W) |
|---|---:|---:|---:|---:|
| Input, initial | 0.71 | 0.0055 (provisional) | 1.19 | 0.7045 |
| Output, between input readings | 0.61 | Below displayed resolution (`R 0000`) | 1.01 | approximately 0.61 |
| Input, repeat | 0.70 | 0.0055 (provisional) | 1.19 | 0.6945 |

The repeated forward power is 0.01 W (1.41%) below the initial input reading. The reflected display and SWR are unchanged. This supports short-term consistency at the displayed resolution, without quantifying instrument accuracy or removing fixture effects.

Using the repeated input reading and approximately 0.61 W output:

- Accepted input = 0.70 − 0.0055 = **0.6945 W**; accepted fraction **99.2143%**.
- Calculated input SWR = **1.1945**, consistent with displayed 1.19.
- Output / accepted input = 0.61/0.6945 = **87.83%**.
- Net input minus output = **0.0845 W**, or **12.17%** of accepted input.
- Accepted-input-to-output attenuation = **0.563 dB**.
- Output / incident forward input = 0.61/0.70 = **87.14%**; incident-to-output attenuation = **0.598 dB**.

Across the two bracketing input readings, inferred output/accepted-input ratio is **86.59–87.83%**, and net input minus output is **0.0845–0.0945 W**. This interval reflects only the two observed input readings, not an uncertainty bound. A simple bracketing midpoint gives accepted input approximately 0.6995 W, output/accepted input approximately **87.21%**, and difference approximately **0.0895 W**; it assumes the output reading represents the same operating state.

The current practical estimate is approximately **0.61 W delivered toward the load from approximately 0.69–0.70 W accepted input**, with an apparent system deficit around **0.09 W**. Do not assign the entire deficit specifically to board dissipation or infer radiation efficiency. The difference from the archived NanoVNA attenuation remains unresolved.
