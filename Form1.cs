using System.Numerics;
using System.Text.Json;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace RfSimulator;

public partial class Form1 : Form
{
    private readonly NumericUpDown _startMHzInput = new();
    private readonly NumericUpDown _stopMHzInput = new();
    private readonly NumericUpDown _pointsInput = new();
    private readonly NumericUpDown _z0Input = new();
    private readonly ComboBox _presetCombo = new();
    private readonly TextBox _structureInput = new();
    private readonly Button _simulateButton = new();
    private readonly Button _saveJsonButton = new();
    private readonly Button _loadJsonButton = new();
    private readonly Label _statusLabel = new();

    private readonly PlotView _smithPlot = new();
    private readonly PlotView _s11DbPlot = new();
    private readonly PlotView _s21PolarPlot = new();
    private readonly PlotView _s21DbPlot = new();

    public Form1()
    {
        InitializeComponent();
        BuildUi();
        RunSimulation();
    }

    private void BuildUi()
    {
        Text = "RF LC Structure Simulator";
        WindowState = FormWindowState.Maximized;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var controlsPanel = BuildControlsPanel();
        var plotsGrid = BuildPlotsGrid();

        root.Controls.Add(controlsPanel, 0, 0);
        root.Controls.Add(plotsGrid, 0, 1);
        Controls.Add(root);
    }

    private Control BuildControlsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 12,
            RowCount = 3,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ConfigureNumeric(_startMHzInput, 1, 500, 5, 2, 1);
        ConfigureNumeric(_stopMHzInput, 1, 500, 50, 2, 1);
        ConfigureNumeric(_pointsInput, 11, 5001, 401, 0, 1);
        ConfigureNumeric(_z0Input, 1, 1000, 50, 1, 0.5m);

        _presetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _presetCombo.Items.AddRange(new object[]
        {
            "Validation #1 Through",
            "Validation #2 Sharp",
            "Validation #3 Gradient"
        });
        _presetCombo.SelectedIndex = 1;
        _presetCombo.SelectedIndexChanged += (_, _) => LoadPreset();

        _simulateButton.Text = "Run Simulation";
        _simulateButton.AutoSize = true;
        _simulateButton.Click += (_, _) => RunSimulation();

        _saveJsonButton.Text = "Save JSON";
        _saveJsonButton.AutoSize = true;
        _saveJsonButton.Click += (_, _) => SaveConfiguration();

        _loadJsonButton.Text = "Load JSON";
        _loadJsonButton.AutoSize = true;
        _loadJsonButton.Click += (_, _) => LoadConfiguration();

        _statusLabel.AutoSize = true;
        _statusLabel.Text = "Ready";

        _structureInput.Multiline = true;
        _structureInput.ScrollBars = ScrollBars.Both;
        _structureInput.AcceptsReturn = true;
        _structureInput.AcceptsTab = true;
        _structureInput.Font = new Font("Consolas", 9);
        _structureInput.Dock = DockStyle.Fill;
        _structureInput.Height = 95;

