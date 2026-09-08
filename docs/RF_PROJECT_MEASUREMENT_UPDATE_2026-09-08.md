# RF Slow-Wave Project: Measurement and Direction Update

**Date:** 8 September 2026  
**Status:** measured 10-coil board, through-reference comparison completed

## 1. Main project objective

The primary objective is to create an artificial electromagnetic medium that increases the propagation constant \(\beta\), reduces effective wavelength, and slows phase progression over a short physical distance.

The present LC board is a one-dimensional electrical analogue of that future medium:

- series inductors represent increased distributed inductance;
- shunt capacitors represent increased distributed capacitance;
- the cascade produces a large electrical length within a short physical length.

The eventual goal is not merely an LC matching network. It is a graded physical medium, associated with effective permittivity \(\varepsilon\) and possibly permeability or geometry-dependent inductance, surrounding or interacting with an electrically small radiator.

## 2. Hardware state used for the measurement

- Active path: 10 adjustable series coils and 9 adjustable shunt capacitors.
- Physical active length, first coil to last coil: approximately **17 cm**.
- Measurement system: NanoVNA-F V2, 50-ohm reference.
- `Saver measured 11.s2p`: short/direct reference path.
- `Saver measured 12.s2p`: signal passing through the LC board.
- Both files contain 401 samples from 5 to 50 MHz and were acquired approximately 72 seconds apart.
- The same Saver software calibration was applied to both files.

The useful complex response of the LC structure alone is evaluated as:

\[
H_{LC}(f)=\frac{S21_{board}(f)}{S21_{reference}(f)}
\]

This division removes most cable, adapter, and direct-path phase delay.

## 3. End-to-end delay result

Over the existing measured samples from **10.0625 to 19.9625 MHz**:

| Quantity | Result |
|---|---:|
| Direct-path group delay | 1.392 ns |
| Board group delay | 96.299 ns |
| Excess LC-structure group delay | **94.907 ns** |
| Excess S21 phase change across the band | **-338.25 degrees** |

The 17 cm distance would take approximately **0.567 ns** to traverse in free space. Treating the measured excess delay as an end-to-end effective group delay gives:

\[
v_{g,eff}=\frac{0.17}{94.907\ \mathrm{ns}}
\approx 1.79\times10^6\ \mathrm{m/s}
\]

and the corresponding band-average group-delay factor is:

\[
n_{g,band}=\frac{c}{v_{g,eff}}\approx167
\]

Thus, the 17 cm LC path provides approximately the same delay as **28.5 m of free-space propagation**. This is an end-to-end finite-network result, not yet a measurement of a local homogeneous-material velocity.

## 4. Two useful operating regions

The measured board has two useful 50-ohm transmission windows.

| Region | Approximate usable window* | Representative frequency |
|---|---:|---:|
| Lower-frequency window | 5.225-6.0125 MHz | 5.5625 MHz |
| Higher-frequency window | 11.6375-12.3125 MHz | 11.975-12.0875 MHz |

\*Usable here means measured S11 at or below -10 dB and S21 at or above -3 dB.

### Representative-point comparison

| Quantity | 5.5625 MHz | Approximately 12 MHz |
|---|---:|---:|
| S11 | -32.36 dB | -17.61 dB at 11.975 MHz |
| S21 | -0.56 dB | -1.47 to -1.53 dB |
| Incident power reflected | 0.06% | 1.7-2.5% |
| Incident power transmitted | 88.0% | 70-71% |
| Apparent missing/internal power | about 12% | about 26-28% |
| Excess unwrapped phase | about -170.3 degrees | about -365.3 degrees at 12.0875 MHz |
| Local excess group delay | about 136.6 ns | about 168.3 ns at 12.0875 MHz |
| Phase delay, \(-\phi/\omega\) | about 85.1 ns | about 83.9 ns |
| Phase-index/compression estimate for 17 cm | about 150 | about 148 |
| Local group-index estimate for 17 cm | about 241 | about 297 |

## 5. Interpretation of 5.6 MHz versus 12 MHz

The 12 MHz region has the larger local group delay, but this does not automatically make it the better artificial-medium operating point.

- The phase compression is nearly the same at both frequencies: approximately 150.
- Transmission and matching are substantially better near 5.6 MHz.
- The difference between phase delay and group delay is larger near 12 MHz.
- Therefore the 12 MHz region is more dispersive and more strongly influenced by resonance.
- The 5.6 MHz region presently gives the better combination of phase compression, matching, and low insertion loss.

For artificial-medium research, **5.6 MHz is the preferred current characterization region**. The 12 MHz region remains useful for studying stronger resonant energy storage and for a future small-antenna experiment, where the radiator is electrically less small than at 5.6 MHz.

High group delay alone is not the optimization goal. The desired medium should have:

