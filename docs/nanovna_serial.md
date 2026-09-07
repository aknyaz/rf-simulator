# Experimental NanoVNA-F V2 acquisition

## Updated persistent connection workflow

The UI now uses Open device → Measure → Close device. Open verifies the shell and holds the serial port. Measure is disabled while disconnected. Close cancels in-flight work, waits for the bounded serial operation to stop, then disposes the port. During that interval the status says Closing, not Disconnected. Completion after cancellation does not publish a measurement. Errors close the session and retain previous data. Application shutdown also releases the session. Disconnect detection happens during I/O; there is no continuous background hardware polling.

Load calibration selects software correction before measurement; successful hardware scans use that captured calibration automatically. See saver_calibration.md. Open device offers to load calibration when none is selected. Imported measurements are never automatically corrected. The following paragraphs describe the original one-shot API, which is retained internally; its per-operation port release no longer describes the UI session.

Measurements now includes a COM selector, Ports refresh, Check F V2, Measure sweep, Cancel, and Save measured S2P. Close Saver before opening the port. The profile must be selected knowingly for the F V2, not the unrelated V2+/SAA binary protocol. Model identity is not automatically verified.

Check opens at 115200 baud, synchronizes the text shell, requests `version` and `help`, then releases the port. Acquisition requires advertised `scan` support. On user confirmation it issues 101-point `scan start stop 101` segments and reads `frequencies`, `data 0`, and `data 1`. Overlapping segment boundaries are removed. Requests support 101, 201, 301, 401, ... 10001 points, 50 kHz to 3 GHz, with 50-ohm reference. Returned grid and channel counts are checked before publishing the complete measurement. A failed/cancelled scan leaves the previous measurement intact.

No calibration, calibration save/recall, reset, firmware update, or transmit-power commands are sent. Calibration validity is UNKNOWN. Acquisition does generate RF and can leave the device at the final segment's sweep state; it does not restore/resume the previous sweep. Cancel closes the connection after the current bounded serial read; the instrument may continue its own scan. There is no automatic retry or background acquisition.

The implemented text command contract was checked against the upstream hardware adapters (September 7, 2026):
- https://github.com/NanoVNA-Saver/nanovna-saver/blob/main/src/NanoVNASaver/Hardware/NanoVNA_F_V2.py
- https://github.com/NanoVNA-Saver/nanovna-saver/blob/main/src/NanoVNASaver/Hardware/NanoVNA.py
- https://github.com/NanoVNA-Saver/nanovna-saver/blob/main/src/NanoVNASaver/Hardware/VNA.py
- https://github.com/NanoVNA-Saver/nanovna-saver/blob/main/src/NanoVNASaver/Hardware/Serial.py

This implementation is experimental: automated tests cover parsing, ranges, and UI construction, not the physical instrument or firmware-specific scan/calibration behavior. Validate the first acquisition against Saver before using it to guide tuning. Saved reverse parameters are explicitly marked placeholders. Acquisition and saving never update the simulation circuit.
