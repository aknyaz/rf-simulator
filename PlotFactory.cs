using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace RfSimulator;

public static class PlotFactory
{
    public static PlotModel BuildSmithModel(IReadOnlyList<SweepPoint> sweep)
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

    public static PlotModel BuildS21PolarModel(IReadOnlyList<SweepPoint> sweep)
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

    public static PlotModel BuildS11DbModel(IReadOnlyList<SweepPoint> sweep)
    {
        return BuildDbModel("S11 Return Loss (dB)", sweep, p => RfMath.ToDbMagnitude(p.S11));
    }

    public static PlotModel BuildS21DbModel(IReadOnlyList<SweepPoint> sweep)
    {
        return BuildDbModel("S21 Gain (dB)", sweep, p => RfMath.ToDbMagnitude(p.S21));
    }

    public static PlotModel BuildS21PhaseModel(IReadOnlyList<SweepPoint> sweep)
    {
        var phaseRadians = RfMath.GetUnwrappedS21PhaseRadians(sweep);
        var phaseDegrees = phaseRadians.Select(p => p * 180.0 / Math.PI).ToList();
        var model = BuildFrequencyModel("S21 Phase (degrees)", "deg", sweep, i => phaseDegrees[i]);
        var yAxis = model.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);

        if (phaseDegrees.Count > 0 && yAxis is not null)
        {
            var minPhase = phaseDegrees.Min();
            var maxPhase = phaseDegrees.Max();
            var phaseRange = Math.Max(maxPhase - minPhase, 30.0);
            var padding = Math.Max(phaseRange * 0.08, 5.0);

            yAxis.Minimum = Math.Floor((minPhase - padding) / 30.0) * 30.0;
            yAxis.Maximum = Math.Ceiling((maxPhase + padding) / 30.0) * 30.0;
            yAxis.MajorStep = 30;
            yAxis.MinorStep = 10;
            yAxis.StringFormat = "0";
        }

