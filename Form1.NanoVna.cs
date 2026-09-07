using System.IO.Ports;
using System.Text;

namespace RfSimulator;

public partial class Form1
{
    private CancellationTokenSource? _nanoCancellation;
    private NanoVnaSerial.Session? _nanoSession;
    private SaverCalibration? _selectedCalibration;
    private readonly Label _calibrationStatus = new() { AutoSize = true, Text = "Software calibration: OFF" };

    private void SelectHardwareCalibration()
    {
        if (_nanoCancellation is not null) { MessageBox.Show(this, "Wait for the current operation to finish."); return; }
        using var dialog = new OpenFileDialog { Filter = "Saver calibration (*.cal)|*.cal" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var calibration = new SaverCalibration(dialog.FileName);
            _selectedCalibration = calibration;
            _calibrationStatus.Text = $"Auto CAL: {Path.GetFileName(calibration.Source)} | {calibration.MinimumHz / 1e6:g}–{calibration.MaximumHz / 1e6:g} MHz | ideal standards; matching cables/device state required";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Calibration load failed"); }
    }

    private Control BuildNanoVnaPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        var refresh = new Button { Text = "Ports", AutoSize = true };
        var probe = new Button { Text = "Open device", AutoSize = true };
        var acquire = new Button { Text = "Measure", AutoSize = true, Enabled = false };
        var cancel = new Button { Text = "Close device", AutoSize = true, Enabled = false };
        var save = new Button { Text = "Save measured S2P", AutoSize = true };
        var status = new Label { AutoSize = true, Text = "Disconnected — port free. Close Saver before opening." };
        void UpdateButtons()
        {
            var busy = _nanoCancellation is not null;
            var connected = _nanoSession is not null;
            ports.Enabled = refresh.Enabled = probe.Enabled = !busy && !connected;
            acquire.Enabled = !busy && connected;
            cancel.Enabled = busy || connected;
        }
        void Disconnect()
        {
            if (_nanoCancellation is not null)
            {
                _nanoCancellation.Cancel();
                status.Text = "Closing — waiting for current serial operation to stop…";
                cancel.Enabled = false;
                return;
            }
            _nanoSession?.Dispose(); _nanoSession = null;
            status.Text = "Disconnected — port released. Saver can open it.";
            UpdateButtons();
        }
        void RefreshPorts()
        {
            try
            {
                var selected = ports.SelectedItem as string;
                ports.Items.Clear(); ports.Items.AddRange(SerialPort.GetPortNames().OrderBy(name => name).ToArray());
                if (selected is not null && ports.Items.Contains(selected)) ports.SelectedItem = selected;
                else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        async Task Run(bool measure)
        {
            if (_nanoCancellation is not null) return;
            if (ports.SelectedItem is not string name) { status.Text = "Select a COM port."; return; }
            var frequency = ReadFrequency();
            var calibration = _selectedCalibration;
            try
            {
                if (measure)
                {
                    if (_nanoSession is null) throw new InvalidOperationException("Open device first.");
                    NanoVnaSerial.ValidateSweep(frequency);
                    if (calibration is not null && (frequency.StartHz < calibration.MinimumHz || frequency.StopHz > calibration.MaximumHz))
                        throw new InvalidOperationException("Sweep exceeds loaded calibration range. Adjust shared frequencies or load another calibration.");
                }
            }
            catch (Exception ex) { status.Text = ex.Message; return; }
            if (measure && MessageBox.Show(this,
                $"Acquire {frequency.StartMHz:g}–{frequency.StopMHz:g} MHz, {frequency.Points} points?\nThe F V2 will generate an RF sweep and its active sweep range may change. Calibration is not verified. No calibration/save/reset commands are sent. Confirm the connected device is NanoVNA-F V2 and the board has no external RF source attached.",
                "Start hardware measurement", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            using var cancellation = new CancellationTokenSource();
            _nanoCancellation = cancellation;
            UpdateButtons();
            status.Text = measure ? "Measuring…" : "Opening selected port…";
            try
            {
                if (measure)
                {
                    var progress = new Progress<string>(message => { if (!IsDisposed && _nanoCancellation == cancellation && !cancellation.IsCancellationRequested) status.Text = message; });
                    var session = _nanoSession!;
                    var measured = await Task.Run(() => session.Measure(frequency, cancellation.Token, progress));
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (IsDisposed) return;
                    var corrected = calibration?.Apply(measured);
                    _measurement = corrected ?? measured;
                    _beforeCalibration = corrected is null ? null : measured;
                    ShowMeasurement(false);
                    status.Text = $"Connected: {session.Description}. Received {measured.Points.Count} points; {(calibration is null ? "UNCORRECTED" : "software CAL applied")}. Port remains open.";
                }
                else
                {
                    var session = await Task.Run(() => new NanoVnaSerial.Session(name, cancellation.Token));
                    if (IsDisposed || cancellation.IsCancellationRequested) { session.Dispose(); cancellation.Token.ThrowIfCancellationRequested(); return; }
                    _nanoSession = session;
                    status.Text = $"Connected: {session.Description}. Port held until Close device.";
                }
            }
            catch (OperationCanceledException)
            {
                _nanoSession?.Dispose(); _nanoSession = null;
                if (!IsDisposed) status.Text = "Disconnected — port released. Previous measurement retained; device may finish its scan.";
            }
            catch (Exception ex)
            {
                _nanoSession?.Dispose(); _nanoSession = null;
                if (!IsDisposed) status.Text = $"Disconnected — port released. {ex.Message} Previous measurement retained.";
            }
            finally
            {
                _nanoCancellation = null;
                if (IsDisposed) { _nanoSession?.Dispose(); _nanoSession = null; }
                else UpdateButtons();
            }
            if (!measure && !IsDisposed && _nanoSession is not null && _selectedCalibration is null &&
                MessageBox.Show(this, "Load a Saver .cal file for automatic correction of every new hardware scan?", "Software calibration", MessageBoxButtons.YesNo) == DialogResult.Yes)
                SelectHardwareCalibration();
        }
        refresh.Click += (_, _) => RefreshPorts();
        probe.Click += async (_, _) => await Run(false);
        acquire.Click += async (_, _) => await Run(true);
        cancel.Click += (_, _) => Disconnect();
        FormClosing += (_, _) => Disconnect();
        Disposed += (_, _) =>
        {
            if (_nanoCancellation is not null) _nanoCancellation.Cancel();
            else { _nanoSession?.Dispose(); _nanoSession = null; }
        };
        save.Click += (_, _) =>
        {
            if (_measurement is not { } measured) { status.Text = "No measurement to save."; return; }
            using var dialog = new SaveFileDialog { Filter = "Touchstone (*.s2p)|*.s2p", FileName = $"measured-{DateTime.Now:yyyyMMdd-HHmmss}.s2p" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var text = new StringBuilder("! Measured S11/S21; S12/S22 are placeholders, NOT measurements. Calibration unverified.\n");
                text.AppendLine("! " + measured.Source.Replace('\r', ' ').Replace('\n', ' '));
                text.AppendLine(FormattableString.Invariant($"# HZ S RI R {measured.ReferenceOhms:R}"));
                foreach (var point in measured.Points)
                    text.AppendLine(FormattableString.Invariant($"{point.FrequencyHz:R} {point.S11.Real:R} {point.S11.Imaginary:R} {point.S21.Real:R} {point.S21.Imaginary:R} 0 0 0 0"));
                File.WriteAllText(dialog.FileName, text.ToString());
                status.Text = "Saved measured S2P.";
            }
            catch (Exception ex) { status.Text = "Save failed: " + ex.Message; }
        };
        panel.Controls.AddRange(new Control[] { ports, refresh, probe, acquire, cancel, save, status, _calibrationStatus });
        RefreshPorts();
        return panel;
    }
}
