# Technical Task: Add Small Antenna / Artificial Medium Mode

## Goal

Extend the existing RF LC Structure Simulator with a new independent mode:

**Small Antenna / Artificial Medium Mode**

The existing simulator should remain unchanged and continue working as a normal two-port RF tool:

```text
50 ohm source → LC structure → 50 ohm load
```

The new mode should model a different physical problem:

```text
50 ohm source → gradient LC artificial medium → electrically small antenna
```

The purpose is to test whether an LC artificial medium can gradually compress the wave, store/reactively exchange energy, and deliver power efficiently into a small antenna load.

Do not remove or break existing features:

* S11/S21 plots
* S21 phase
* group delay
* propagation analysis
* Touchstone export
* optimizer
* existing 50 ohm two-port mode

---

## New Mode Name

Add a separate tab, mode, or panel called:

```text
Small Antenna Mode
```

or:

```text
Artificial Medium Antenna Mode
```

---

## Physical Model

In the current mode, the structure is terminated with 50 ohms.

In the new mode, the output load is not 50 ohms. Instead, it should be an antenna impedance:

```text
Zant = Rrad + Rloss + jXant
```

Where:

* `Rrad` = radiation resistance
* `Rloss` = loss resistance
* `Xant` = antenna reactance

The user should be able to enter these values.

Default example values:

```text
Rrad = 2 ohm
Rloss = 1 ohm
Xant = -300 ohm
```

This represents a small capacitive antenna.

The total antenna resistance is:

```text
Rant = Rrad + Rloss
```

The full antenna impedance is:

```text
Zant = Rant + jXant
```

---

## Core Calculation

For a selected LC structure, calculate the input impedance looking into:

```text
LC structure terminated by Zant
```

Use ABCD matrix math.

For total ABCD matrix:

```text
[A B]
[C D]
```

The input impedance with load `ZL = Zant` is:

```text
Zin = (A * ZL + B) / (C * ZL + D)
```

Then calculate input reflection coefficient relative to 50 ohms:

```text
GammaIn = (Zin - 50) / (Zin + 50)
```

Input return loss:

```text
ReturnLossIn_dB = 20 * log10(|GammaIn|)
```

This should be negative dB, same style as existing S11 plot.

---

## Delivered Power Calculation

The main metric should not be S21.

The main metric should be:

```text
power delivered to antenna radiation resistance
```

Use a 50 ohm source model.

Assume available source power is normalized to 1 W.

Calculate voltage/current at antenna load using the ABCD solution.

Then calculate:

```text
P_radiated = 0.5 * |Iant_rms|^2 * Rrad
P_loss     = 0.5 * |Iant_rms|^2 * Rloss
P_total_load = P_radiated + P_loss
radiation_efficiency = P_radiated / P_total_load
delivered_power_ratio = P_total_load / P_available_source
radiated_power_ratio = P_radiated / P_available_source
```

If the simulator uses peak phasors instead of RMS phasors, be consistent and document the convention.

For first implementation, normalized relative power is acceptable as long as it is consistent.

Show results in dB:

```text
RadiatedPower_dB = 10 * log10(radiated_power_ratio)
DeliveredPower_dB = 10 * log10(delivered_power_ratio)
```

---

## New Plots

Add plots for Small Antenna Mode:

### Plot 1 — Input Return Loss

```text
Input Return Loss vs Frequency
```

This is the equivalent of S11, but now with antenna load at the end.

### Plot 2 — Antenna Delivered Power

Show:

```text
Delivered power to total antenna resistance
Radiated power to Rrad only
```

vs frequency.

### Plot 3 — Antenna Current

```text
|Iant| vs frequency
```

This is important because small antennas often need high current.

### Plot 4 — Antenna Voltage

```text
|Vant| vs frequency
```

Useful for detecting unrealistic high-voltage resonant solutions.

### Plot 5 — Propagation / Compression View

Reuse or adapt the existing propagation analysis.

For a selected frequency, show:

