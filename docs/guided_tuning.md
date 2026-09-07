# Guided tuning, local file protocol v1

## Active result waiting

After submitting, the assistant must call `--tuning-wait` with the returned ID and keep its tool operation active. It returns the terminal result JSON without another user message. Default timeout is 600 seconds, maximum 3600; timeout does not cancel the measurement and the same ID can be waited on again. Ctrl+C stops only the observer. The GUI displays whether a recent observer heartbeat exists, not whether an AI has actually analyzed the result. Heartbeats expire after three seconds if the observer exits unexpectedly. This is active local waiting, not automatic chat wakeup after the assistant ends its turn. Stopped/crashed sessions without terminal results eventually time out.

Open the NanoVNA and load calibration in Measurements, then Start guided session. Choose a parent directory: the GUI creates and displays a unique session folder. Starting requires confirmation that Done triggers RF acquisition. Start does not adjust any component or run a scan. Instructions are plain text, never executable code.

Send one instruction from the CLI:

```
rf_sim_cli --tuning-submit SESSION_FOLDER "Baseline: leave all components unchanged; remove tool and click Done."
rf_sim_cli --tuning-status SESSION_FOLDER STEP_ID
rf_sim_cli --tuning-wait SESSION_FOLDER STEP_ID 600
```

Submit returns the new step ID. The GUI polls only while the user-enabled session is active. It displays the instruction and waits for Done — measure, Skip, or Stop session. Done uses the shared settings captured at click time and the selected calibration; acquisition and calibration coverage are validated before starting. Hardware scans use the same exclusively owned serial session as manual measurement. No second COM connection is opened.

Each step has an exclusive instruction.json claim and result.json state (awaiting_user, measuring, complete, skipped, cancelled, failed, stopped). Complete publishes only after raw.s2p and corrected.s2p have been saved without overwriting. Results include frequency configuration, calibration source, device description, and basic metrics. The external assistant reads files, compares steps, and explicitly submits the next instruction. No autonomous tuning decisions or ChatGPT connection is implied. Results are not evidence of improvement without a baseline and repeatability analysis.

There is one pending command.json. Submission is atomic and refuses to replace it. IDs are GUIDs; replaying an already claimed ID halts the session rather than rescanning. Double-clicking Done cannot start a second scan. Skipped/stopped steps do not measure. Close device during measurement cancels, releases the port, and retains the prior measurement. Stop prevents further polling; restart creates a NEW session folder. On crash, incomplete step files remain for diagnosis; there is no automatic resume/retry. Treat measuring without an active GUI as interrupted, not successful. Only local trusted software should write into the chosen folder.

This first version saves each result for external analysis, not automatic numeric before/after scoring. Physical repeatability and guided serial cancellation must still be verified on the real device; automated tests exercise the file protocol and existing numerical/UI checks.
