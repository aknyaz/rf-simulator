using System.Text.Json;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace RfSimulator;

public partial class Form1
{
    private readonly CircuitWorkspace _workspace = new();
    private readonly CheckBox _manualMarkerInput = new() { Text = "Manual marker", AutoSize = true };
    private readonly Label _markerReadout = new() { AutoSize = true };
    private readonly NumericUpDown _randomSeedInput = new();
    private readonly CheckBox _optimizerAdvancedInput = new() { Text = "Advanced optimization settings", AutoSize = true };
    private readonly TextBox _candidatePreviewText = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly PlotView _candidateS11Preview = new();
    private readonly PlotView _candidateDelayPreview = new();
    private bool _workflowReady;
    private bool _refreshingWorkspace;
    private double _resolvedMarkerMHz;
    private long _configurationGeneration;
    private long _inspectionGeneration;
    private long _candidateGeneration = -1;
    private OptimizerEngine.OptimizerConfig? _candidateConfig;
    private OptimizerEngine.CandidateResult? _candidateBaseline;
    private CircuitSnapshot? _candidateBaselineSnapshot;
    private double RequestedMarkerMHz => ReadFrequency().InspectionMHz(_manualMarkerInput.Checked ? (double)_manualTargetMHzInput.Value : null);

    private FrequencyConfiguration ReadFrequency() => new((double)_startMHzInput.Value,
        (double)_stopMHzInput.Value, (int)_pointsInput.Value, (double)_z0Input.Value);

    private static bool StructuresEqual(IReadOnlyList<RfElement> left, IReadOnlyList<RfElement> right) =>
        left.Count == right.Count && left.Zip(right).All(p => p.First.Type == p.Second.Type && p.First.Value == p.Second.Value);

    private CircuitSnapshot CaptureCurrentState()
    {
        var structure = SimulationEngine.ParseStructure(_structureInput.Text);
        var locks = StructuresEqual(structure, CloneManualStructure())
            ? _manualElements.Select((e, i) => (e, i)).Where(p => p.e.Locked).Select(p => p.i).ToArray()
            : Array.Empty<int>();
        var previous = _workspace.Revision;
        var snapshot = _workspace.Set(ReadFrequency(), structure, locks);
        if (previous != snapshot.Revision) InvalidateCandidates();
        return snapshot;
    }

    private SimulationConfig CurrentExportConfiguration()
    {
        var config = CaptureCurrentState().ToConfig(_manualMarkerInput.Checked ? RequestedMarkerMHz : null);
        config.CellSpacingMm = (double)_manualSpacingMmInput.Value;
        return config;
    }

