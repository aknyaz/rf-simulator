# RF LC Structure Optimizer — Technical Specification for Codex

## Project Purpose

Extend the existing **RF LC Structure Simulator** so it can automatically search for a better matched LC ladder / artificial transmission structure using only the real available component values.

The optimizer should find candidate structures that provide:

- good input matching: low S11 / good return loss
- acceptable transmission: S21 close to 0 dB
- controlled phase rotation / electrical length
- preferably smooth / gradient-like component profiles

The purpose is to avoid endless manual soldering by testing many possible LC structures in software first.

---

## Existing Context

The current simulator already supports:

- C# application
- JSON structure input
- Series inductors
- Shunt capacitors
- ABCD matrix calculation
- ABCD to S-parameter conversion
- S11 Smith chart
- S11 return loss plot
- S21 gain plot
- S21 polar plot
- frequency sweep from 5 MHz to 50 MHz

The solver appears reasonable when compared against NanoVNA measurements.

Now add **automatic structure optimization**.

---

## Available Components

### Available Series Inductors

Use only these values:

```text
10 nH
47 nH
68 nH
100 nH
220 nH
330 nH
470 nH
680 nH
820 nH
```

In SI units:

```csharp
10e-9
47e-9
68e-9
100e-9
220e-9
330e-9
470e-9
680e-9
820e-9
```

### Available Shunt Capacitors

Use only these values initially:

```text
0.5 pF
15 pF
18 pF
20 pF
22 pF
47 pF
51 pF
56 pF
62 pF
68 pF
75 pF
82 pF
91 pF
100 pF
110 pF
120 pF
130 pF
150 pF
180 pF
200 pF
220 pF
330 pF
470 pF
```

In SI units:

```csharp
0.5e-12
15e-12
18e-12
20e-12
22e-12
47e-12
51e-12
56e-12
62e-12
68e-12
75e-12
82e-12
91e-12
100e-12
110e-12
120e-12
130e-12
150e-12
180e-12
200e-12
220e-12
330e-12
470e-12
```

Important: pF values must use `e-12`, not `e-11`.

---

## Default Frequency Range

Default optimization range:

```text
5 MHz to 50 MHz
```

Default points:

```text
401
```

Default reference impedance:

```text
Z0 = 50 Ohm
```

The optimizer should allow changing these values in the UI.

---

## Structure Model

The structure is an alternating ladder:

```text
SeriesL - ShuntC - SeriesL - ShuntC - ... - SeriesL
```

Example 5-inductor / 4-capacitor structure:

```json
[
  { "Type": "SeriesL", "Value": 470e-9 },
  { "Type": "ShuntC",  "Value": 47e-12 },
  { "Type": "SeriesL", "Value": 330e-9 },
  { "Type": "ShuntC",  "Value": 68e-12 },
  { "Type": "SeriesL", "Value": 220e-9 },
  { "Type": "ShuntC",  "Value": 100e-12 },
  { "Type": "SeriesL", "Value": 330e-9 },
  { "Type": "ShuntC",  "Value": 68e-12 },
  { "Type": "SeriesL", "Value": 470e-9 }
]
```

The optimizer should support at least:

- 3 series inductors / 2 shunt capacitors
- 5 series inductors / 4 shunt capacitors
- 7 series inductors / 6 shunt capacitors
- 9 series inductors / 8 shunt capacitors

---

## Optimization Objective

The primary target is a **well-matched artificial LC structure**.

### Primary Goal

Minimize worst-case S11 magnitude over the selected frequency band.

Use S11 in dB:

```text
S11_dB = 20 * log10(|S11|)
```

More negative is better.

Example target:

```text
Target S11 <= -15 dB over the band
```

If S11 is worse than -15 dB, apply penalty.

### Secondary Goal

Keep S21 close to 0 dB.

Use:

```text
S21_dB = 20 * log10(|S21|)
```

Preferred:

```text
S21 >= -3 dB over the band
```

If S21 is below -3 dB, apply penalty.

### Optional Goal

Encourage useful phase delay / slow-wave behavior.

Use unwrapped S21 phase:

```text
phase = unwrap(angle(S21))
```

Optionally reward total phase rotation, but only if S11 and S21 are acceptable.

Do not maximize phase at the expense of huge loss or huge reflection.

---

## Suggested Score Function

Implement a score function where lower is better.

Example:

```text
score =
    10.0 * worstS11Penalty
  +  5.0 * averageS11Penalty
  +  5.0 * worstS21Penalty
  +  1.0 * ripplePenalty
  +  0.5 * nonSmoothGradientPenalty
```

### worstS11Penalty

For each frequency point:

```text
if S11_dB > targetS11_dB:
    penalty += (S11_dB - targetS11_dB)^2
```

Example:

```text
targetS11_dB = -15
```

Since -5 dB is worse than -15 dB, it produces penalty.

### averageS11Penalty

Average reflected power or average S11 dB penalty across all points.

### worstS21Penalty

For each frequency point:

```text
if S21_dB < targetS21_dB:
    penalty += (targetS21_dB - S21_dB)^2
```

Example:

```text
targetS21_dB = -3
```

Since -6 dB is worse than -3 dB, it produces penalty.

### ripplePenalty

Penalize large ripple in S21:

```text
ripple = max(S21_dB) - min(S21_dB)
```

### nonSmoothGradientPenalty

For gradient structures, prefer smooth component profiles.

Penalty can be based on normalized difference between adjacent values.

---

## Optimization Algorithms

Implement at least two algorithms.

### Algorithm 1 — Sequential Local Search

