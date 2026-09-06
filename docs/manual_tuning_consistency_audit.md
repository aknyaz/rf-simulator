# Manual tuning and optimization consistency audit

Implemented and checked September 6, 2026. This is an implementation audit, not a validation of a physical antenna against measurements.

## Workflow

1. Set **Frequency** once: Start, Stop, sweep points, and the common real reference impedance. Simulation, manual plots, and the main optimizer use this configuration. No separate optimization range is retained.
2. **Inspect at (MHz)** automatically follows the arithmetic midpoint. For 10–20 MHz it is 15 MHz. Enable **Manual marker** to inspect elsewhere in the band. If a subsequent range change excludes the manual marker, the control returns to automatic midpoint mode.
3. The marker is a readout location, not an optimization goal. The requested frequency is mapped to the nearest sweep sample (ties choose the earlier sample). The displayed resolved frequency, red plot markers, S-parameter/phase/delay readouts, and fresh propagation calculation all use that sample's frequency. With an even point count the midpoint can lie between samples. No interpolation is used. Antenna-loaded analysis uses that frequency with its explicitly different complex load.
4. Edit an unlocked component. The current response is cleared and labeled out of date immediately; a 120 ms debounce recalculates all current views. The plots retain their last viewport only through OxyPlot behavior, not an old response cache.
5. **Optimize Current** searches the current circuit. Select a row to inspect the **Candidate Preview** tab, where solid candidate curves are compared with the dashed optimization baseline. The component table and current plots remain unchanged. **Apply Candidate** explicitly applies the selected row (first row by default).
6. **Advanced optimization settings** contains weights, iteration limit, restart count, seed, and random restart search. Both searches preserve the current topology and locked indices. Unsupported tunable types are retained unchanged; the main search changes only SeriesL and ShuntC values.

The main results table's **Band avg +GD (ns)** is the mean of positive finite sweep delay samples. The manual readout is the delay at one marker frequency. They need not match, even for the same circuit. A pair such as 77.13 ns and 76.36 ns cannot be diagnosed from the numbers alone: the original application also allowed different sweep bands and stale panels. The exported circuit, sweep, and baseline now make that comparison reproducible.

## Confirmed defects and ambiguities

| Finding | Resolution |
|---|---|
| Main and optimizer frequency controls were independent | One shared configuration; duplicate controls removed |
| Main optimizer used 50 ohms regardless of the simulation reference | Both consume the shared reference impedance |
| Touchstone export recalculated at 50 ohms and always declared R 50 | Export uses and declares the current reference; includes configuration/circuit metadata |
| Manual live edits updated only the manual response | One current-state refresh updates main, manual, propagation, and antenna diagnostics |
| Selecting a candidate, or completing optimization, silently replaced the circuit | Separate preview and explicit application |
| Manual locks were absent from the optimizer input | Locked indices included in the immutable baseline snapshot and both search paths |
| Every non-SeriesL component was searched using capacitor values | Only supported SeriesL/ShuntC types are varied; other types remain unchanged |
| Background completion could publish after an edit | Generation and revision checks discard old completion; edits also request cancellation |
| Mixed marker behavior: S-parameters at nearest sample, propagation at requested frequency | Both now use the same resolved sample frequency |
| Small-antenna terminal/exploration previews replaced current diagnostic plots | Candidate diagnostics are separate; current exports refer to current analysis |
| S21 phase ticks were always 30 degrees apart | Tick spacing scales with phase span so long circuits do not produce overlapping labels |

There was no evidence that the frequency discrepancy itself came from incorrect group-delay differentiation or a faulty LC physical model. The solver and score formula were retained.

## State and saved data

`CircuitWorkspace` owns a revisioned `CircuitSnapshot`: circuit values, locked indices, and `FrequencyConfiguration`. Snapshots clone their input and return copies, so workers cannot mutate current state. A SHA-256 circuit identifier is calculated from exact ordered component JSON; it identifies values/topology, while revision and frequency metadata identify the full run. Manual controls and the hidden JSON editor are UI adapters; `CaptureCurrentState` is the commit boundary. Main and manual plots share the same evaluated sweep. Baseline plots are explicitly separate comparisons, recalculated on the current sweep.