    private void InitializeSharedWorkflow()
    {
        var previousControls = (TableLayoutPanel)_startMHzInput.Parent!;
        var host = (TableLayoutPanel)previousControls.Parent!;
        var controls = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        host.Controls.Remove(previousControls);
        host.Controls.Add(controls, 0, 0);
        controls.SuspendLayout();
        controls.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        controls.Controls.Clear();
        controls.ColumnStyles.Clear();
        controls.RowStyles.Clear();
        controls.ColumnCount = 2;
        controls.RowCount = 5;
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var frequencyGroup = new GroupBox { Text = "Frequency — shared by simulation and optimization", AutoSize = true, Dock = DockStyle.Fill };
        var frequencyFields = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        foreach (var (label, input) in new[] { ("Start (MHz)", _startMHzInput), ("Stop (MHz)", _stopMHzInput), ("Sweep points", _pointsInput), ("Reference (ohm)", _z0Input) })
        {
            var field = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            field.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            input.Dock = DockStyle.None;
            input.Width = 110;
            field.Controls.Add(input);
            frequencyFields.Controls.Add(field);
        }
        frequencyGroup.Controls.Add(frequencyFields);
        controls.Controls.Add(frequencyGroup, 0, 0);
        controls.SetColumnSpan(frequencyGroup, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange(new Control[] { _simulateButton, _saveJsonButton, _loadJsonButton,
            new Label { Text = "Example circuit", AutoSize = true, Padding = new Padding(5, 5, 0, 0) }, _presetCombo });
        _presetCombo.Width = 170;
        controls.Controls.Add(actions, 0, 1);
        controls.SetColumnSpan(actions, 2);
        controls.Controls.Add(new Label { Text = "Circuit", AutoSize = true }, 0, 2);
        controls.SetColumnSpan(_structureSummaryInput, 1);
        controls.Controls.Add(_structureSummaryInput, 1, 2);
        controls.Controls.Add(new Label { Text = "Current", AutoSize = true }, 0, 3);
        _statusLabel.Height = 28;
        controls.SetColumnSpan(_statusLabel, 1);
        controls.Controls.Add(_statusLabel, 1, 3);
        var marker = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };
        marker.Controls.Add(new Label { Text = "Inspect at (MHz)", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        _manualTargetMHzInput.Dock = DockStyle.None;
        _manualTargetMHzInput.Width = 110;
        _manualTargetMHzInput.DecimalPlaces = 6;
        marker.Controls.Add(_manualTargetMHzInput);
        marker.Controls.Add(_manualMarkerInput);
        marker.Controls.Add(_markerReadout);
        controls.Controls.Add(marker, 0, 4);
        controls.SetColumnSpan(marker, 2);
        // The marker fits on the minimum supported panel width; avoid FlowLayout's initial
        // 100-pixel height being retained as an empty last autosized table row.
        marker.AutoSize = false;
        marker.Dock = DockStyle.Fill;
        controls.RowStyles[4] = new RowStyle(SizeType.Absolute, 34);
        controls.ResumeLayout(true);
        previousControls.Dispose();
        foreach (var input in new[] { _startMHzInput, _stopMHzInput, _pointsInput, _z0Input })
            input.ValueChanged += (_, _) => ScheduleCurrentRefresh();
        _structureInput.TextChanged += (_, _) => ScheduleCurrentRefresh();
        _manualTargetMHzInput.ValueChanged += (_, _) => ScheduleCurrentRefresh();
        _manualMarkerInput.CheckedChanged += (_, _) => ScheduleCurrentRefresh();
        // The old frequency widgets remain only as adapters for the specialist panel internals.
        foreach (var control in new Control[] { _propagationFrequencyMHzInput, _propagationSweepFrequencyCombo, _propagationUseCenterButton })
            control.Enabled = false;
        _singleAntennaFrequencyMHzInput.Enabled = false;
        _singleAntennaUseCurrentStructureInput.Checked = true;
        _singleAntennaUseCurrentStructureInput.Enabled = false;
        _singleAntennaExploreStartMHzInput.Enabled = false;
        _singleAntennaExploreStopMHzInput.Enabled = false;
        _singleAntennaExploreStepMHzInput.Enabled = false;
        _propagationSpacingMmInput.Enabled = false;
        _antennaSpacingMmInput.Enabled = false;
        _applyBestButton.Enabled = false;
        _exportJsonButton.Enabled = false;
        _exportCsvButton.Enabled = false;
        _workflowReady = true;
        FormClosed += (_, _) => { _optimizationCancellation.Cancel(); _manualLiveUpdateTimer.Dispose(); };
    }

    private void UpdateInspectionControls()
    {
        var frequency = ReadFrequency();
        frequency.Validate();
        SetNumericValue(_propagationSpacingMmInput, _manualSpacingMmInput.Value);
        SetNumericValue(_antennaSpacingMmInput, _manualSpacingMmInput.Value);
        // A range change makes an out-of-range manual marker fall back explicitly to the midpoint.
        if (_manualMarkerInput.Checked && ((double)_manualTargetMHzInput.Value < frequency.StartMHz
            || (double)_manualTargetMHzInput.Value > frequency.StopMHz))
            _manualMarkerInput.Checked = false;
        _manualTargetMHzInput.Maximum = 10000;
        _manualTargetMHzInput.Minimum = (decimal)frequency.StartMHz;
        _manualTargetMHzInput.Maximum = (decimal)frequency.StopMHz;
        if (!_manualMarkerInput.Checked) _manualTargetMHzInput.Value = (decimal)frequency.MidpointMHz;
        _manualTargetMHzInput.Enabled = _manualMarkerInput.Checked;
        SetNumericValue(_singleAntennaExploreStartMHzInput, (decimal)frequency.StartMHz);
        SetNumericValue(_singleAntennaExploreStopMHzInput, (decimal)frequency.StopMHz);
        // Exploration uses the shared sweep grid too.
        _singleAntennaExploreStepMHzInput.DecimalPlaces = 9;
        _singleAntennaExploreStepMHzInput.Minimum = 0.000000001m;
        _singleAntennaExploreStepMHzInput.Maximum = 10000;
        _singleAntennaExploreStepMHzInput.Value = (decimal)((frequency.StopMHz - frequency.StartMHz) / (frequency.Points - 1));
    }

    private void ScheduleCurrentRefresh()
    {
        if (!_workflowReady || _refreshingWorkspace) return;
        try { CaptureCurrentState(); }
        catch { InvalidateCandidates(); }
        _inspectionGeneration++;
        _terminalCouplingCandidate = null;
        _singleAntennaApplyTerminalButton.Enabled = false;
        ClearCurrentResults("Out of date — recalculating current circuit…");
        _manualLiveUpdateTimer.Stop();
        _manualLiveUpdateTimer.Start();
    }

    private void ClearCurrentResults(string message)
    {
        _lastSweep = new();
        _lastAntennaSweep = new();
        _lastSingleAntennaResult = null;
        _statusLabel.Text = message;
        _manualStatusLabel.Text = message;
        _manualMetricsText.Text = message;
        _propagationSummaryText.Text = message;
        _antennaSummaryText.Text = message;
        _singleAntennaRadiationSummaryText.Text = message;
        foreach (var field in GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            if (field.GetValue(this) is PlotView plot && plot != _candidateS11Preview && plot != _candidateDelayPreview)
                plot.Model = null;
    }

    private void InvalidateCandidates()
    {
        _configurationGeneration++;
        _optimizationCancellation.Cancel();
        _candidateGeneration = -1;
        _candidateResults.Clear();
        _resultsGrid.Rows.Clear();
        _candidateS11Preview.Model = null;
        _candidateDelayPreview.Model = null;
        _candidatePreviewText.Text = "No current candidates. Circuit, sweep, locks or goals changed; run optimization again.";
        _optimizerStatusLabel.Text = "Configuration changed. Previous candidates are out of date.";
        _applyBestButton.Enabled = false;
        _exportJsonButton.Enabled = false;
        _exportCsvButton.Enabled = false;
        _terminalCouplingCandidate = null;
        _singleAntennaApplyTerminalButton.Enabled = false;
        _bestAntennaCandidate = null;
        _antennaApplyBestButton.Enabled = false;
        _lastExplorationCases.Clear();
        _singleAntennaExportTopResultsButton.Enabled = false;
    }

    private void ConfigureOptimizerAdvanced(FlowLayoutPanel panel)
    {
        ConfigureNumeric(_randomSeedInput, 0, int.MaxValue, 12345, 0, 1);
        var seedRow = new FlowLayoutPanel { Size = new Size(360, 32) };
        seedRow.Controls.Add(new Label { Text = "Random seed", Width = 150 });
        _randomSeedInput.Dock = DockStyle.None;
        _randomSeedInput.Width = 195;
        seedRow.Controls.Add(_randomSeedInput);
        panel.Controls.Add(seedRow);
        var advancedControls = new Control[] { _phaseObjectiveWeightInput.Parent!, _groupDelayObjectiveWeightInput.Parent!,
            _maxIterationsInput.Parent!, _randomRestartsInput.Parent!, seedRow, _randomSearchButton };
        // Topology is preserved for both current-circuit search modes. The old method combo was unused.
        _structureLengthCombo.Parent!.Visible = false;
        _optimizationMethodCombo.Parent!.Visible = false;
        var advanced = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        foreach (var control in advancedControls) advanced.Controls.Add(control);
        _randomSearchButton.Text = "Random restarts (current topology + locks)";
        panel.Controls.Add(_optimizerAdvancedInput);
        panel.Controls.Add(advanced);
        panel.Controls.SetChildIndex(_optimizerAdvancedInput, 8);
        panel.Controls.SetChildIndex(advanced, 9);
        _optimizerAdvancedInput.CheckedChanged += (_, _) => advanced.Visible = _optimizerAdvancedInput.Checked;
        foreach (var control in new NumericUpDown[] { _targetS11DbInput, _minS21DbInput, _targetPhaseRotationDegInput,
            _phaseObjectiveWeightInput, _groupDelayObjectiveWeightInput, _maxIterationsInput, _randomRestartsInput, _randomSeedInput })
            control.ValueChanged += (_, _) => { if (_workflowReady) InvalidateCandidates(); };
        _enablePhaseObjectiveInput.CheckedChanged += (_, _) => { if (_workflowReady) InvalidateCandidates(); };
        _enableGroupDelayObjectiveInput.CheckedChanged += (_, _) => { if (_workflowReady) InvalidateCandidates(); };
    }

    private Control BuildCandidatePreviewPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(_candidatePreviewText, 0, 0);
        panel.SetColumnSpan(_candidatePreviewText, 2);
        ConfigurePlotView(_candidateS11Preview);
        ConfigurePlotView(_candidateDelayPreview);
        panel.Controls.Add(_candidateS11Preview, 0, 1);
        panel.Controls.Add(_candidateDelayPreview, 1, 1);
        return panel;
    }

    private void PreviewCandidate(OptimizerEngine.CandidateResult candidate)
    {
        if (_candidateGeneration != _configurationGeneration || _candidateConfig is null) return;
        var improvement = candidate.Score < _candidateBaseline!.Score;
        var changed = candidate.Structure.Zip(_candidateBaselineSnapshot!.Structure).Count(p => p.First.Value != p.Second.Value);
        var text = $"CANDIDATE PREVIEW — current circuit unchanged. Apply Candidate to use this circuit.\r\n"
            + $"Baseline r{_candidateBaselineSnapshot.Revision} / {_candidateBaselineSnapshot.CircuitId[..8]} | {_candidateConfig.StartHz / 1e6:g}–{_candidateConfig.StopHz / 1e6:g} MHz, {_candidateConfig.Points} points, {_candidateConfig.Z0:g} ohm | Seed {_candidateConfig.RandomSeed}\r\n"
            + $"Score (lower is better): baseline {_candidateBaseline.Score:F6} → candidate {candidate.Score:F6}. {(improvement ? "IMPROVES baseline" : "NO improvement")}; {changed} component values changed.\r\n"
            + $"{(candidate.Feasible ? "Performance goals met" : "GOALS UNMET: " + string.Join("; ", candidate.ConstraintViolations))}\r\n"
            + $"Candidate band mean of positive group-delay samples: {candidate.AverageGroupDelayNs:F3} ns. Current marker delay is a single-point value.\r\n"
            + "Baseline is dashed; candidate is solid. Component values are exact in export.\r\n"
            + FormatStructureInline(candidate.Structure);
        _candidatePreviewText.Text = text;
        _optimizerStatusLabel.Text = $"Candidate: {(improvement ? "improves baseline score" : "no improvement")}. {(candidate.Feasible ? "Goals met." : "GOALS UNMET: " + string.Join("; ", candidate.ConstraintViolations))}\r\nSee Candidate Preview tab. Current circuit unchanged.";
        _candidateS11Preview.Model = AddManualFrequencyBaseline(PlotFactory.BuildS11DbModel(candidate.SweepCache), _candidateBaseline.SweepCache, p => RfMath.ToDbMagnitude(p.S11));
        _candidateDelayPreview.Model = AddManualFrequencyBaseline(PlotFactory.BuildGroupDelayModel(candidate.SweepCache), _candidateBaseline.SweepCache, RfMath.CalculateGroupDelayNs(_candidateBaseline.SweepCache));
    }

    private async void StartSharedOptimization(bool randomRestarts)
    {
        if (_optimizationRunning) return;
        try
        {
            var config = GetOptimizerConfig();
            var snapshot = _workspace.Current!;
            InvalidateCandidates();
            var generation = _configurationGeneration;
            _optimizationCancellation = new CancellationTokenSource();
            var token = _optimizationCancellation.Token;
            _optimizationRunning = true;
            _optimizeCurrentButton.Enabled = _randomSearchButton.Enabled = false;
            _stopOptimizationButton.Enabled = true;
            _optimizerStatusLabel.Text = $"Optimizing r{snapshot.Revision}, seed {config.RandomSeed}. Current circuit unchanged.";
            var result = await Task.Run(() =>
            {
                List<SweepPoint> Evaluate(List<RfElement> s) => SimulationEngine.Simulate(config.StartHz, config.StopHz, config.Points, config.Z0, s);
                var baseline = OptimizerEngine.EvaluateStructure(snapshot.Structure, Evaluate, config);
                var candidates = randomRestarts
                    ? OptimizerEngine.RandomRestartLocalSearch(Evaluate, config, cancellationToken: token)
                    : new List<OptimizerEngine.CandidateResult> { OptimizerEngine.SequentialLocalSearch(snapshot.Structure, Evaluate, config, cancellationToken: token) };
                return (baseline, candidates);
            }, token);
            token.ThrowIfCancellationRequested();
            PublishCandidates(generation, snapshot, config, result.baseline, result.candidates);
        }
        catch (OperationCanceledException) { _optimizerStatusLabel.Text = "Optimization cancelled; no candidate applied."; }
        catch (Exception ex) { _optimizerStatusLabel.Text = "Optimization error: " + ex.Message; }
        finally
        {
            _optimizationRunning = false;
            _optimizeCurrentButton.Enabled = _randomSearchButton.Enabled = true;
            _stopOptimizationButton.Enabled = false;
        }
    }

    private void PublishCandidates(long generation, CircuitSnapshot snapshot, OptimizerEngine.OptimizerConfig config,
        OptimizerEngine.CandidateResult baseline, List<OptimizerEngine.CandidateResult> candidates)
    {
        if (generation != _configurationGeneration || snapshot.Revision != _workspace.Revision) return;
        _candidateGeneration = generation;
        _candidateConfig = config;
        _candidateBaselineSnapshot = snapshot;
        _candidateBaseline = baseline;
        _candidateResults = candidates;
        UpdateResultsGrid();
        if (candidates.Count > 0) DisplayCandidateAt(0);
        _applyBestButton.Enabled = candidates.Count > 0;
        _exportJsonButton.Enabled = _exportCsvButton.Enabled = candidates.Count > 0;
    }

    private void LoadSharedProject(string json)
    {
        var (config, notice) = ProjectFiles.Read(json, ReadFrequency());
        if (config.StartMHz < (double)_startMHzInput.Minimum || config.StopMHz > (double)_stopMHzInput.Maximum
            || config.Z0 < (double)_z0Input.Minimum || config.Z0 > (double)_z0Input.Maximum
            || !double.IsFinite(config.CellSpacingMm) || config.CellSpacingMm < (double)_manualSpacingMmInput.Minimum
            || config.CellSpacingMm > (double)_manualSpacingMmInput.Maximum)
            throw new InvalidOperationException("Saved frequency or reference impedance is outside the supported UI range.");
        _refreshingWorkspace = true;
        try
        {
            _startMHzInput.Value = (decimal)config.StartMHz;
            _stopMHzInput.Value = (decimal)config.StopMHz;
            _pointsInput.Value = config.Points;
            _z0Input.Value = (decimal)config.Z0;
            _manualSpacingMmInput.Value = (decimal)config.CellSpacingMm;
            _manualMarkerInput.Checked = false;
            UpdateInspectionControls();
            if (config.ManualMarkerMHz is { } marker)
            {
                _manualMarkerInput.Checked = true;
                _manualTargetMHzInput.Value = (decimal)marker;
            }
            _presetCombo.SelectedIndex = -1;
            SetManualStructure(config.Structure, true);
            foreach (var index in config.LockedIndices) _manualElements[index].Locked = true;
            RefreshManualElementsGrid();
        }
        finally { _refreshingWorkspace = false; }
        ScheduleCurrentRefresh();
        RunSimulation();
        _statusLabel.Text += " | " + notice;
    }

    private string AddCurrentContextToCsv(string csv)
    {
        var context = JsonSerializer.Serialize(CurrentExportConfiguration()).Replace("\"", "\"\"");
        var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        return string.Join(Environment.NewLine, lines.Select((line, index) => index == 0
            ? line + ",CurrentConfigurationJson" : line + ",\"" + context + "\""));
    }

    private string BuildCandidateCsv()
    {
        static string Cell(object? value) => "\"" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { "Rank,Score,BaselineScore,ImprovesBaseline,Feasible,Violations,BaselineCircuitId,BaselineRevision,StartMHz,StopMHz,Points,Z0,Seed,AveragePositiveGroupDelayNs,StructureJson,OptimizerConfigJson" };
        lines.AddRange(_candidateResults.Select((c, i) => string.Join(",", new object?[] {
            i + 1, c.Score, _candidateBaseline?.Score, c.Score < _candidateBaseline?.Score, c.Feasible,
            string.Join("; ", c.ConstraintViolations), _candidateBaselineSnapshot?.CircuitId, _candidateBaselineSnapshot?.Revision,
            _candidateConfig?.StartHz / 1e6, _candidateConfig?.StopHz / 1e6, _candidateConfig?.Points, _candidateConfig?.Z0,
            _candidateConfig?.RandomSeed, c.AverageGroupDelayNs, JsonSerializer.Serialize(c.Structure), JsonSerializer.Serialize(_candidateConfig)
        }.Select(Cell))));
        return string.Join(Environment.NewLine, lines);
    }

    private PlotModel BuildMarkedPolarModel(IReadOnlyList<SweepPoint> sweep)
    {
        var model = PlotFactory.BuildS21PolarModel(sweep);
        var point = sweep[FindManualNearestSweepIndex(sweep, _resolvedMarkerMHz * 1e6)];
        var marker = new ScatterSeries { MarkerType = MarkerType.Circle, MarkerFill = OxyColors.IndianRed, MarkerSize = 4 };
        marker.Points.Add(new ScatterPoint(point.S21.Real, point.S21.Imaginary));
        model.Series.Add(marker);
        return model;
    }
}
