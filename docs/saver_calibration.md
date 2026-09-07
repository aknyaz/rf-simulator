# Saver software calibration

Measurements → Load calibration loads a 13-column NanoVNA-Saver calibration before acquisition. Every subsequent hardware scan is automatically corrected once with the selected calibration. Open device offers calibration selection if none is loaded. Coverage is checked before scanning. Imported S2P files are not automatically corrected. Files / advanced exposes the uncorrected last hardware scan and disabling correction for future scans. Changing calibration selection does not modify an existing measurement. No calibration commands are sent to the instrument.

This implementation assumes ideal short/open/50-ohm load and zero-delay through standards. Nonideal standard settings from Saver are not stored in this basic .cal table and are not inferred. Calibration is only meaningful with the same measurement cables, reference planes, and device calibration state. Do not correct already-corrected Saver exports. Application is guarded by a source marker, preserved in our measured exports; external files may not disclose their correction state, so user confirmation is still required.

Error terms are solved from the measured SOL/through/isolation standards, then interpolated in complex Cartesian form before forward S11/S21 correction. Only the covered range is allowed, with a 1 kHz upper endpoint rounding allowance for Saver segmented exports. Reverse S parameters remain unavailable. The mathematical convention was checked against NanoVNA-Saver Hardware/Calibration.py's equivalent forward error model (actual module: src/NanoVNASaver/Calibration.py).

Validation using calibration2.cal on the uncorrected direct-through export gave S21 -0.056 dB at 5 MHz and -0.067 dB at 50 MHz, versus -0.056 and -0.076 dB in the separately acquired corrected Saver export. These are different acquisitions, not an exact numerical equivalence test. Existing tests and an ideal-standard identity/double-application test also pass.

CLI: `--inspect-s2p FILE [SAVER_CAL_FILE]`. Input files are read-only.
