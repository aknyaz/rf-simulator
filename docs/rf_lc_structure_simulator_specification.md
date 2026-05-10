# RF / Metamaterial LC Structure Simulator

## Project Goal

Create a C# application for modeling artificial LC transmission structures and metamaterial-like ladder networks.

The software should:

- Simulate LC ladder structures
- Calculate S-parameters
- Display S11 and S21 responses
- Display Smith chart
- Display Polar plot
- Compare simulated results with real NanoVNA measurements
- Allow iterative optimization of matching structures

---

# Main Concept

The software models a structure as a sequence of distributed RF sections.

Example:

L - C - L - C - L

Where:
- Series inductors represent distributed inductance
- Shunt capacitors represent distributed capacitance

This structure behaves similarly to a discretized transmission line.

---

# Frequency Range

Default simulation range:
1 MHz - 50 MHz

Sweep points:
201 or 401

Sweep type:
Linear

---

# Supported Components

## Series Inductor

Equation:
Z_L = jωL

ABCD Matrix:

| 1  jωL |
| 0   1  |

---

## Shunt Capacitor

Equation:
Y_C = jωC

ABCD Matrix:

| 1   0 |
| jωC 1 |

---

# Structure Representation

Example JSON representation:

[
  { "type": "SeriesL", "value": 220e-9 },
  { "type": "ShuntC", "value": 100e-12 },
  { "type": "SeriesL", "value": 220e-9 },
  { "type": "ShuntC", "value": 100e-12 },
  { "type": "SeriesL", "value": 220e-9 }
]

---

# Solver Architecture

For every frequency point:

1. Compute element impedances/admittances
2. Build ABCD matrix for each section
3. Multiply all matrices in order
4. Obtain total ABCD matrix
5. Convert ABCD matrix to S-parameters
6. Plot graphs

---

# ABCD to S-Parameter Conversion

Assume:
Z0 = 50 Ohm

S11 = (A + B/Z0 - CZ0 - D) / (A + B/Z0 + CZ0 + D)

S21 = 2 / (A + B/Z0 + CZ0 + D)

---

# Graphs Required

## S11 Return Loss

20*log10(|S11|)

## S21 Transmission

20*log10(|S21|)

## Smith Chart

S11 complex trajectory

## Polar Plot

S21 complex trajectory

---

# Optimization Method

Iterative sequential approximation

Algorithm:
1. Modify one component value
2. Recalculate response
3. Compare score
4. Keep improvement
5. Repeat

---

# Initial Component Library

## Inductors

10 nH
47 nH
68 nH
100 nH
220 nH
330 nH
470 nH
680 nH
820 nH

---

## Capacitors

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

---

# Validation Structures

## Validation #1 — Through Connection

Expected:
- S11 very low
- S21 near 0 dB
- Smith chart centered

---

## Validation #2 — Sharp Structure

220nH - 100pF - 220nH - 100pF - 220nH

---

## Validation #3 — Gradient Structure

470nH - 47pF
330nH - 68pF
220nH - 100pF
330nH - 68pF
470nH - 47pF

---

# Recommended Technologies

Language:
C#

Math:
System.Numerics.Complex

---

# Engineering Goal Summary

The goal is to investigate:

- artificial propagation media
- distributed RF structures
- impedance transformation
- slow-wave behavior
- phase delay in electrically small structures

using experimentally verifiable LC ladder networks.
