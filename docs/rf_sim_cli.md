# RF Simulator CLI

Run from the repository root:

```powershell
.\rf_sim_cli.cmd --input tests\small-antenna-best-structure1.json --mode antenna --freq-start 5 --freq-stop 50 --points 401
.\rf_sim_cli.cmd --input tests\small-antenna-best-structure1.json --mode compression --freq 27.5 --spacing-mm 5
.\rf_sim_cli.cmd --mode optimize-antenna --iters 1000 --output best_antenna.json
.\rf_sim_cli.cmd --mode optimize-compression --iters 1000 --output best_medium.json
.\rf_sim_cli.cmd --run-tests tests\cli
```

Build the CLI directly:

```powershell
dotnet build rf_sim_cli\rf_sim_cli.csproj
```

The CLI prints deterministic JSON metrics. Analysis commands exit with code `0` when their built-in pass flags pass and code `2` when analysis completes but thresholds fail. Regression mode exits with code `0` when all fixtures pass.

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