* node voltage magnitude
* node phase
* local phase shift per cell
* slow-wave factor
* stored electric energy in capacitors
* stored magnetic energy in inductors

But now the final load should be `Zant`, not 50 ohms.

---

## Optimizer Changes

Add a separate optimizer objective for Small Antenna Mode.

Do not reuse the existing two-port score directly.

The optimizer should rank candidates by:

1. Good input match:

   * input return loss better than target, e.g. below `-10 dB` or `-15 dB`

2. High radiated power ratio:

   * maximize power delivered into `Rrad`

3. Reasonable loss:

   * penalize power delivered into `Rloss`

4. Avoid unrealistic resonant traps:

   * penalize excessive antenna voltage
   * penalize sharp narrowband spikes
   * penalize huge stored energy concentrated in one element

5. Encourage wave compression:

   * reward increasing phase accumulation toward the antenna side
   * reward smooth local phase shift
   * reward smooth slow-wave factor progression

6. Optional:

   * reward monotonic or mostly monotonic LC gradient toward the antenna side

---

## Candidate Structure Behavior We Want

The desired physical behavior is not symmetric matching.

We do not want:

```text
50 ohm → slow-wave section → 50 ohm
```

We want:

```text
50 ohm → gradual artificial medium → small antenna load
```

The LC values may become larger toward the antenna end.

Expected behavior:

* input side remains reasonably matched to 50 ohms
* phase accumulates along the structure
* local wavelength becomes shorter toward the antenna side
* energy storage increases smoothly toward the antenna side
* antenna current increases
* more power reaches `Rrad`
* output does not need to look like 50 ohms

---

## UI Requirements

In Small Antenna Mode, add controls:

```text
Rrad ohms
Rloss ohms
Xant ohms
Frequency start
Frequency stop
Number of points
Cell spacing mm
Target input return loss
Max antenna voltage warning threshold
```

Add status/summary box:

```text
Best input return loss
Frequency of best match
Radiated power ratio
Delivered power ratio
Radiation efficiency
Peak antenna current
Peak antenna voltage
Total phase accumulation
Average slow-wave factor
```

---

## Important Separation from Existing Mode

Keep existing mode intact.

Recommended architecture:

```text
Mode 1: TwoPortMode
- 50 ohm source
- 50 ohm load
- S11/S21/phase/group delay/Touchstone

Mode 2: SmallAntennaMode
- 50 ohm source
- LC artificial medium
- complex antenna load
- input match + delivered/radiated power + propagation compression
```

Do not modify the existing score unless absolutely necessary.

Add new score function only for Small Antenna Mode.

---

## Validation Tests

Use simple sanity tests.

### Test 1 — Direct antenna without LC structure

With a bad antenna impedance such as:

```text
Zant = 3 - j300 ohm
```

Input return loss should be poor.

### Test 2 — Simple matching network

Add a basic LC matching network and verify that input return loss improves near one frequency.

### Test 3 — Loss split

If:

```text
Rrad = 2 ohm
Rloss = 0 ohm
```

radiation efficiency should be near 100%.

If:

```text
Rrad = 2 ohm
Rloss = 2 ohm
```

radiation efficiency should be near 50%.

### Test 4 — Existing two-port mode

Existing simulator behavior must remain unchanged.

### Test 5 — Propagation analysis

For a gradient LC structure, node phase should show phase accumulation toward the antenna side.

---

## First Implementation Scope

Keep the first version simple.

Minimum required:

1. Add Small Antenna Mode UI.
2. Allow user to enter `Rrad`, `Rloss`, `Xant`.
3. Calculate `Zin`, input return loss, antenna voltage/current.
4. Plot input return loss and radiated/delivered power.
5. Reuse propagation analysis with antenna load.
6. Add a basic optimizer score for antenna mode.

Do not implement parasitic models yet.

Do not implement full antenna radiation simulation.

This is not a full EM solver.
This is a circuit-level artificial-medium matching and wave-compression simulator.