        panel.Controls.Add(new Label { Text = "Start MHz", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.Controls.Add(_startMHzInput, 1, 0);
        panel.Controls.Add(new Label { Text = "Stop MHz", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        panel.Controls.Add(_stopMHzInput, 3, 0);
        panel.Controls.Add(new Label { Text = "Points", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);
        panel.Controls.Add(_pointsInput, 5, 0);
        panel.Controls.Add(new Label { Text = "Z0 (Ohm)", AutoSize = true, Anchor = AnchorStyles.Left }, 6, 0);
        panel.Controls.Add(_z0Input, 7, 0);
        panel.Controls.Add(_simulateButton, 8, 0);
        panel.Controls.Add(_saveJsonButton, 9, 0);
        panel.Controls.Add(_loadJsonButton, 10, 0);
        panel.Controls.Add(_statusLabel, 11, 0);

        panel.Controls.Add(new Label { Text = "Preset", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        panel.Controls.Add(_presetCombo, 1, 1);
        panel.SetColumnSpan(_presetCombo, 2);

        panel.Controls.Add(new Label { Text = "Structure JSON", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        panel.Controls.Add(_structureInput, 1, 2);
        panel.SetColumnSpan(_structureInput, 9);

        LoadPreset();
        return panel;
    }

    private static void ConfigureNumeric(NumericUpDown input, decimal min, decimal max, decimal value, int decimalPlaces, decimal increment)
    {
        input.Minimum = min;
        input.Maximum = max;
        input.Value = value;
        input.DecimalPlaces = decimalPlaces;
        input.Increment = increment;
        input.Dock = DockStyle.Fill;
    }

    private Control BuildPlotsGrid()
    {
        var plots = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };

        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        ConfigurePlotView(_smithPlot);
        ConfigurePlotView(_s11DbPlot);
        ConfigurePlotView(_s21PolarPlot);
        ConfigurePlotView(_s21DbPlot);

        plots.Controls.Add(_smithPlot, 0, 0);
        plots.Controls.Add(_s11DbPlot, 1, 0);
        plots.Controls.Add(_s21PolarPlot, 0, 1);
        plots.Controls.Add(_s21DbPlot, 1, 1);

        return plots;
    }

    private static void ConfigurePlotView(PlotView view)
    {
        view.Dock = DockStyle.Fill;
        view.BackColor = Color.WhiteSmoke;
        view.PanCursor = Cursors.Hand;
        view.ZoomHorizontalCursor = Cursors.SizeWE;
        view.ZoomVerticalCursor = Cursors.SizeNS;
    }

    private void LoadPreset()
    {
        _structureInput.Text = _presetCombo.SelectedIndex switch
        {
            0 => "[]",
            2 => "[\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 470e-9 },\r\n" +
                 "  { \"type\": \"ShuntC\", \"value\": 47e-12 },\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 330e-9 },\r\n" +
                 "  { \"type\": \"ShuntC\", \"value\": 68e-12 },\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 220e-9 },\r\n" +
                 "  { \"type\": \"ShuntC\", \"value\": 100e-12 },\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 330e-9 },\r\n" +
                 "  { \"type\": \"ShuntC\", \"value\": 68e-12 },\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 470e-9 },\r\n" +
                 "  { \"type\": \"ShuntC\", \"value\": 47e-12 }\r\n" +
                 "]",
            _ => "[\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 220e-9 },\r\n" +
                 "  { \"type\": \"ShuntC\", \"value\": 100e-12 },\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 220e-9 },\r\n" +
                 "  { \"type\": \"ShuntC\", \"value\": 100e-12 },\r\n" +
                 "  { \"type\": \"SeriesL\", \"value\": 220e-9 }\r\n" +
                 "]"
        };
    }

    private void RunSimulation()
    {
        try
        {
            var structure = ParseStructure(_structureInput.Text);
            var sweep = Simulate(
                (double)_startMHzInput.Value * 1e6,
                (double)_stopMHzInput.Value * 1e6,
                (int)_pointsInput.Value,
                (double)_z0Input.Value,
                structure);

            _smithPlot.Model = BuildSmithModel(sweep);
            _s11DbPlot.Model = BuildS11DbModel(sweep);
            _s21PolarPlot.Model = BuildS21PolarModel(sweep);
            _s21DbPlot.Model = BuildS21DbModel(sweep);

            _statusLabel.Text = $"OK | {sweep.Count} points";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void SaveConfiguration()
    {
        try
        {
            var structure = ParseStructure(_structureInput.Text);
            var config = new SimulationConfig
            {
                StartMHz = (double)_startMHzInput.Value,
                StopMHz = (double)_stopMHzInput.Value,
                Points = (int)_pointsInput.Value,
                Z0 = (double)_z0Input.Value,
                Structure = structure
            };

            using var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                AddExtension = true,
                FileName = "rf-simulation-config.json"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
            _statusLabel.Text = $"Saved: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Save error: {ex.Message}";
        }
    }

    private void LoadConfiguration()
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var json = File.ReadAllText(dialog.FileName);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var config = JsonSerializer.Deserialize<SimulationConfig>(json, options)
                         ?? throw new InvalidOperationException("Configuration JSON is empty.");

            config.Structure ??= new List<RfElement>();
            ValidateStructure(config.Structure);

            SetNumericValue(_startMHzInput, (decimal)config.StartMHz);
            SetNumericValue(_stopMHzInput, (decimal)config.StopMHz);
            SetNumericValue(_pointsInput, config.Points);
            SetNumericValue(_z0Input, (decimal)config.Z0);

            _presetCombo.SelectedIndex = -1;
            _structureInput.Text = JsonSerializer.Serialize(config.Structure, new JsonSerializerOptions { WriteIndented = true });
            RunSimulation();
            _statusLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Load error: {ex.Message}";
        }
    }

    private static void SetNumericValue(NumericUpDown input, decimal value)
    {
        input.Value = Math.Min(input.Maximum, Math.Max(input.Minimum, value));
    }

    private static List<RfElement> ParseStructure(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var elements = JsonSerializer.Deserialize<List<RfElement>>(json, options)
                       ?? throw new InvalidOperationException("Structure JSON is empty.");

        ValidateStructure(elements);

        return elements;
    }

    private static void ValidateStructure(IReadOnlyCollection<RfElement> elements)
    {
        if (elements.Count == 0)
        {
            throw new InvalidOperationException("Structure must contain at least one element.");
        }

        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.Type))
            {
                throw new InvalidOperationException("Each element must define type.");
            }
            if (element.Value <= 0)
            {
                throw new InvalidOperationException("Element values must be positive.");
            }
        }
    }

    private static List<SweepPoint> Simulate(double startHz, double stopHz, int points, double z0, IReadOnlyList<RfElement> structure)
    {
        if (points < 2)
        {
            throw new InvalidOperationException("Sweep points must be at least 2.");
        }
        if (stopHz <= startHz)
        {
            throw new InvalidOperationException("Stop frequency must be greater than start frequency.");
        }

        var result = new List<SweepPoint>(points);
        var step = (stopHz - startHz) / (points - 1);

        for (var i = 0; i < points; i++)
        {
            var f = startHz + i * step;
            var omega = 2.0 * Math.PI * f;
            var total = AbcdMatrix.Identity;

            foreach (var element in structure)
            {
                var section = element.Type.Trim().ToLowerInvariant() switch
                {
                    "seriesl" => AbcdMatrix.SeriesImpedance(Complex.ImaginaryOne * omega * element.Value),
                    "shuntc" => AbcdMatrix.ShuntAdmittance(Complex.ImaginaryOne * omega * element.Value),
                    _ => throw new InvalidOperationException($"Unsupported element type: {element.Type}")
                };

                total = total * section;
            }

            var denominator = total.A + total.B / z0 + total.C * z0 + total.D;
            var s11 = (total.A + total.B / z0 - total.C * z0 - total.D) / denominator;
            var s21 = 2.0 / denominator;

            result.Add(new SweepPoint(f, s11, s21));
        }

        return result;
    }

    private static PlotModel BuildSmithModel(IReadOnlyList<SweepPoint> sweep)
    {
        var model = new PlotModel { Title = "S11 Smith Chart", PlotAreaBorderColor = OxyColors.Gray };
        ConfigureComplexPlaneAxes(model);

        AddSmithGrid(model);

        var trace = new LineSeries
        {
            Color = OxyColor.FromRgb(184, 161, 0),
            StrokeThickness = 1.6
        };

        foreach (var p in sweep)
        {
            trace.Points.Add(new DataPoint(p.S11.Real, p.S11.Imaginary));
        }

        model.Series.Add(trace);
        AddMarkers(model, sweep.Select(p => new Complex(p.S11.Real, p.S11.Imaginary)).ToList());
        return model;
    }

    private static PlotModel BuildS21PolarModel(IReadOnlyList<SweepPoint> sweep)
    {
        var model = new PlotModel { Title = "S21 Polar Plot", PlotAreaBorderColor = OxyColors.Gray };
        ConfigureComplexPlaneAxes(model);

        AddPolarGrid(model);

        var trace = new LineSeries
        {
            Color = OxyColor.FromRgb(184, 161, 0),
            StrokeThickness = 1.6
        };

        foreach (var p in sweep)
        {
            trace.Points.Add(new DataPoint(p.S21.Real, p.S21.Imaginary));
        }

        model.Series.Add(trace);
        AddMarkers(model, sweep.Select(p => new Complex(p.S21.Real, p.S21.Imaginary)).ToList());
        return model;
    }

    private static PlotModel BuildS11DbModel(IReadOnlyList<SweepPoint> sweep)
    {
        return BuildDbModel("S11 Return Loss (dB)", sweep, p => ToDbMagnitude(p.S11));
    }

    private static PlotModel BuildS21DbModel(IReadOnlyList<SweepPoint> sweep)
    {
        return BuildDbModel("S21 Gain (dB)", sweep, p => ToDbMagnitude(p.S21));
    }

    private static PlotModel BuildDbModel(string title, IReadOnlyList<SweepPoint> sweep, Func<SweepPoint, double> selector)
    {
        var model = new PlotModel { Title = title, PlotAreaBorderColor = OxyColors.Gray };
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Frequency (MHz)",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(80, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(30, OxyColors.Gray)
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "dB",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(80, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(30, OxyColors.Gray)
        });

        var line = new LineSeries
        {
            Color = OxyColor.FromRgb(184, 161, 0),
            StrokeThickness = 1.6
        };

        foreach (var p in sweep)
        {
            line.Points.Add(new DataPoint(p.FrequencyHz / 1e6, selector(p)));
        }
        model.Series.Add(line);

        AddDbMarkers(model, sweep, selector);
        return model;
    }

    private static void ConfigureComplexPlaneAxes(PlotModel model)
    {
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = -1.1,
            Maximum = 1.1,
            IsAxisVisible = false
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -1.1,
            Maximum = 1.1,
            IsAxisVisible = false
        });
    }

    private static void AddPolarGrid(PlotModel model)
    {
        var radii = new[] { 0.25, 0.5, 0.75, 1.0 };
        foreach (var r in radii)
        {
            model.Series.Add(CircleSeries(0, 0, r, OxyColor.FromAColor(70, OxyColors.Gray)));
        }

        foreach (var angle in new[] { 0, 45, 90, 135 })
        {
            var rad = Math.PI * angle / 180.0;
            var c = Math.Cos(rad);
            var s = Math.Sin(rad);
            model.Series.Add(new LineSeries
            {
                Color = OxyColor.FromAColor(60, OxyColors.Gray),
                StrokeThickness = 1,
                Points =
                {
                    new DataPoint(-c, -s),
                    new DataPoint(c, s)
                }
            });
        }
    }

    private static void AddSmithGrid(PlotModel model)
    {
        model.Series.Add(CircleSeries(0, 0, 1.0, OxyColor.FromAColor(90, OxyColors.Gray)));

        foreach (var r in new[] { 0.2, 0.5, 1.0, 2.0, 5.0 })
        {
            var centerX = r / (1.0 + r);
            var radius = 1.0 / (1.0 + r);
            model.Series.Add(CircleSeries(centerX, 0, radius, OxyColor.FromAColor(70, OxyColors.Gray)));
        }

        foreach (var x in new[] { 0.2, 0.5, 1.0, 2.0, 5.0 })
        {
            AddReactanceArc(model, x, OxyColor.FromAColor(70, OxyColors.Gray));
            AddReactanceArc(model, -x, OxyColor.FromAColor(70, OxyColors.Gray));
        }
    }

    private static void AddReactanceArc(PlotModel model, double x, OxyColor color)
    {
        var center = new DataPoint(1, 1.0 / x);
        var radius = Math.Abs(1.0 / x);
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = 1
        };

        for (var deg = -180; deg <= 180; deg += 2)
        {
            var rad = Math.PI * deg / 180.0;
            var px = center.X + radius * Math.Cos(rad);
            var py = center.Y + radius * Math.Sin(rad);
            if (px * px + py * py <= 1.001)
            {
                series.Points.Add(new DataPoint(px, py));
            }
        }

        if (series.Points.Count > 1)
        {
            model.Series.Add(series);
        }
    }