Circuit, sweep, reference, or lock changes invalidate candidates. Main optimization goal/seed/search-setting changes also invalidate them. Changing the marker alone does not change the current revision, optimizer objective, or main candidates. Calculations on the UI thread finish synchronously; optimization workers hold a snapshot and can publish only for the captured generation/revision. The older antenna exploration loop uses `DoEvents`; generation checks immediately after pumping events discard work if settings changed.

Current JSON saves contain circuit ID/revision, sweep/reference, exact component values, locks, optional manual marker, and model spacing. Manual **Save Structure** now writes this project object; both load actions still accept the former raw component arrays. Raw arrays retain the current sweep. Old official project objects had only the main range; it is now authoritative. Extended legacy optimizer fields are explicitly reported as ignored, including potentially conflicting ranges. Invalid saved ranges are rejected rather than silently clamped. Absent locks/marker/spacing retain compatible defaults.

Candidate JSON/CSV records the optimization configuration (including seed and baseline structure/locks), baseline score, improvement flag, feasibility, violations, and exact candidate structure. Touchstone comments record the current circuit and sweep. Antenna exports retain their existing schemas with added current-configuration context; antenna case frequency/load remain explicitly separate from the two-port matched-load sweep.

## Units and numerical checks

| Quantity | UI or output | Evaluation convention |
|---|---|---|
| Frequency | MHz | Multiply by 1e6 to get Hz; omega = 2*pi*f in radians/s |
| Inductance | nH (or formatted uH) | Multiply nH by 1e-9; JSON values in H |
| Capacitance | pF (or formatted nF) | Multiply pF by 1e-12; JSON values in F |
| Resistance/reference | ohms | Same real reference at both S-parameter ports |
| Phase | degrees | Unwrap in radians, then multiply by 180/pi |
| Group delay | ns | -delta(phi)/delta(omega) in seconds, then multiply by 1e9 |
| Cell spacing | mm | Multiply by 1e-3 to obtain meters; spatial interpretation only |

The main and manual S-parameters and optimizer all call `SimulationEngine.Simulate`, using the same cascaded ABCD evaluator and equal real reference impedances. Propagation reconstructs voltages/currents with the existing separate ABCD propagation implementation; its matched-load input impedance is checked against impedance derived from S11. Antenna analysis supplies a complex termination instead of the reference-matched output, so its match and power numbers intentionally differ.

`RfMath.CalculateGroupDelayNs` uses a central secant for interior sweep points and one-sided differences at the endpoints. The uniform sweep makes the central secant the centered finite difference. Endpoint accuracy is lower. Existing behavior for fewer than two samples or a nonpositive frequency interval is zero; the shared configuration rejects such sweeps before evaluation. Nonfinite derivative values are still converted to zero by this legacy helper; the optimizer now rejects nonfinite S-parameter responses rather than presenting those as usable candidates.

Unwrapping assumes adjacent samples do not hide a phase change larger than pi. Coarse sweeps can miss narrow resonances and alias phase; no finite grid proves convergence. Near an S21 null the phase and its derivative become sensitive to numerical error. Current status flags transmission magnitudes below 1e-6 (S21 below -120 dB) as a caution to refine the sweep. This is a diagnostic threshold, not a new scoring term, physical rejection rule, or guarantee of accuracy above that threshold. Delay curves are not smoothed or forced to increase.

Regression checks cover a synthetic 123 ns pure delay through multiple wraps, zero-delay through, synthetic near-zero transmission magnitude, and a single series inductor compared with its analytic delay. For the inductor, with a = L/(2 Z0), delay = a/(1 + (omega*a)^2). Tolerances are 1e-8 ns interior and 1e-5 ns endpoints on the fine test grid. The synthetic near-null test checks arithmetic, not reliability of real null measurements.

Lossless LC tests verify |S11|^2 + |S21|^2 = 1 and reciprocity within 1e-11 at 25, 50, 75, and 100 ohms. A resistor control confirms that loss is not mistakenly removed or forced to conserve transmitted-plus-reflected power. No such lossless requirement is imposed on resistive or radiating models.

## Existing objective, unchanged

Lower score is better. All metrics are over the configured band, not the inspection marker:

