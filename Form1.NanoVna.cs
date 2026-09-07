using System.IO.Ports;
using System.Text;

namespace RfSimulator;

public partial class Form1
{
    private CancellationTokenSource? _nanoCancellation;

    private Control BuildNanoVnaPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var ports = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        var refresh = new Button { Text = "Ports", AutoSize = true };
        var probe = new Button { Text = "Check F V2", AutoSize = true };
        var acquire = new Button { Text = "Measure sweep", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        var save = new Button { Text = "Save measured S2P", AutoSize = true };
        var status = new Label { AutoSize = true, Text = "F V2 text-shell profile. Close Saver first. Calibration unverified." };
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
            try { if (measure) NanoVnaSerial.ValidateSweep(frequency); }
            catch (Exception ex) { status.Text = ex.Message; return; }
            if (measure && MessageBox.Show(this,
                $"Acquire {frequency.StartMHz:g}–{frequency.StopMHz:g} MHz, {frequency.Points} points?\nThe F V2 will generate an RF sweep and its active sweep range may change. Calibration is not verified. No calibration/save/reset commands are sent. Confirm the connected device is NanoVNA-F V2 and the board has no external RF source attached.",
                "Start hardware measurement", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            using var cancellation = new CancellationTokenSource();
            _nanoCancellation = cancellation;
            ports.Enabled = refresh.Enabled = probe.Enabled = acquire.Enabled = false;
            cancel.Enabled = true;
            status.Text = "Opening selected port…";
            try
            {
                if (measure)
                {
                    var progress = new Progress<string>(message => { if (!IsDisposed) status.Text = message; });
                    var measured = await Task.Run(() => NanoVnaSerial.Acquire(name, frequency, cancellation.Token, progress));
                    if (IsDisposed) return;
                    _measurement = measured;
                    ShowMeasurement(false);
                    status.Text = $"Received {measured.Points.Count} points. Port released; calibration unverified. Save measured S2P to keep this scan.";
                }
                else
                {
                    var message = await Task.Run(() => NanoVnaSerial.Probe(name, cancellation.Token));
                    if (!IsDisposed) status.Text = message;
                }
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Cancelled; previous measurement retained. Port released; device may finish its current scan."; }
            catch (Exception ex) { if (!IsDisposed) status.Text = $"Failed: {ex.Message} Previous measurement retained."; }
            finally
            {
                _nanoCancellation = null;
                if (!IsDisposed) { ports.Enabled = refresh.Enabled = probe.Enabled = acquire.Enabled = true; cancel.Enabled = false; }
            }
        }
        refresh.Click += (_, _) => RefreshPorts();
        probe.Click += async (_, _) => await Run(false);
        acquire.Click += async (_, _) => await Run(true);
        cancel.Click += (_, _) => _nanoCancellation?.Cancel();
        FormClosing += (_, _) => _nanoCancellation?.Cancel();
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
        panel.Controls.AddRange(new Control[] { ports, refresh, probe, acquire, cancel, save, status });
        RefreshPorts();
        return panel;
    }
}