        return model;
    }

    public static PlotModel BuildGroupDelayModel(IReadOnlyList<SweepPoint> sweep)
    {
        var groupDelayNs = RfMath.CalculateGroupDelayNs(sweep);
        var model = BuildFrequencyModel("Group Delay (ns)", "ns", sweep, i => groupDelayNs[i]);
        var yAxis = model.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);

        if (groupDelayNs.Count > 0 && yAxis is not null)
        {
            var finiteDelays = groupDelayNs.Where(double.IsFinite).ToList();
            if (finiteDelays.Count > 0)
            {
                var minDelay = finiteDelays.Min();
                var maxDelay = finiteDelays.Max();
                var delayRange = Math.Max(maxDelay - minDelay, 1.0);
                var padding = Math.Max(delayRange * 0.10, 0.5);

                yAxis.Minimum = Math.Floor((minDelay - padding) * 2.0) / 2.0;
                yAxis.Maximum = Math.Ceiling((maxDelay + padding) * 2.0) / 2.0;
                yAxis.MajorStep = ChooseGroupDelayMajorStep(yAxis.Maximum - yAxis.Minimum);
                yAxis.MinorStep = yAxis.MajorStep / 2.0;
                yAxis.StringFormat = "0.##";
            }
        }

        return model;
    }

    public static PlotModel BuildNodeVoltageMagnitudeModel(PropagationResult result)
    {
        return BuildIndexModel(
            "Node Voltage Magnitude",
            "Node Index",
            "|V|",
            result.Nodes.Select(n => new DataPoint(n.Index, n.VoltageMagnitude)).ToList());
    }

    public static PlotModel BuildNodePhaseModel(PropagationResult result)
    {
        return BuildIndexModel(
            "Node Phase",
            "Node Index",
            "deg",
            result.Nodes.Select(n => new DataPoint(n.Index, n.PhaseDeg)).ToList());
    }

    public static PlotModel BuildLocalPhaseShiftModel(PropagationResult result)
    {
        return BuildIndexModel(
            "Local Phase Shift Per Cell",
            "Cell Index",
            "deg",
            result.Cells.Select(c => new DataPoint(c.Index, c.DeltaPhaseDeg)).ToList());
    }

    public static PlotModel BuildSlowWaveFactorModel(PropagationResult result)
    {
        return BuildIndexModel(
            "Slow-Wave Factor",
            "Cell Index",
            "c / v_phase",
            result.Cells
                .Where(c => double.IsFinite(c.SlowWaveFactor))
                .Select(c => new DataPoint(c.Index, c.SlowWaveFactor))
                .ToList());
    }

    public static PlotModel BuildStoredEnergyModel(PropagationResult result)
    {
        var model = CreateIndexModel("Stored Energy Per Element", "Element Index", "J");
        model.Series.Add(BuildLineSeries(
            "Inductor magnetic energy",
            OxyColor.FromRgb(184, 161, 0),
            result.Cells.Select(c => new DataPoint(c.Index, c.InductorEnergyJoules)).ToList()));
        model.Series.Add(BuildLineSeries(
            "Capacitor electric energy",
            OxyColors.DodgerBlue,
            result.Capacitors.Select(c => new DataPoint(c.Index, c.CapacitorEnergyJoules)).ToList()));
        model.IsLegendVisible = true;
        return model;
    }

    private static PlotModel BuildDbModel(string title, IReadOnlyList<SweepPoint> sweep, Func<SweepPoint, double> selector)
    {
        return BuildFrequencyModel(title, "dB", sweep, i => selector(sweep[i]));
    }

    private static PlotModel BuildFrequencyModel(string title, string yAxisTitle, IReadOnlyList<SweepPoint> sweep, Func<int, double> selector)
    {
        var model = new PlotModel { Title = title, PlotAreaBorderColor = OxyColors.Gray };
        var startMHz = sweep.Count > 0 ? sweep[0].FrequencyHz / 1e6 : 0;
        var stopMHz = sweep.Count > 0 ? sweep[^1].FrequencyHz / 1e6 : 1;
        var axisPadMHz = Math.Max((stopMHz - startMHz) * 0.025, 0.5);
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Frequency (MHz)",
            Minimum = startMHz,
            Maximum = stopMHz + axisPadMHz,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(80, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(30, OxyColors.Gray)
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = yAxisTitle,
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

        for (int i = 0; i < sweep.Count; i++)
        {
            line.Points.Add(new DataPoint(sweep[i].FrequencyHz / 1e6, selector(i)));
        }
        model.Series.Add(line);

        AddFrequencyMarkers(model, sweep, selector);
        return model;
    }

    private static PlotModel BuildIndexModel(string title, string xAxisTitle, string yAxisTitle, IReadOnlyList<DataPoint> points)
    {
        var model = CreateIndexModel(title, xAxisTitle, yAxisTitle);
        model.Series.Add(BuildLineSeries(null, OxyColor.FromRgb(184, 161, 0), points));
        return model;
    }

    private static PlotModel CreateIndexModel(string title, string xAxisTitle, string yAxisTitle)
    {
        var model = new PlotModel { Title = title, PlotAreaBorderColor = OxyColors.Gray };
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = xAxisTitle,
            MajorStep = 1,
            MinorStep = 1,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(80, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(30, OxyColors.Gray)
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = yAxisTitle,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(80, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(30, OxyColors.Gray)
        });
        return model;
    }

    private static LineSeries BuildLineSeries(string? title, OxyColor color, IReadOnlyList<DataPoint> points)
    {
        var line = new LineSeries
        {
            Title = title,
            Color = color,
            StrokeThickness = 1.6,
            MarkerType = MarkerType.Circle,
            MarkerSize = 2.5
        };

        foreach (var point in points)
        {
            line.Points.Add(point);
        }

        return line;
    }

    private static double ChooseGroupDelayMajorStep(double rangeNs)
    {
        if (rangeNs <= 5)
        {
            return 1;
        }

        if (rangeNs <= 20)
        {
            return 5;
        }

        if (rangeNs <= 50)
        {
            return 10;
        }

        return 25;
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

    private static void AddFrequencyMarkers(PlotModel model, IReadOnlyList<SweepPoint> sweep, Func<int, double> selector)
    {
        if (sweep.Count == 0)
        {
            return;
        }

        var indices = new[] { 0, sweep.Count / 2, sweep.Count - 1 };
        var colors = new[] { OxyColors.Red, OxyColors.LimeGreen, OxyColors.DodgerBlue };

        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            var p = sweep[index];
            model.Series.Add(new ScatterSeries
            {
                MarkerType = MarkerType.Triangle,
                MarkerStroke = colors[i],
                MarkerFill = OxyColors.Transparent,
                MarkerSize = 4,
                Points = { new ScatterPoint(p.FrequencyHz / 1e6, selector(index)) }
            });
        }
    }
}