- Worst S11: largest S11 dB (closest to zero); penalty 10 times squared excess over the S11 limit.
- Average S11: arithmetic mean of sampled dB values; penalty 5 times squared excess over that same limit.
- Worst S21: smallest S21 dB; penalty 5 times squared shortfall below the S21 limit.
- S21 ripple: squared difference between maximum and minimum S21 dB.
- Optional phase: absolute unwrapped endpoint difference, not the sum of every phase excursion. Shortfall from the phase goal is divided by 90 degrees, squared, and multiplied by 25 times phase weight.
- Optional delay reward: minus delay weight times log(1 + mean positive delay in ns), only when both worst S11 and worst S21 satisfy their limits. This rewards larger positive band-mean delay. It is neither a target-delay objective nor a flatness objective. Zero/negative delays are excluded from this legacy aggregate.
- Component-gradient penalty: 0.5 times the sum of squared log ratios of adjacent SeriesL values and adjacent ShuntC values (each type considered separately).

The score uses soft penalties, so the best returned candidate may still violate goals. Feasibility reports worst S11, worst S21, and enabled phase goal separately; lower score is not called feasible automatically. An infeasible result may be explicitly applied for inspection, with an unmet-goals message. No new objective or hard constraint was added to the search itself.

## Repeated values and reproducibility

100 nH and 62 pF are members of the existing preferred-value lists. Identical neighboring values have zero gradient penalty, so repetition can be a valid score preference. Sequential search tries ordered values and accepts the first improvement, scanning elements from the beginning again after an improvement. At a finite iteration limit it may leave later initial values untouched. Off-list initial values are not rounded on load; a searched replacement comes from the discrete list. Locks preserve exact original values. There is no requirement that components differ, and no indexing defect forcing these repeated values was found. The historical screenshot alone cannot establish which search step produced its exact repeated cells.

The preview reports how many values changed and compares the candidate score with the exact baseline evaluated under the same configuration. Random restart mode records the seed (default 12345), preserves current topology/locks, and generates restarts in a stable order. Parallel completion order does not determine the winner: value order and restart index resolve ties. Tests compare identical seeds with one and four workers, requiring identical component JSON and scores within 1e-12. Bitwise identity across different .NET runtimes/architectures is not promised; comparisons near a score tie can be sensitive to floating-point differences.

## Model-dependent spatial and antenna readouts

Slow-wave factor is computed from the unwrapped node-voltage phase difference: beta = -delta(phi)/spacing, phase velocity = omega/beta, SWF = c/(phase velocity). Beta has units rad/m; velocity m/s; SWF is dimensionless. Nonpositive beta does not produce a valid positive-velocity SWF and is excluded from summary extrema. In a finite reflected ladder this is a local phase diagnostic, not a measured Bloch propagation constant or evidence of antenna efficiency. Changing the assumed spacing rescales SWF without changing the lumped S-parameters.

Stored-energy estimates use the existing 0.5*L*|I|^2 and 0.5*C*|V|^2 conventions, in joules. The matched propagation calculation normalizes output voltage to 1 V and uses real power Re(V*conj(I)); the manual load-power readout is therefore a normalization-dependent quantity, not a fixed input-power efficiency. Manual energy concentration groups each series energy with the correspondingly indexed capacitor energy and divides the largest group by their total. It is dimensionless and depends on that grouping/topology. This interpretation is best suited to alternating SeriesL/ShuntC ladders; generalized elements do not have a complete spatial energy model. Readouts and the propagation tab are explicitly labeled model-dependent.

The distributed radiation curves remain first-order current-element proxies. They do not include full geometry, mutual coupling, a calibrated electromagnetic radiation solution, or a guarantee of power-consistent absolute gain. No attempt was made to rewrite their physical model or equate high SWF/energy concentration with useful radiation efficiency.

## Validation command

```powershell
dotnet build rf_sim_cli/rf_sim_cli.csproj
dotnet run --project rf_sim_cli/rf_sim_cli.csproj --no-build -- --run-tests tests/cli
```

The existing CLI regression runner now includes six focused workflow/numerical groups alongside five existing physics fixtures. UI checks exercise actual WinForms controls on an STA thread: shared 10–20 MHz/401-point setup, automatic/manual marker behavior, component edits, locks, preview isolation, explicit application, stale-worker rejection, invalid ranges, and saved-project loading. These tests do not validate physical antenna performance.
