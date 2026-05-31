# RF Simulator CLI

Run from the repository root:

```powershell
.\rf_sim_cli.cmd --input tests\small-antenna-best-structure1.json --mode antenna --freq-start 5 --freq-stop 50 --points 401
.\rf_sim_cli.cmd --input tests\small-antenna-best-structure1.json --mode compression --freq 27.5 --spacing-mm 5
.\rf_sim_cli.cmd --mode optimize-antenna --iters 1000 --output best_antenna.json
.\rf_sim_cli.cmd --mode optimize-compression --iters 1000 --output best_medium.json
.\rf_sim_cli.cmd --mode grow-compression --max-cells 12 --target-rl-db -10 --iters-per-step 2000 --output grown_medium.json
.\rf_sim_cli.cmd --mode grow-compression --seed tests\cli\known_good_seed.json --max-cells 12 --output grown_medium.json
.\rf_sim_cli.cmd --optimize compression --iters 10000 --launcher-cells 2 --auto-test tests\cli --save-best best_medium.json
.\rf_sim_cli.cmd --run-tests tests\cli
```

Build the CLI directly:

```powershell
dotnet build rf_sim_cli\rf_sim_cli.csproj
```

The CLI prints deterministic JSON metrics. Analysis commands exit with code `0` when their built-in pass flags pass and code `2` when analysis completes but thresholds fail. Regression mode exits with code `0` when all fixtures pass.

CLI optimizer output files such as `grown_medium.json` contain a raw LC structure array. The desktop GUI `Load JSON` action accepts these files directly and keeps the sweep settings currently shown in the GUI. GUI-saved configuration files remain wrapped objects containing sweep settings and the structure.

## Batch Optimization

Use batch optimization for repeatable automated experimentation:

```powershell
.\rf_sim_cli.cmd --optimize compression --iters 10000 --launcher-cells 2 --auto-test tests\cli --save-best best_medium.json --history-json optimizer_history.json --history-csv optimizer_history.csv --seed 12345
```

The batch optimizer:

- runs regression fixtures before and after optimization when `--auto-test` is provided;
- mutates LC values automatically;
- optimizes the first `--launcher-cells` LC cells as a gradual input-coupling taper;
- rejects candidates that fail the CLI physics-validation flags;
- retains only valid candidates with a lower optimizer score;
- updates the best-structure JSON whenever an improvement is accepted;
- records every accepted and rejected candidate in JSON and CSV history files.

Compression validation reports two separate results:

- `internal_medium_quality`: the downstream medium has bounded slow-wave behavior and distributed energy.
- `usable_rf_device`: internal quality passes, return loss meets `--target-rl`, and average reflected input power is at most `--max-average-reflection-percent`.

The compression-mode `overall` flag is the `usable_rf_device` result. Batch optimization may retain an improving internal-only candidate as an exploration state, but it saves `--save-best` only after the usable-device constraints pass.

Use `--optimize antenna` for antenna matching. The default seed is `12345`, so repeated runs with the same arguments produce the same result. Optional mutation controls are:

```powershell
--mutations-per-candidate 1 --mutation-log-step 0.35 --continuous
```

`--mutation-log-step` applies to continuous-value optimization. Without `--continuous`, the optimizer mutates values using the simulator's discrete component lists.

For compression mode, the default `--launcher-cells 2` treats the first two LC cells as an impedance taper. Input coupling is scored from broadband reflected power, launcher monotonicity, abrupt launcher jumps, and the transition from the launcher into the downstream slow-wave medium. Compression smoothness, phase progression, and distributed-energy scoring remain focused on the downstream medium. Batch compression runs direct `65%` of mutations into the launcher by default; change this with `--launcher-mutation-fraction 0.65`. The default coupling gate is `--max-average-reflection-percent 50`.

## Incremental Synthesis

Use `grow-compression` to build an adiabatic taper one LC cell at a time:

```powershell
.\rf_sim_cli.cmd --mode grow-compression --max-cells 12 --target-rl-db -10 --iters-per-step 2000 --output grown_medium.json
```

Start from a verified multi-cell launcher:

```powershell
.\rf_sim_cli.cmd --mode grow-compression --seed tests\cli\known_good_seed.json --max-cells 12 --output grown_medium.json
```

The synthesizer:

- searches both `SeriesL + ShuntC` and `ShuntC + SeriesL` seed orientations;
- loads a multi-cell launcher directly when `--seed` is a JSON path;
- validates launcher seeds using consistent RF metrics, target return loss, delivered power, bounded slow-wave behavior, distributed energy, input coupling, internal-medium quality, usable-device quality, and resonance limits;
- includes standard components and deterministic logarithmic samples from the configured L/C ranges;
- appends one LC cell at a time;
- optimizes the new cell and the previous two cells by default;
- accepts growth extensions only when matching and delivered power remain controlled while phase delay and group delay grow and the slow-wave factor stays bounded;
- applies full internal-medium, input-coupling, resonance, and energy-localization checks during final-device validation;
- stops when the cell limit, runtime limit, or coupling-preserving growth limit is reached.

The JSON output includes `accepted_cells`, `rejected_step_count`, `stop_reason`, `final_structure`, `final_metrics`, `final_validation_reasons`, `best_rejected_seeds`, `growth_report`, and step-by-step best structures. `growth_report` is a concise bench-style progression showing return loss, reflected input power, delivered power, phase, group delay, slow-wave maximum, pass/reject status, and rejection reason at each attempted length. `best_rejected_seeds` records seed return loss, delivered power, reflected input power, values, and rejection reasons. `--output` is written only after a usable RF-device structure is found. Intermediate bootstrap candidates remain visible in the JSON report without being exported as successful devices.

Grow-compression validation uses 50-ohm two-port metrics at `--freq` as its authoritative narrowband device measurements: `target_frequency_s11_db`, `target_frequency_s21_db`, `target_frequency_phase_deg`, and `target_frequency_group_delay_ns`. When `--freq` is omitted, the target is the sweep midpoint. An extension must preserve target-frequency S11/S21 while improving target-frequency phase delay and group delay.

The separate `best_s11_db` and `delivered_power_db` fields come from the antenna-terminated sweep and remain diagnostic for antenna mode. The `broadband_sweep_*` fields come from the 50-ohm two-port sweep and remain diagnostics for bandwidth interpretation. They do not override target-frequency grow-compression validation. A narrowband grow run reports `broadband_sweep_quality = false` when broadband reflection is poor, but broadband quality does not fail an otherwise usable target-frequency design.

The grow result emits `final_status`, `final_verdict`, and the nested `final_metrics.pass_fail_flags`. Top-level `passed` is exactly `final_metrics.pass_fail_flags.overall`, and `final_usable_rf_device` is exactly `final_metrics.pass_fail_flags.usable_rf_device`. Internal slow-wave, resonance, and energy-localization checks still apply before an extended structure is exported. Use `--random-seed 12345` to control deterministic mutation when `--seed` is occupied by a JSON path.

RF metric fields are explicit about their operating point:

- `best_s11_db` is signed S11 dB at the best-match frequency: `20 log10(|gamma|)`.
- `best_return_loss_positive_db` is conventional positive return loss: `-best_s11_db`.
- `best_return_loss_db` remains as a backward-compatible alias for signed `best_s11_db`.
- `best_match_reflection_coefficient_magnitude` is `|gamma|` at that same frequency.
- `best_match_reflected_power_percent` is `|gamma|^2 * 100` at that same frequency.
- `average_input_reflection_percent` is the sweep-average reflected power percentage, not the best-frequency value.
- `delivered_power_db` and `delivered_power_at_best_match_db` are delivered antenna power at the best-match frequency.
- `peak_delivered_power_db` is the best delivered power found anywhere in the sweep.

The CLI emits `reflection_metrics_consistent` and `power_balance_consistent` flags. For the lossless LC network, accepted power `1 - |gamma|^2` must match antenna-delivered power within numerical tolerance.

Useful controls:

```powershell
--optimize-previous-cells 2 --runtime-limit-seconds 0
--min-seed-delivered-power-db -6 --max-delivered-power-collapse-db 1
--min-phase-increase-deg 0.01 --min-group-delay-increase-ns 0.01
--max-resonance-penalty 500
```

## Regression Fixture Format

```json
{
  "name": "smooth gradient medium",
  "mode": "compression",
  "frequency_mhz": 27.5,
  "spacing_mm": 5,
  "expect_overall_pass": true,
  "expected_metric_ranges": {
    "lc_smoothness_error": { "max": 1 },
    "energy_concentration_ratio": { "max": 0.9 }
  },
  "structure": [
    { "type": "SeriesL", "value": 5e-8 },
    { "type": "ShuntC", "value": 1.5e-11 },
    { "type": "SeriesL", "value": 7.5e-8 }
  ]
}
```
