Add optional continuous-value optimization mode.

Instead of selecting only from available discrete L/C values, allow L and C values to vary continuously within user-defined ranges.

Purpose:
This mode is for physics exploration and proof-of-principle, not final buildable BOM.

Requirements:
- Add checkbox: Use continuous L/C values.
- Add ranges:
  L min/max in nH
  C min/max in pF
- Allow optimizer to generate arbitrary values within those ranges.
- Add optional smooth-gradient constraint:
  values should generally increase toward antenna side.
- Penalize abrupt jumps between adjacent cells.
- Keep discrete-value mode unchanged.
- Display resulting values rounded in nH/pF.
- Allow exporting JSON with exact values.