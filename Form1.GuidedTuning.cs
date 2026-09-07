using System.Text.Json;

namespace RfSimulator;

public partial class Form1
{
    private string? _tuningDirectory;
    private string? _tuningStep;
    private TuningInstruction? _tuningInstruction;
    private bool _tuningStopped;

    private Control BuildGuidedTuningPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var start = new Button { Text = "Start guided session…", AutoSize = true };
        var done = new Button { Text = "Done — measure", AutoSize = true, Enabled = false };
        var skip = new Button { Text = "Skip", AutoSize = true, Enabled = false };
        var stop = new Button { Text = "Stop session", AutoSize = true, Enabled = false };
        var observerStatus = new Label { AutoSize = true, Text = "Result observer: not connected (does not wake a chat automatically)." };
        var instruction = new TextBox { ReadOnly = true, Multiline = true, Width = 850, Height = 55, Text = "Guided tuning: start a session to receive local instructions. Measurement requires your Done click." };
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        void Finish(string state, string detail, object? metadata = null)
        {
            if (_tuningStep is not null) TuningExchange.Result(_tuningStep, state, detail, metadata);
            if (_tuningDirectory is not null && TuningExchange.Read(_tuningDirectory)?.Id == _tuningInstruction?.Id)
                File.Delete(Path.Combine(_tuningDirectory, "command.json"));
            _tuningStep = null; _tuningInstruction = null;
            done.Enabled = skip.Enabled = false;
        }
        void Halt()
        {
            timer.Stop(); _tuningStopped = true; done.Enabled = skip.Enabled = false;
            if (_nanoCancellation is not null && _tuningInstruction is not null) _nanoCancellation.Cancel();
            else
            {
                if (_tuningInstruction is not null) Finish("stopped", "Stopped by user; no measurement performed.");
                start.Enabled = true;
            }
            stop.Enabled = false;
            instruction.Text = "Session stopped. No further instructions will execute.";
        }
        start.Click += (_, _) =>
        {
            if (_nanoCancellation is not null) return;
            using var dialog = new FolderBrowserDialog { Description = "Choose parent folder for a NEW tuning session" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, "Each Done click will generate an RF sweep using the shared frequency settings and loaded calibration. Confirm F V2, passive board with no external RF source, and matching calibration cables. Instructions are plain text only; Stop prevents further steps.", "Enable guided measurements", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            try
            {
                _tuningDirectory = Path.Combine(dialog.SelectedPath, "tuning-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tuningDirectory);
                File.WriteAllText(Path.Combine(_tuningDirectory, "session.json"), JsonSerializer.Serialize(new { created = DateTimeOffset.Now, protocol = 1, description = "Local instructions only; no automatic physical tuning." }));
                _tuningStopped = false; _tuningInstruction = null; _tuningStep = null;
                start.Enabled = false; stop.Enabled = true;
                instruction.Text = "Waiting for instruction. Session folder:\r\n" + _tuningDirectory;
                timer.Start();
            }
            catch (Exception ex) { instruction.Text = ex.Message; }
        };
        timer.Tick += (_, _) =>
        {
            if (_tuningDirectory is not null)
            {
                try { observerStatus.Text = TuningExchange.HasObserver(_tuningDirectory, _tuningInstruction?.Id)
                    ? "Result observer: actively waiting for completion."
                    : "Result observer: not waiting. Files are saved, but automatic analysis is not active."; }
                catch (IOException) { observerStatus.Text = "Result observer: session folder unavailable."; }
            }
            if (_tuningDirectory is null || _tuningInstruction is not null || _nanoCancellation is not null) return;
            try
            {
                var command = TuningExchange.Read(_tuningDirectory);
                if (command is null) return;
                var step = TuningExchange.Claim(_tuningDirectory, command);
                _tuningInstruction = command; _tuningStep = step;
                TuningExchange.Result(step, "awaiting_user", command.Message);
                instruction.Text = command.Message + "\r\nRemove hand/tool, then click Done — measure. Step: " + command.Id;
                done.Enabled = skip.Enabled = true;
            }
            catch (Exception ex) { timer.Stop(); instruction.Text = "Session halted: " + ex.Message; done.Enabled = skip.Enabled = false; }
        };
        skip.Click += (_, _) =>
        {
            try { Finish("skipped", "User skipped instruction; no measurement performed."); instruction.Text = "Skipped. Waiting for next instruction."; }
            catch (Exception ex) { timer.Stop(); instruction.Text = ex.Message; }
        };
        stop.Click += (_, _) =>
        {
            try { Halt(); } catch (Exception ex) { instruction.Text = ex.Message; }
        };
        done.Click += async (_, _) =>
        {
            if (_tuningInstruction is null || _tuningStep is null || _tuningStopped || _nanoCancellation is not null) return;
            var session = _nanoSession;
            var calibration = _selectedCalibration;
            var frequency = ReadFrequency();
            try
            {
                if (session is null) throw new InvalidOperationException("Open device first.");
                if (calibration is null) throw new InvalidOperationException("Load calibration before guided tuning.");
                NanoVnaSerial.ValidateSweep(frequency);
                if (frequency.StartHz < calibration.MinimumHz || frequency.StopHz > calibration.MaximumHz) throw new InvalidOperationException("Sweep exceeds calibration coverage.");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); return; }
            var step = _tuningStep;
            done.Enabled = skip.Enabled = false;
            using var cancellation = new CancellationTokenSource();
            _nanoCancellation = cancellation;
            _refreshNanoControls?.Invoke();
            try
            {
                TuningExchange.Result(step, "measuring", "User confirmed adjustment. RF acquisition in progress.");
                var raw = await Task.Run(() => session.Measure(frequency, cancellation.Token, null));
                cancellation.Token.ThrowIfCancellationRequested();
                TuningExchange.SaveSweep(Path.Combine(step, "raw.s2p"), raw);
                var corrected = calibration.Apply(raw);
                TuningExchange.SaveSweep(Path.Combine(step, "corrected.s2p"), corrected);
                var phase = RfMath.GetUnwrappedS21PhaseRadians(corrected.Points);
                Finish("complete", "Measurement saved. Awaiting external analysis; no automatic recommendation.", new
                {
                    frequency, calibration = calibration.Source, device = session.Description,
                    rawFile = "raw.s2p", correctedFile = "corrected.s2p",
                    worstS11Db = corrected.Points.Max(point => RfMath.ToDbMagnitude(point.S11)),
                    worstS21Db = corrected.Points.Min(point => RfMath.ToDbMagnitude(point.S21)),
                    bandMeanDelayNs = -(phase[^1] - phase[0]) / (2 * Math.PI * (corrected.Points[^1].FrequencyHz - corrected.Points[0].FrequencyHz)) * 1e9
                });
                if (!IsDisposed) { _measurement = corrected; _beforeCalibration = raw; ShowMeasurement(false); instruction.Text = "Saved. Waiting for next instruction.\r\n" + step; }
            }
            catch (Exception ex)
            {
                session.Dispose(); _nanoSession = null;
                try { Finish(ex is OperationCanceledException ? "cancelled" : "failed", ex.Message); }
                catch { timer.Stop(); }
                if (!IsDisposed) instruction.Text = "Step ended: " + ex.Message + " Port released; reconnect before next measurement.";
            }
            finally
            {
                _nanoCancellation = null;
                if (IsDisposed) { session.Dispose(); _nanoSession = null; }
                else
                {
                    _refreshNanoControls?.Invoke();
                    if (_tuningStopped) start.Enabled = true;
                }
            }
        };
        FormClosing += (_, _) => { timer.Stop(); _tuningStopped = true; };
        Disposed += (_, _) => timer.Dispose();
        panel.Controls.AddRange(new Control[] { start, done, skip, stop, instruction, observerStatus });
        return panel;
    }
}