    private static LineSeries CircleSeries(double cx, double cy, double radius, OxyColor color)
    {
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = 1
        };

        for (var deg = 0; deg <= 360; deg += 2)
        {
            var rad = Math.PI * deg / 180.0;
            series.Points.Add(new DataPoint(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad)));
        }
        return series;
    }

    private static void AddMarkers(PlotModel model, IReadOnlyList<Complex> trace)
    {
        if (trace.Count == 0)
        {
            return;
        }

        var indices = new[] { 0, trace.Count / 2, trace.Count - 1 };
        var colors = new[] { OxyColors.Red, OxyColors.LimeGreen, OxyColors.DodgerBlue };

        for (var i = 0; i < indices.Length; i++)
        {
            var point = trace[indices[i]];
            model.Series.Add(new ScatterSeries
            {
                MarkerType = MarkerType.Triangle,
                MarkerStroke = colors[i],
                MarkerFill = OxyColors.Transparent,
                MarkerSize = 4,
                Points = { new ScatterPoint(point.Real, point.Imaginary) }
            });
        }
    }

    private static void AddDbMarkers(PlotModel model, IReadOnlyList<SweepPoint> sweep, Func<SweepPoint, double> selector)
    {
        if (sweep.Count == 0)
        {
            return;
        }

        var indices = new[] { 0, sweep.Count / 2, sweep.Count - 1 };
        var colors = new[] { OxyColors.Red, OxyColors.LimeGreen, OxyColors.DodgerBlue };

        for (var i = 0; i < indices.Length; i++)
        {
            var p = sweep[indices[i]];
            model.Series.Add(new ScatterSeries
            {
                MarkerType = MarkerType.Triangle,
                MarkerStroke = colors[i],
                MarkerFill = OxyColors.Transparent,
                MarkerSize = 4,
                Points = { new ScatterPoint(p.FrequencyHz / 1e6, selector(p)) }
            });
        }
    }

    private static double ToDbMagnitude(Complex value)
    {
        var mag = value.Magnitude;
        mag = Math.Max(mag, 1e-15);
        return 20.0 * Math.Log10(mag);
    }

    private sealed class RfElement
    {
        public string Type { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    private sealed class SimulationConfig
    {
        public double StartMHz { get; set; }
        public double StopMHz { get; set; }
        public int Points { get; set; }
        public double Z0 { get; set; }
        public List<RfElement> Structure { get; set; } = new();
    }

    private readonly record struct SweepPoint(double FrequencyHz, Complex S11, Complex S21);

    private readonly record struct AbcdMatrix(Complex A, Complex B, Complex C, Complex D)
    {
        public static AbcdMatrix Identity => new(1, 0, 0, 1);

        public static AbcdMatrix SeriesImpedance(Complex z) => new(1, z, 0, 1);

        public static AbcdMatrix ShuntAdmittance(Complex y) => new(1, 0, y, 1);

        public static AbcdMatrix operator *(AbcdMatrix left, AbcdMatrix right)
        {
            return new AbcdMatrix(
                left.A * right.A + left.B * right.C,
                left.A * right.B + left.B * right.D,
                left.C * right.A + left.D * right.C,
                left.C * right.B + left.D * right.D);
        }
    }
}