- large, smoothly accumulated phase;
- low reflection;
- low dissipative loss per accumulated phase turn;
- bounded and preferably smooth group delay;
- distributed rather than localized energy storage;
- minimal difference between phase and group indices over the intended band.

## 6. Loss interpretation

For the measured passive two-port structure, the incident power can be approximately separated as:

\[
P_{refl}=|S11|^2,\qquad
P_{out}=|S21|^2,\qquad
P_{missing}=1-|S11|^2-|S21|^2
\]

Across 10-20 MHz, the arithmetic band averages were:

- reflected: **47.5%**;
- transmitted to Port 2: **31.1%**;
- missing from the measured two-port balance: **21.4%**.

The large S21 reduction away from the pass windows is mainly reflection, not purely coil loss. For example:

| Frequency | Reflected | Transmitted | Missing |
|---:|---:|---:|---:|
| 11.975 MHz | 1.7% | 70.3% | 27.9% |
| 15.0125 MHz | 66.6% | 20.4% | 13.0% |
| 19.9625 MHz | 85.7% | 5.1% | 9.1% |

`P_missing` includes component dissipation, possible radiation, fixture effects, and calibration residuals. In the present terminated bench configuration, coil loss is expected to be the dominant physical contribution, but the S-parameter pair alone cannot uniquely separate these mechanisms.

## 7. Effective permittivity interpretation

If a homogeneous, nonmagnetic material had a refractive index of approximately 150, the formal relation

\[
n\approx\sqrt{\varepsilon_r\mu_r}
\]

with \(\mu_r=1\) would imply:

\[
\varepsilon_{r,formal}\approx150^2\approx22500
\]

This number is only a scale comparison. The LC ladder must not be described as a verified homogeneous dielectric with \(\varepsilon_r=22500\), because:

- both effective series inductance and shunt capacitance are increased;
- the structure is discrete and finite;
- its behavior is frequency-dispersive;
- terminations and resonances influence end-to-end phase and delay;
- no local field or node-phase measurement has yet established a homogeneous propagation constant.

The defensible result is that the board produces an **effective phase compression of approximately 150** over its 17 cm active length at the identified operating points.

## 8. Small-antenna experiment: important distinction

A 10-20 cm wire connected only to the final node is predominantly a small capacitive antenna load. The complete circuit could then behave as:

\[
50\ \Omega\ source \rightarrow complex\ LC\ matching\ network
\rightarrow capacitive\ short\ radiator
\]

The series inductance can resonate with the antenna capacitance in the same way as a conventional loading coil. Therefore:

- good S11 alone would not prove an artificial-medium antenna;
- a 50-ohm load must not remain in parallel with the antenna;
- the antenna replaces the output load during a one-port input-S11 experiment;
- an over-air S21 comparison is needed to distinguish useful radiation from matching and dissipation;
- the proper control is the same radiator with a conventional loading/matching network.

For a relative over-air comparison, Port 2 should feed a separate fixed receiving antenna. The geometry, cables, antenna orientations, and environment must remain unchanged. A common-mode choke at the transmitting feed suppresses radiation from the outside of the NanoVNA coax; it is not an input matching element.

The final concept requires the radiator to interact with or be embedded in the graded medium. A wire merely attached to the last LC element is first a terminal-load experiment, not by itself a demonstration of an embedded-medium radiator.

## 9. Recommended immediate work

Do not add the second ten cells or retune the entire board yet. Preserve the current physical settings as a measured baseline.

### Next measurement

Perform a dedicated sweep around the lower-frequency pass window:

- start: **4.5 MHz**;
- stop: **6.5 MHz**;
- points: **801** if acquisition remains stable;
- direct-reference and board measurements using the same cables and reference planes;
- at least two no-touch repeats of the board measurement.

Analyze:

1. unwrapped excess S21 phase;
2. local and fitted group delay;
3. phase-linearity residual;
4. phase index and group index versus frequency;
5. S11 and S21 bandwidth;
6. missing-power estimate;
7. insertion loss per 360 degrees of accumulated phase.

### Decision gate before more hardware work

Add or tune additional cells only if the detailed sweep shows that a longer structure is needed to increase smooth distributed phase accumulation without materially worsening reflection, loss per phase turn, or dispersion.

Proceed to the antenna experiment only after the medium-characterization result has been archived. Treat the antenna test as a separate experimental phase with a conventional-matching control.

## 10. Current concise conclusion

The ten-coil board has demonstrated large electrical length in a 17 cm physical structure. The strongest present medium-like operating point is near **5.6 MHz**, where the measured transmission is about **-0.56 dB**, matching is excellent, and phase compression is approximately **150**. The region near **12 MHz** gives higher local group delay but is more lossy and more dispersive. The next priority is a fine, repeatable 4.5-6.5 MHz characterization, not further manual tuning or immediate antenna matching.
