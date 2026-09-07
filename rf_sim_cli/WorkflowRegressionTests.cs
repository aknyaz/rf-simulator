using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace RfSimulator.Cli;

// Extends the repository's existing CLI regression runner; no additional test framework/package.
internal static class WorkflowRegressionTests
{
    private static List<RfElement> Ladder() => new()
    {
        new() { Type = "SeriesL", Value = 100e-9 }, new() { Type = "ShuntC", Value = 62e-12 },
        new() { Type = "SeriesL", Value = 100e-9 }, new() { Type = "ShuntC", Value = 62e-12 },
        new() { Type = "SeriesL", Value = 220e-9 }
    };

    public static List<RegressionResult> Run()
    {
        var results = new List<RegressionResult>();
        void Test(string name, Action action)
        {
            var failures = new List<string>();
            try { action(); } catch (Exception ex) { failures.Add(ex.ToString()); }
            results.Add(new RegressionResult { Name = name, File = "workflow/" + name, Passed = failures.Count == 0, Failures = failures });
        }

        Test("saver_calibration_ideal_and_double_application", () =>
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "# Ideal standards\n1000000 -1 0 1 0 0 0 1 0 0 0 0 0\n20000000 -1 0 1 0 0 0 1 0 0 0 0 0\n");
                var calibration = new SaverCalibration(path);
                var original = new MeasuredSweep("test", 50, new[] { new SweepPoint(10e6, new Complex(0.1, -0.2), new Complex(0.7, 0.1)) });
                var corrected = calibration.Apply(original);
                Near((corrected.Points[0].S11 - original.Points[0].S11).Magnitude, 0, 1e-12);
                Near((corrected.Points[0].S21 - original.Points[0].S21).Magnitude, 0, 1e-12);
                Throws(() => calibration.Apply(corrected));
                Throws(() => calibration.Apply(original with { ReferenceOhms = 75 }));
            }
            finally { File.Delete(path); }
        });

        Test("nanovna_segment_validation", () =>
        {
            NanoVnaSerial.ValidateSweep(new FrequencyConfiguration(10, 20, 401, 50));
            Throws(() => NanoVnaSerial.ValidateSweep(new FrequencyConfiguration(10, 20, 402, 50)));
            Throws(() => NanoVnaSerial.ValidateSweep(new FrequencyConfiguration(10, 20, 401, 75)));
            var frequencies = Enumerable.Range(0, 101).Select(index => (10000000 + index * 25000).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var data = Enumerable.Repeat("0.5 -0.25", 101).ToArray();
            var points = NanoVnaSerial.ParseSegment(frequencies, data, data, 10000000, 12500000);
            Check(points.Count == 101, "Wrong segment size");
            Near(points[50].S21.Real, 0.5, 0);
            Near(points[50].S21.Imaginary, -0.25, 0);
            Throws(() => NanoVnaSerial.ParseSegment(frequencies, data[..100], data, 10000000, 12500000));
            frequencies[30] = frequencies[29];
            Throws(() => NanoVnaSerial.ParseSegment(frequencies, data, data, 10000000, 12500000));
        });

        Test("touchstone_import_units_formats_and_delay", () =>
        {
            foreach (var format in new[] { "RI", "MA", "DB" })
            foreach (var unit in new[] { ("HZ", 1.0), ("KHZ", 1e3), ("MHZ", 1e6), ("GHZ", 1e9) })
            {
                var lines = new List<string> { $"# {unit.Item1} S {format} R 75" };
                for (var index = 0; index < 101; index++)
                {
                    var frequency = 10e6 + index * 1e5;
                    var phase = -2 * Math.PI * frequency * 64e-9;
                    var pair = format == "RI" ? FormattableString.Invariant($"{Math.Cos(phase):R} {Math.Sin(phase):R}")
                        : FormattableString.Invariant($"{(format == "MA" ? 1 : 0)} {phase * 180 / Math.PI:R}");
                    lines.Add(FormattableString.Invariant($"{frequency / unit.Item2:R} 0 0 {pair}\n0 0 0 0 ! wrapped record"));
                }
                var measurement = TouchstoneImporter.Parse(string.Join("\n", lines));
                Near(measurement.ReferenceOhms, 75, 0);
                Check(measurement.Points.Count == 101, "Incorrect record count");
                foreach (var delay in RfMath.CalculateGroupDelayNs(measurement.Points)) Near(delay, 64, 1e-8);
            }
            foreach (var invalid in new[] { "", "# HZ Z RI R 50", "[Version] 2.0", "# HZ S RI R 0",
                "# HZ S RI R 50\n1 0 0 1 0 0 0 0 0\n1 0 0 1 0 0 0 0 0",
                "# HZ S RI R 50\n1 0 0 NaN 0 0 0 0 0\n2 0 0 1 0 0 0 0 0" })
                Throws(() => TouchstoneImporter.Parse(invalid));
        });

        Test("shared_configuration_and_snapshot_isolation", () =>
        {
            var frequency = new FrequencyConfiguration(10, 20, 401, 75);
            Near(frequency.StartHz, 10e6, 0); Near(frequency.StopHz, 20e6, 0);
            Near(frequency.InspectionMHz(null), 15, 0); Near(frequency.InspectionMHz(12.34), 12.34, 0);
            Throws(() => frequency.InspectionMHz(21));
            foreach (var bad in new[] { frequency with { StartMHz = 0 }, frequency with { StopMHz = 10 },
                frequency with { Points = 1 }, frequency with { Z0 = double.NaN } }) Throws(bad.Validate);
            var workspace = new CircuitWorkspace();
            var structure = Ladder();
            var snapshot = workspace.Set(frequency, structure, new[] { 0 });
            structure[0].Value = 1;
            var copy = snapshot.Structure; copy[0].Value = 2;
            Near(snapshot.Structure[0].Value, 100e-9, 0);
            Check(workspace.Set(frequency, snapshot.Structure, new[] { 0 }).Revision == 1, "Unchanged inputs changed revision");
            Check(workspace.Set(frequency with { StopMHz = 25 }, snapshot.Structure).Revision == 2, "Sweep did not change revision");
            var sweep = snapshot.Evaluate();
            Check(sweep.Count == 401, "Wrong point count"); Near(sweep[200].FrequencyHz, 15e6, 0);
        });

        Test("lossless_power_conservation_and_lossy_control", () =>
        {
            foreach (var z0 in new[] { 25.0, 50, 75, 100 })
            foreach (var structure in new[] { Ladder(), new List<RfElement> { new() { Type = "SeriesC", Value = 150e-12 }, new() { Type = "ShuntL", Value = 470e-9 } } })
            foreach (var point in SimulationEngine.SimulateSParameters(5e6, 50e6, 401, z0, structure))
            {
                Near(point.S11.Magnitude * point.S11.Magnitude + point.S21.Magnitude * point.S21.Magnitude, 1, 1e-11);
                Near((point.S12 - point.S21).Magnitude, 0, 1e-11);
            }
            var lossy = SimulationEngine.Simulate(10e6, 20e6, 3, 50, new[] { new RfElement { Type = "SeriesR", Value = 50 } });
            Check(lossy.All(p => p.S11.Magnitude * p.S11.Magnitude + p.S21.Magnitude * p.S21.Magnitude < 0.6), "Lossy circuit was treated as lossless");
            var sweep = SimulationEngine.Simulate(10e6, 20e6, 401, 75, Ladder());
            var propagation = PropagationAnalysis.Analyze(Ladder(), sweep[200].FrequencyHz, 75, 0.005, sweep);
            var expectedImpedance = 75 * (Complex.One + sweep[200].S11) / (Complex.One - sweep[200].S11);
            Near((propagation.Diagnostics.InputImpedanceOhms - expectedImpedance).Magnitude, 0, 1e-10);
            Throws(() => SimulationEngine.Simulate(0, 20e6, 401, 50, Ladder()));
        });

        Test("group_delay_units_unwrap_endpoints_and_null_caution", () =>
        {
            const double delaySeconds = 123e-9;
            var pureDelay = Enumerable.Range(0, 401).Select(i =>
            {
                var f = 10e6 + i * 25e3;
                return new SweepPoint(f, Complex.Zero, Complex.FromPolarCoordinates(1, -2 * Math.PI * f * delaySeconds));
            }).ToList();
            foreach (var delay in RfMath.CalculateGroupDelayNs(pureDelay)) Near(delay, 123, 1e-9);
            var nearNull = pureDelay.Select(p => p with { S21 = p.S21 * 1e-14 }).ToList();
            foreach (var delay in RfMath.CalculateGroupDelayNs(nearNull)) Near(delay, 123, 1e-9);
            var through = pureDelay.Select(p => p with { S21 = Complex.One }).ToList();
            Check(RfMath.CalculateGroupDelayNs(through).All(d => d == 0), "Through delay not zero");
            const double inductance = 220e-9;
            var inductor = SimulationEngine.Simulate(10e6, 20e6, 4001, 75, new[] { new RfElement { Type = "SeriesL", Value = inductance } });
            var delays = RfMath.CalculateGroupDelayNs(inductor);
            for (var i = 0; i < inductor.Count; i++)
            {
                var ratio = inductance / 150;
                var analytic = ratio / (1 + Math.Pow(2 * Math.PI * inductor[i].FrequencyHz * ratio, 2)) * 1e9;
                Near(delays[i], analytic, i == 0 || i == inductor.Count - 1 ? 1e-5 : 1e-8);
            }
            var reversed = pureDelay.Select(p => p with { S21 = Complex.Conjugate(p.S21) }).ToList();
            Near(RfMath.CalculatePhaseMetrics(reversed).AverageGroupDelayNs, 0, 0); // Existing positive-only aggregation is intentional and documented.
        });

        Test("locked_elements_reproducible_search_and_feasibility", () =>
        {
            var structure = Ladder();
            structure.Add(new RfElement { Type = "SeriesR", Value = 12 });
            var config = new OptimizerEngine.OptimizerConfig { StartHz = 10e6, StopHz = 20e6, Points = 41, Z0 = 75,
                MaxIterations = 3, RandomRestarts = 4, RandomSeed = 817, InitialStructure = structure, LockedIndices = new[] { 0, 1 }, MaxDegreeOfParallelism = 1 };
            List<SweepPoint> Eval(List<RfElement> s) => SimulationEngine.Simulate(config.StartHz, config.StopHz, config.Points, config.Z0, s);
            var local = OptimizerEngine.SequentialLocalSearch(structure, Eval, config);
            Near(local.Structure[0].Value, structure[0].Value, 0); Near(local.Structure[1].Value, structure[1].Value, 0);
            Near(local.Structure[^1].Value, 12, 0);
            var first = OptimizerEngine.RandomRestartLocalSearch(Eval, config);
            config.MaxDegreeOfParallelism = 4;
            var second = OptimizerEngine.RandomRestartLocalSearch(Eval, config);
            for (var i = 0; i < first.Count; i++)
            {
                Near(first[i].Score, second[i].Score, 1e-12);
                Check(JsonSerializer.Serialize(first[i].Structure) == JsonSerializer.Serialize(second[i].Structure), "Seed/parallelism changed structure");
                Near(first[i].Structure[0].Value, structure[0].Value, 0);
                Near(first[i].Structure[1].Value, structure[1].Value, 0);
                Near(first[i].Structure[^1].Value, 12, 0);
            }
            config.TargetS11Db = -100;
            var infeasible = OptimizerEngine.EvaluateStructure(structure, Eval, config);
            Check(!infeasible.Feasible && infeasible.ConstraintViolations.Count > 0, "Infeasible candidate reported successful");
        });

        Test("legacy_projects_and_reference_impedance_export", () =>
        {
            var f = new FrequencyConfiguration(10, 20, 401, 75);
            var (array, _) = ProjectFiles.Read(JsonSerializer.Serialize(Ladder()), f);
            Near(array.StartMHz, 10, 0); Near(array.Z0, 75, 0);
            var legacy = "{\"StartMHz\":10,\"StopMHz\":20,\"Points\":401,\"Z0\":75,\"OptStartMHz\":5,\"OptStopMHz\":50,\"Structure\":" + JsonSerializer.Serialize(Ladder()) + "}";
            var (project, notice) = ProjectFiles.Read(legacy, f);
            Check(notice.Contains("ignored"), "Legacy optimizer range silently ignored");
            Near(project.StartMHz, 10, 0);
            var snapshot = new CircuitSnapshot(3, f, Ladder(), new[] { 2 });
            var (roundtrip, _) = ProjectFiles.Read(JsonSerializer.Serialize(snapshot.ToConfig(12.5)), f);
            Check(roundtrip.CircuitId == snapshot.CircuitId && roundtrip.LockedIndices.SequenceEqual(new[] { 2 }), "Export lost identity/locks");
            Near(roundtrip.ManualMarkerMHz!.Value, 12.5, 0);
            var path = Path.GetTempFileName();
            try
            {
                TouchstoneExporter.WriteS2P(path, SimulationEngine.SimulateSParameters(10e6, 20e6, 401, 75, Ladder()), 75, JsonSerializer.Serialize(snapshot.ToConfig()));
                var text = File.ReadAllText(path);
                Check(text.Contains("R 75") && text.Contains(snapshot.CircuitId), "Touchstone reference/identity missing");
            }
            finally { File.Delete(path); }
        });

        // WinForms state transitions are exercised on the UI thread, without displaying a window.
        var thread = new Thread(() => Test("manual_ui_state_marker_preview_apply_and_stale_workers", TestUi));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        return results;
    }

    private static void TestUi()
    {
        using var form = new Form1();
        _ = form.Handle;
        T Field<T>(string name) => (T)typeof(Form1).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        object? Call(string name, params object?[] args) => typeof(Form1).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
        void Run() => Call("RunSimulation");
        Field<NumericUpDown>("_startMHzInput").Value = 10;
        Field<NumericUpDown>("_stopMHzInput").Value = 20;
        Field<NumericUpDown>("_pointsInput").Value = 401;
        Field<NumericUpDown>("_z0Input").Value = 75;
        Run();
        Near((double)Field<NumericUpDown>("_manualTargetMHzInput").Value, 15, 0);
        var config = (OptimizerEngine.OptimizerConfig)Call("GetOptimizerConfig")!;
        Near(config.StartHz, 10e6, 0); Near(config.StopHz, 20e6, 0); Near(config.Z0, 75, 0);
        Check(config.Points == 401, "Optimizer points differ");
        var workspace = Field<CircuitWorkspace>("_workspace");
        var original = workspace.Current!;
        Field<CheckBox>("_manualMarkerInput").Checked = true;
        Field<NumericUpDown>("_manualTargetMHzInput").Value = 12.333333m;
        Run();
        Check(workspace.Revision == original.Revision, "Marker altered circuit revision");
        Near(Field<double>("_resolvedMarkerMHz"), 12.325, 1e-10);
        Check(Field<TextBox>("_manualMetricsText").Text.Contains("12.325000"), "Marker readout not at plotted frequency");
        foreach (var name in new[] { "_groupDelayPlot", "_manualGroupDelayPlot" })
        {
            var line = Field<PlotView>(name).Model.Series.OfType<LineSeries>().First();
            Check(line.Points.Count == 401 && line.Points[0].X == 10 && line.Points[^1].X == 20, "Current plot has wrong sweep");
        }
        Field<NumericUpDown>("_stopMHzInput").Value = 12;
        Run();
        Check(!Field<CheckBox>("_manualMarkerInput").Checked, "Out-of-band marker not reset");
        Near((double)Field<NumericUpDown>("_manualTargetMHzInput").Value, 11, 0);
        Field<NumericUpDown>("_stopMHzInput").Value = 20; Run();
        var grid = Field<DataGridView>("_manualElementsGrid");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        Field<NumericUpDown>("_manualValueInput").Value = 330;
        Check(Field<List<SweepPoint>>("_lastSweep").Count == 0, "Edited circuit left old results visible");
        Run();
        Near(workspace.Current!.Structure[0].Value, 330e-9, 1e-20);
        Field<CheckBox>("_manualLockInput").Checked = true;
        Run();
        config = (OptimizerEngine.OptimizerConfig)Call("GetOptimizerConfig")!;
        Check(config.LockedIndices.Contains(0), "UI lock not passed to optimizer");
        var snapshot = workspace.Current!;
        var candidateStructure = snapshot.Structure;
        candidateStructure[1].Value = 68e-12;
        List<SweepPoint> Eval(List<RfElement> s) => SimulationEngine.Simulate(config.StartHz, config.StopHz, config.Points, config.Z0, s);
        var baseline = OptimizerEngine.EvaluateStructure(snapshot.Structure, Eval, config);
        var candidate = OptimizerEngine.EvaluateStructure(candidateStructure, Eval, config);
        var generation = Field<long>("_configurationGeneration");
        Call("PublishCandidates", generation, snapshot, config, baseline, new List<OptimizerEngine.CandidateResult> { candidate });
        Check(workspace.Current!.CircuitId == snapshot.CircuitId, "Preview changed current circuit");
        Near(((List<RfElement>)Call("CloneManualStructure")!)[1].Value, snapshot.Structure[1].Value, 0);
        Check(Field<TextBox>("_candidatePreviewText").Text.Contains("current circuit unchanged"), "Preview label missing");
        Field<CheckBox>("_manualMarkerInput").Checked = true;
        Field<NumericUpDown>("_manualTargetMHzInput").Value = 16; Run();
        Check(Field<long>("_candidateGeneration") == generation, "Marker changed optimization objective");
        Call("ApplyBestResult");
        Near(workspace.Current!.Structure[1].Value, 68e-12, 0);
        Check(workspace.Current.LockedIndices.Contains(0), "Apply lost locks");
        Check(Field<List<SweepPoint>>("_lastSweep").SequenceEqual(workspace.Current.Evaluate()), "Applied results do not match state");
        Check(Field<PlotView>("_manualGroupDelayPlot").Model is not null && Field<PlotView>("_groupDelayPlot").Model is not null, "Apply failed to refresh views");
        Call("PublishCandidates", generation, snapshot, config, baseline, new List<OptimizerEngine.CandidateResult> { candidate });
        Check(Field<List<OptimizerEngine.CandidateResult>>("_candidateResults").Count == 0, "Stale worker replaced current candidates");
        Check(!Field<Button>("_applyBestButton").Enabled, "Stale candidate can be applied");
        Field<NumericUpDown>("_startMHzInput").Value = 25; Run();
        Check(Field<PlotView>("_groupDelayPlot").Model is null, "Invalid sweep retains old plot");
        Check(Field<Label>("_statusLabel").Text.Contains("Out of date"), "Invalid sweep not labeled");
        Call("LoadSharedProject", JsonSerializer.Serialize(snapshot.ToConfig()));
        Check(workspace.Current!.LockedIndices.Contains(0) && Field<List<SweepPoint>>("_lastSweep").Count == 401, "Load not synchronized");
        Field<NumericUpDown>("_maxIterationsInput").Value = 2;
        var beforeAsync = workspace.Current.CircuitId;
        Call("OptimizeCurrentStructure");
        void WaitForOptimization()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Field<bool>("_optimizationRunning") && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            Check(!Field<bool>("_optimizationRunning"), "Async optimizer failed to complete");
        }
        WaitForOptimization();
        Check(Field<List<OptimizerEngine.CandidateResult>>("_candidateResults").Count > 0, "Async optimizer published no candidate");
        Check(workspace.Current.CircuitId == beforeAsync, "Async completion applied candidate implicitly");
        var csv = (string)Call("BuildCandidateCsv")!;
        Check(csv.Contains("OptimizerConfigJson") && csv.Contains("BaselineCircuitId") && csv.Contains("\"\"StartHz\"\""), "CSV missing context or escaping");
        Field<NumericUpDown>("_targetS11DbInput").Value = -16;
        Check(Field<List<OptimizerEngine.CandidateResult>>("_candidateResults").Count == 0, "Goal edit retained old candidate");
        Call("OptimizeCurrentStructure");
        Field<NumericUpDown>("_stopMHzInput").Value = 21;
        Run();
        WaitForOptimization();
        Check(Field<List<OptimizerEngine.CandidateResult>>("_candidateResults").Count == 0, "Async completion survived a sweep edit");
        Field<NumericUpDown>("_stopMHzInput").Value = 20; Run();
        if (Environment.GetEnvironmentVariable("RF_SIM_RENDER_UI") != "1") return;
        // Optional repeatable layout artifact for visual review.
        var artifactDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts");
        Directory.CreateDirectory(artifactDirectory);
        form.WindowState = FormWindowState.Normal;
        form.Size = new System.Drawing.Size(1900, 1000);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.Show();
        form.WindowState = FormWindowState.Normal;
        form.Location = new System.Drawing.Point(-32000, -32000);
        form.Size = new System.Drawing.Size(1900, 1000);
        Application.DoEvents();
        form.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(Path.Combine(artifactDirectory, "manual-workflow.png"));
        Field<CheckBox>("_optimizerAdvancedInput").Checked = true;
        form.PerformLayout();
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(Path.Combine(artifactDirectory, "advanced-workflow.png"));
        Field<CheckBox>("_optimizerAdvancedInput").Checked = false;
        Call("OptimizeCurrentStructure");
        WaitForOptimization();
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        var previewTab = Descendants(form).OfType<TabPage>().Single(p => p.Text == "Candidate Preview");
        ((TabControl)previewTab.Parent!).SelectedTab = previewTab;
        form.PerformLayout();
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(Path.Combine(artifactDirectory, "candidate-preview.png"));
        var measurement = TouchstoneImporter.Parse("# MHZ S MA R 50\n10 0.1 0 0.8 -90 0 0 0 0\n15 0.1 0 0.8 -135 0 0 0 0\n20 0.1 0 0.8 -180 0 0 0 0");
        typeof(Form1).GetField("_measurement", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, measurement);
        var beforeMeasurement = Field<TextBox>("_structureInput").Text;
        typeof(Form1).GetMethod("ShowMeasurement", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { true });
        Check(Field<TextBox>("_structureInput").Text == beforeMeasurement, "Measurement comparison changed circuit");
        Check(Field<PlotView[]>("_measurementPlots").All(plot => plot.Model.Series.Count == 2), "Missing measured/model traces");
        var measurementTab = Descendants(form).OfType<TabPage>().Single(page => page.Text == "Measurements");
        ((TabControl)measurementTab.Parent!).SelectedTab = measurementTab;
        form.PerformLayout();
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(Path.Combine(artifactDirectory, "measurement-preview.png"));
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, double tolerance) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"Expected {expected:G17}, got {actual:G17}, tolerance {tolerance}");
    private static void Throws(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected validation failure");
    }
}
