Add a new “Propagation / Slow-Wave Analysis” mode to the RF LC Structure Simulator.

Goal:
The existing simulator shows only external two-port behavior: S11, S21, phase, and group delay.
Now we want to analyze what happens inside the LC ladder network at each node/cell.

Physics goal:
Show how the artificial LC medium slows the wave, accumulates phase, stores energy, and creates a shorter effective wavelength compared to free space.

Do not change existing S-parameter math, optimizer scoring, or Touchstone export.

New UI:
Add a new tab or panel called:
“Propagation Analysis”

This tab should appear after running a simulation for the current structure.

Required user input:
1. Cell spacing / physical distance per section
   - Default: 5 mm
   - Units: mm
   - This represents approximate physical distance between adjacent LC nodes on the PCB.

2. Analysis frequency
   - Default: center frequency of sweep, e.g. 27.5 MHz
   - User can select a frequency from the sweep or type manually.

Core calculation:
For the selected frequency, solve the internal LC ladder network and calculate complex node voltages and branch currents.

For each node/cell calculate:
1. Node voltage magnitude
2. Node voltage phase
3. Phase difference between adjacent nodes
4. Local beta:
   beta = -deltaPhase / cellSpacingMeters

5. Effective wavelength:
   lambda_eff = 2 * PI / beta

6. Phase velocity:
   v_phase = omega / beta

7. Slow-wave factor:
   slowWaveFactor = c / v_phase

8. Capacitor stored electric energy:
   E_C = 0.5 * C * |V|^2

9. Inductor stored magnetic energy:
   E_L = 0.5 * L * |I|^2

Important:
Use unwrapped node phase so phase does not jump at ±180 degrees.

Suggested graphs in the new tab:

Graph 1:
Node voltage magnitude vs node index

Graph 2:
Node voltage phase vs node index

Graph 3:
Local phase shift per cell vs cell index

Graph 4:
Slow-wave factor vs cell index

Graph 5:
Stored energy per element
- Capacitor electric energy
- Inductor magnetic energy

Also show a summary box:
- selected frequency
- total phase shift
- total group delay if available
- average slow-wave factor
- minimum / maximum slow-wave factor
- equivalent free-space length:
  L_eq = c * groupDelay
- physical structure length:
  L_phys = numberOfCells * cellSpacing
- compression ratio:
  L_eq / L_phys

Interpretation:
If slowWaveFactor > 1, the wave is slower than free space.
If slowWaveFactor is large and S21 loss is low, the structure behaves like a compact artificial slow-wave medium.

Implementation notes:
- Keep this separate from optimizer scoring for now.
- This is a diagnostic/physics visualization feature.
- The first version does not need to be perfect for arbitrary topology, but it should work for the current alternating SeriesL / ShuntC ladder structures.
- Show clear warnings if the structure topology is unsupported.
- Avoid changing existing plots or optimizer behavior.

Expected result:
We should be able to select a candidate such as:
100 nH - 51 pF - 100 nH - 51 pF - 220 nH - 51 pF - 100 nH

Then open Propagation Analysis and see:
- phase accumulating along the ladder,
- local slow-wave factor,
- energy stored in L and C elements,
- effective wavelength shorter than free-space wavelength.