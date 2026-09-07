# Measured S2P workflow

Open **Measurements → Import measured S2P**. Import does not change the circuit, marker, shared frequency controls, saved project, or instrument. Measurement data stays in memory; reopen the original file after restarting.

The tab plots S11/S21 magnitude, unwrapped S21 phase, and unsmoothed group delay. **Compare with current model snapshot** captures the circuit and evaluates it at the measurement frequencies and reference impedance. Gold is measured; dashed blue is the model snapshot. Click Compare again after editing the circuit. The snapshot is not a live optimization candidate. Phase traces may differ by an integer multiple of 360 degrees.

The importer supports Touchstone 1.x two-port S data, RI/MA/DB, Hz/kHz/MHz/GHz, inline comments, and wrapped records. Without an option line it uses Touchstone defaults (GHz, S, MA, 50 ohms). An explicit option line must specify all five fields. Nonfinite values, non-increasing frequencies, incomplete records, Touchstone 2 keywords, and noise sections are rejected. S12/S22 are validated but not used: zero fields in a NanoVNA export must not be interpreted as verified reverse measurements. Calibration cannot be inferred from this file.

Delay uses unwrapped phase in radians and angular frequency in radians/second, then converts seconds to ns. Interior values use neighboring samples; endpoints use one-sided differences. The band mean is computed from endpoint phase change divided by total angular-frequency span, not by averaging positive samples. Differentiation amplifies noise; coarse samples can alias phase and transmission nulls make it unreliable. The UI breaks phase/delay traces at S21 magnitudes below 1e-6; this threshold is a caution, not an accuracy guarantee.

Read-only JSON inspection:

```powershell
dotnet run --project rf_sim_cli/rf_sim_cli.csproj -- --inspect-s2p "C:\tools\s2.s2p.new1.s2p"
```

This first stage does not connect to serial hardware, change calibration, choose a working band, or recommend physical adjustments.