This is the first required method.

Process:

1. Start with an initial structure.
2. Compute score.
3. For each component position:
   - Try replacing it with each available value of the same type.
   - Compute new score.
   - Keep the change if score improves.
4. Repeat passes until:
   - no improvement occurs
   - max iterations reached

Pseudo-code:

```csharp
current = initialStructure;
currentScore = Score(current);

for iteration in 0..MaxIterations:
    improved = false;

    for index in current.Components:
        original = current[index];

        foreach candidateValue in LibraryFor(original.Type):
            test = current.Clone();
            test[index].Value = candidateValue;

            testScore = Score(test);

            if testScore < currentScore:
                current = test;
                currentScore = testScore;
                improved = true;

    if !improved:
        break;
```

### Algorithm 2 — Random Restart Local Search

This avoids being trapped in poor local minima.

Process:

1. Generate random structure using available L/C values.
2. Run Sequential Local Search.
3. Store best result.
4. Repeat N times.
5. Return top candidates.

Parameters:

```text
Random restarts: 100
Max local iterations: 50
Keep top results: 20
```

### Optional Algorithm 3 — Simulated Annealing

This can be added later.

Purpose:
- allow occasional worse moves
- escape local minima

Not required for the first version.

---

## Initial Structures / Presets

### Preset 1 — Sharp Structure

```text
220nH - 100pF - 220nH - 100pF - 220nH
```

JSON:

```json
[
  { "Type": "SeriesL", "Value": 220e-9 },
  { "Type": "ShuntC",  "Value": 100e-12 },
  { "Type": "SeriesL", "Value": 220e-9 },
  { "Type": "ShuntC",  "Value": 100e-12 },
  { "Type": "SeriesL", "Value": 220e-9 }
]
```

### Preset 2 — Gradient Structure

```text
470nH - 47pF - 330nH - 68pF - 220nH - 100pF - 330nH - 68pF - 470nH
```

JSON:

```json
[
  { "Type": "SeriesL", "Value": 470e-9 },
  { "Type": "ShuntC",  "Value": 47e-12 },
  { "Type": "SeriesL", "Value": 330e-9 },
  { "Type": "ShuntC",  "Value": 68e-12 },
  { "Type": "SeriesL", "Value": 220e-9 },
  { "Type": "ShuntC",  "Value": 100e-12 },
  { "Type": "SeriesL", "Value": 330e-9 },
  { "Type": "ShuntC",  "Value": 68e-12 },
  { "Type": "SeriesL", "Value": 470e-9 }
]
```

---

## Optimizer UI Requirements

Add an optimizer panel.

### Inputs

- Optimization start frequency MHz
- Optimization stop frequency MHz
- Number of sweep points
- Target S11 dB
- Minimum acceptable S21 dB
- Structure length:
  - 3L/2C
  - 5L/4C
  - 7L/6C
  - 9L/8C
- Optimization method:
  - Sequential Local Search
  - Random Restart Local Search
- Number of random restarts
- Max iterations

### Buttons

Required:

- Optimize Current Structure
- Random Search
- Stop Optimization
- Apply Best Result
- Export Best Result JSON
- Save Candidate List CSV

### Output Table

Show top candidates:

| Rank | Score | Worst S11 dB | Avg S11 dB | Worst S21 dB | Ripple dB | Structure |
|---|---:|---:|---:|---:|---:|---|

Clicking a candidate should display its graphs.

---

## Data Export

### Export Best JSON

Export candidate structure in the same format currently used by the simulator.

### Export CSV

CSV should contain:

```text
Rank,Score,WorstS11dB,AverageS11dB,WorstS21dB,RippledB,StructureJson
```

---

## Validation Plan

After implementing optimizer:

1. Load Sharp Structure.
2. Simulate and compare to current known graph.
3. Load Gradient Structure.
4. Simulate and compare to current known graph.
5. Run optimizer.
6. Verify proposed best structure has:
   - better S11 over selected band
   - acceptable S21
7. Build optimized structure physically.
8. Compare NanoVNA result to software result.

---

## Important Engineering Notes

### Ideal Model Limitation

The first version uses ideal L and C elements.

Real measurements may differ due to:

- ESR
- ESL
- inductor self-resonance
- capacitor parasitics
- PCB trace inductance
- ground inductance
- SMA launch parasitics
- coupling between adjacent components

This is expected.

### Future Parasitic Model

Later add optional parasitic model:

#### Inductor Model

```text
Series L + ESR + parallel Cp
```

#### Capacitor Model

```text
Series ESL + ESR + C
```

Default parasitics can be disabled.

---

## Definition of Success

The optimizer is successful if it can find structures that are better than the manually created Sharp and Gradient structures according to selected score targets.

Primary success metric:

```text
Worst-case S11 improves over 5 MHz - 50 MHz
while S21 remains above the selected threshold.
```

Example desired result:

```text
S11 < -10 dB over most of the band
S21 > -3 dB over most of the band
```

Better target:

```text
S11 < -15 dB over most of the band
S21 > -3 dB over most of the band
```

---

## Development Priority

Implement in this order:

1. Component libraries
2. Score calculation
3. Sequential Local Search
4. Top candidate table
5. Apply candidate to existing plot system
6. Random Restart Local Search
7. CSV export
8. Optional phase/group delay scoring
9. Optional parasitic models

---

## Final Goal

The software should become a practical RF design tool for finding solderable LC artificial-media structures using the real available component kit.

It should help answer:

> Which LC gradient structure gives the best match and transmission before we solder it?
