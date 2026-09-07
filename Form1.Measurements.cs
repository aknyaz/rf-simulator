using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace RfSimulator;

public partial class Form1
{
    private MeasuredSweep? _measurement;
    private MeasuredSweep? _beforeCalibration;
    private readonly TextBox _measurementInfo = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly PlotView[] _measurementPlots = Enumerable.Range(0, 4).Select(_ => new PlotView { Dock = DockStyle.Fill }).ToArray();

    private Control BuildMeasurementsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var import = new Button { Text = "Import measured S2P", AutoSize = true };
        var compare = new Button { Text = "Compare with current model snapshot", AutoSize = true };
        var calibrate = new Button { Text = "Load calibration…", AutoSize = true };
        var restore = new Button { Text = "Undo CAL", AutoSize = true };
        calibrate.Click += (_, _) => SelectHardwareCalibration();
        restore.Click += (_, _) =>
        {
            if (_nanoCancellation is not null) return;
            if (_beforeCalibration is null) { MessageBox.Show(this, "No uncorrected hardware scan available."); return; }
            _measurement = _beforeCalibration; _beforeCalibration = null; ShowMeasurement(false);
        };
        import.Click += (_, _) =>
        {
            if (_nanoCancellation is not null) { MessageBox.Show(this, "Wait for acquisition to finish."); return; }
            using var dialog = new OpenFileDialog { Filter = "Touchstone two-port (*.s2p)|*.s2p" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { _measurement = TouchstoneImporter.Read(dialog.FileName); _beforeCalibration = null; ShowMeasurement(false); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Measurement import failed"); }
        };
        compare.Click += (_, _) =>
        {
            try { ShowMeasurement(true); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Measurement comparison failed"); }
        };
        var extras = new Button { Text = "Files / advanced…", AutoSize = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Import measured S2P…", null, (_, _) => import.PerformClick());
        menu.Items.Add("Show uncorrected last scan", null, (_, _) => restore.PerformClick());
        menu.Items.Add("Disable CAL for future scans", null, (_, _) =>
        {
            if (_nanoCancellation is not null) return;
            _selectedCalibration = null;
            _calibrationStatus.Text = "Software calibration: OFF (existing measurement unchanged)";
        });
        extras.Click += (_, _) => menu.Show(extras, new System.Drawing.Point(0, extras.Height));
        Disposed += (_, _) => { menu.Dispose(); import.Dispose(); restore.Dispose(); };
        buttons.Controls.AddRange(new Control[] { calibrate, compare, extras });
        panel.Controls.Add(buttons, 0, 0); panel.SetColumnSpan(buttons, 2);
        var hardware = BuildNanoVnaPanel();
        panel.Controls.Add(hardware, 0, 1); panel.SetColumnSpan(hardware, 2);
        panel.Controls.Add(_measurementInfo, 0, 2); panel.SetColumnSpan(_measurementInfo, 2);
        for (var index = 0; index < 4; index++) panel.Controls.Add(_measurementPlots[index], index % 2, 3 + index / 2);
        _measurementInfo.Text = "Import a measured S2P file. Circuit and shared simulation settings remain unchanged. No instrument connection is opened.";
        return panel;
    }

    private void ShowMeasurement(bool compare)
    {
        if (_measurement is not { } measurement) throw new InvalidOperationException("Import a measurement first.");
        var points = measurement.Points;
        IReadOnlyList<SweepPoint>? model = null;
        var modelDescription = "No model overlay.";
        if (compare)
        {
            var structure = SimulationEngine.ParseStructure(_structureInput.Text);
            model = points.Select(point => SimulationEngine.Simulate(point.FrequencyHz, point.FrequencyHz + 1, 2, measurement.ReferenceOhms, structure)[0]).ToList();
            modelDescription = $"Model snapshot captured {DateTime.Now:HH:mm:ss}: {structure.Count} elements, evaluated at measured frequencies and R={measurement.ReferenceOhms:g} ohm. Click Compare again after edits.";
        }
        var phase = RfMath.GetUnwrappedS21PhaseRadians(points);
        var average = -(phase[^1] - phase[0]) / (2 * Math.PI * (points[^1].FrequencyHz - points[0].FrequencyHz)) * 1e9;
        _measurementInfo.Text = $"{measurement.Source}\r\n{points.Count} points | {points[0].FrequencyHz / 1e6:g}–{points[^1].FrequencyHz / 1e6:g} MHz | R={measurement.ReferenceOhms:g} ohm | Band-integrated mean delay: {average:F3} ns\r\nMeasured = solid gold; model snapshot = dashed blue. {modelDescription}\r\nS12/S22 are not used or certified as measured. Calibration is unknown. Delay is unsmoothed; derivatives amplify noise, endpoints are less accurate, and transmission nulls/coarse sampling can make delay unreliable.";
        var titles = new[] { "S11 magnitude", "S21 magnitude", "S21 unwrapped phase", "S21 group delay" };
        var units = new[] { "dB", "dB", "degrees", "ns" };
        for (var index = 0; index < 4; index++)
        {
            var plot = new PlotModel { Title = titles[index] };
            plot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Frequency (MHz)" });
            plot.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = units[index] });
            AddMeasurementTrace(plot, points, index, false);
            if (model is not null) AddMeasurementTrace(plot, model, index, true);
            _measurementPlots[index].Model = plot;
        }
    }

    private static void AddMeasurementTrace(PlotModel plot, IReadOnlyList<SweepPoint> points, int kind, bool model)
    {
        var values = kind switch
        {
            0 => points.Select(point => RfMath.ToDbMagnitude(point.S11)).ToList(),
            1 => points.Select(point => RfMath.ToDbMagnitude(point.S21)).ToList(),
            2 => RfMath.GetUnwrappedS21PhaseRadians(points).Select(value => value * 180 / Math.PI).ToList(),
            _ => RfMath.CalculateGroupDelayNs(points)
        };
        var trace = new LineSeries { Title = model ? "Model snapshot" : "Measured", Color = model ? OxyColors.DodgerBlue : OxyColors.DarkGoldenrod, LineStyle = model ? LineStyle.Dash : LineStyle.Solid };
        for (var index = 0; index < points.Count; index++)
            trace.Points.Add(new DataPoint(points[index].FrequencyHz / 1e6, kind >= 2 && points[index].S21.Magnitude < 1e-6 ? double.NaN : values[index]));
        plot.Series.Add(trace);
    }
}
