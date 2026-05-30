using System.Text.Json;
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
    private readonly TextBox _structureSummaryInput = new();
    private readonly Button _simulateButton = new();
    private readonly Button _saveJsonButton = new();
    private readonly Button _loadJsonButton = new();
    private readonly Button _exportTouchstoneButton = new();
    private readonly Label _statusLabel = new();

    private readonly PlotView _smithPlot = new();
    private readonly PlotView _s11DbPlot = new();
    private readonly PlotView _s21PolarPlot = new();
    private readonly PlotView _s21DbPlot = new();
    private readonly PlotView _s21PhasePlot = new();
    private readonly PlotView _groupDelayPlot = new();

    private readonly NumericUpDown _propagationSpacingMmInput = new();
    private readonly NumericUpDown _propagationFrequencyMHzInput = new();
    private readonly ComboBox _propagationSweepFrequencyCombo = new();
    private readonly Button _propagationUseCenterButton = new();
    private readonly Button _propagationUpdateButton = new();
    private readonly TextBox _propagationSummaryText = new();
    private readonly Label _propagationStatusLabel = new();
    private readonly PlotView _nodeVoltagePlot = new();
    private readonly PlotView _nodePhasePlot = new();
    private readonly PlotView _localPhaseShiftPlot = new();
    private readonly PlotView _slowWaveFactorPlot = new();
    private readonly PlotView _storedEnergyPlot = new();

    private readonly NumericUpDown _antennaRradInput = new();
    private readonly NumericUpDown _antennaRlossInput = new();
    private readonly NumericUpDown _antennaXantInput = new();
    private readonly NumericUpDown _antennaStartMHzInput = new();
    private readonly NumericUpDown _antennaStopMHzInput = new();
    private readonly NumericUpDown _antennaPointsInput = new();
    private readonly NumericUpDown _antennaSpacingMmInput = new();
    private readonly NumericUpDown _antennaTargetReturnLossInput = new();
    private readonly NumericUpDown _antennaMaxVoltageInput = new();
    private readonly NumericUpDown _antennaOptimizerIterationsInput = new();
    private readonly NumericUpDown _antennaLauncherCellsInput = new();
    private readonly NumericUpDown _antennaMaxAverageReflectionInput = new();
    private readonly CheckBox _antennaUseContinuousInput = new();
    private readonly CheckBox _antennaSmoothGradientInput = new();
    private readonly NumericUpDown _antennaLMinNhInput = new();
    private readonly NumericUpDown _antennaLMaxNhInput = new();
    private readonly NumericUpDown _antennaCMinPfInput = new();
    private readonly NumericUpDown _antennaCMaxPfInput = new();
    private readonly Button _antennaSimulateButton = new();
    private readonly Button _antennaOptimizeButton = new();
    private readonly Button _antennaOptimizeCompressionButton = new();
    private readonly Button _antennaApplyBestButton = new();
    private readonly Button _antennaExportBestJsonButton = new();
    private readonly TextBox _antennaSummaryText = new();
    private readonly Label _antennaStatusLabel = new();
    private readonly PlotView _antennaReturnLossPlot = new();
    private readonly PlotView _antennaPowerPlot = new();
    private readonly PlotView _antennaCurrentPlot = new();
    private readonly PlotView _antennaVoltagePlot = new();
    private readonly PlotView _antennaNodeVoltagePlot = new();
    private readonly PlotView _antennaNodePhasePlot = new();
    private readonly PlotView _antennaLocalPhaseShiftPlot = new();
    private readonly PlotView _antennaSlowWaveFactorPlot = new();
    private readonly PlotView _antennaStoredEnergyPlot = new();

    // Optimizer controls
    private readonly NumericUpDown _optStartMHzInput = new();
    private readonly NumericUpDown _optStopMHzInput = new();
    private readonly NumericUpDown _optPointsInput = new();
    private readonly NumericUpDown _targetS11DbInput = new();
    private readonly NumericUpDown _minS21DbInput = new();
    private readonly NumericUpDown _targetPhaseRotationDegInput = new();
    private readonly CheckBox _enablePhaseObjectiveInput = new();
    private readonly CheckBox _enableGroupDelayObjectiveInput = new();
    private readonly NumericUpDown _phaseObjectiveWeightInput = new();
    private readonly NumericUpDown _groupDelayObjectiveWeightInput = new();
    private readonly ComboBox _structureLengthCombo = new();
    private readonly ComboBox _optimizationMethodCombo = new();
    private readonly NumericUpDown _randomRestartsInput = new();
    private readonly NumericUpDown _maxIterationsInput = new();
    private readonly Button _optimizeCurrentButton = new();
    private readonly Button _randomSearchButton = new();
    private readonly Button _stopOptimizationButton = new();
    private readonly Button _applyBestButton = new();
    private readonly Button _exportJsonButton = new();
    private readonly Button _exportCsvButton = new();
    private readonly DataGridView _resultsGrid = new();
    private readonly Label _optimizerStatusLabel = new();

    private List<OptimizerEngine.CandidateResult> _candidateResults = new();
    private List<SweepPoint> _lastSweep = new();
    private List<SmallAntennaPoint> _lastAntennaSweep = new();
    private SmallAntennaCandidate? _bestAntennaCandidate;
    private SmallAntennaObjectiveMode _lastAntennaObjectiveMode = SmallAntennaObjectiveMode.AntennaMatch;
    private int _displayedCandidateIndex = -1;
    private bool _optimizationRunning = false;
    private CancellationTokenSource _optimizationCancellation = new();

    public Form1()
    {
        InitializeComponent();
        BuildUi();
        RunSimulation();
    }

    private void BuildUi()
    {
        Text = "RF LC Structure Simulator & Optimizer";
        WindowState = FormWindowState.Maximized;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(8)
        };
        root.BackColor = SystemColors.Control;
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var simulatorRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 8, 0)
        };
        simulatorRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        simulatorRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var controlsPanel = BuildControlsPanel();
        var analysisTabs = BuildAnalysisTabs();

        simulatorRoot.Controls.Add(controlsPanel, 0, 0);
        simulatorRoot.Controls.Add(analysisTabs, 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 10,
            FixedPanel = FixedPanel.None
        };
        split.BackColor = SystemColors.Control;
        split.Panel1.Controls.Add(simulatorRoot);
        split.Panel2.Controls.Add(BuildOptimizerPanel());
        Shown += (_, _) =>
        {
            split.Panel1MinSize = Math.Min(650, Math.Max(25, split.Width / 3));
            split.Panel2MinSize = Math.Min(520, Math.Max(25, split.Width / 4));
            var maxDistance = split.Width - split.Panel2MinSize - split.SplitterWidth;
            if (maxDistance > split.Panel1MinSize)
            {
                var preferredDistance = (int)(split.Width * 0.62);
                split.SplitterDistance = Math.Min(preferredDistance, maxDistance);
            }
        };

        root.Controls.Add(split, 0, 0);
        Controls.Add(root);
    }

    private Control BuildAnalysisTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        var sParameterTab = new TabPage("S-Parameters");
        sParameterTab.Controls.Add(BuildPlotsGrid());

        var propagationTab = new TabPage("Propagation Analysis");
        propagationTab.Controls.Add(BuildPropagationAnalysisPanel());

        var antennaTab = new TabPage("Small Antenna Mode");
        antennaTab.Controls.Add(BuildSmallAntennaModePanel());

        tabs.TabPages.Add(sParameterTab);
        tabs.TabPages.Add(propagationTab);
        tabs.TabPages.Add(antennaTab);
        return tabs;
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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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

        _exportTouchstoneButton.Text = "Export Touchstone .s2p";
        _exportTouchstoneButton.Size = new Size(360, 30);
        _exportTouchstoneButton.Click += (_, _) => ExportTouchstone();

        var actionButtonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        actionButtonsPanel.Controls.Add(_simulateButton);
        actionButtonsPanel.Controls.Add(_saveJsonButton);
        actionButtonsPanel.Controls.Add(_loadJsonButton);

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Text = "Ready";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.BorderStyle = BorderStyle.FixedSingle;
        _statusLabel.Padding = new Padding(4, 0, 4, 0);

        _structureInput.Multiline = true;
        _structureInput.ScrollBars = ScrollBars.Both;
        _structureInput.AcceptsReturn = true;
        _structureInput.AcceptsTab = true;
        _structureInput.Font = new Font("Consolas", 9);

        _structureSummaryInput.ReadOnly = true;
        _structureSummaryInput.Dock = DockStyle.Fill;
        _structureSummaryInput.BackColor = SystemColors.Window;
        _structureSummaryInput.BorderStyle = BorderStyle.FixedSingle;

        panel.Controls.Add(new Label { Text = "Start MHz", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.Controls.Add(_startMHzInput, 1, 0);
        panel.Controls.Add(new Label { Text = "Stop MHz", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        panel.Controls.Add(_stopMHzInput, 3, 0);
        panel.Controls.Add(new Label { Text = "Points", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);
        panel.Controls.Add(_pointsInput, 5, 0);
        panel.Controls.Add(new Label { Text = "Z0 (Ohm)", AutoSize = true, Anchor = AnchorStyles.Left }, 6, 0);
        panel.Controls.Add(_z0Input, 7, 0);
        panel.Controls.Add(new Label { Text = "Validation", AutoSize = true, Anchor = AnchorStyles.Left }, 8, 0);
        panel.Controls.Add(_presetCombo, 9, 0);
        panel.Controls.Add(actionButtonsPanel, 10, 0);
        panel.SetColumnSpan(actionButtonsPanel, 2);

        panel.Controls.Add(new Label { Text = "Structure", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        panel.Controls.Add(_structureSummaryInput, 1, 1);
        panel.SetColumnSpan(_structureSummaryInput, 11);

        panel.Controls.Add(new Label { Text = "Candidate", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        panel.Controls.Add(_statusLabel, 1, 2);
        panel.SetColumnSpan(_statusLabel, 11);

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
            RowCount = 3,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };

        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 24));

        ConfigurePlotView(_smithPlot);
        ConfigurePlotView(_s11DbPlot);
        ConfigurePlotView(_s21PolarPlot);
        ConfigurePlotView(_s21DbPlot);
        ConfigurePlotView(_s21PhasePlot);
        ConfigurePlotView(_groupDelayPlot);

        plots.Controls.Add(_smithPlot, 0, 0);
        plots.Controls.Add(_s11DbPlot, 1, 0);
        plots.Controls.Add(_s21PolarPlot, 0, 1);
        plots.Controls.Add(_s21DbPlot, 1, 1);
        plots.Controls.Add(_s21PhasePlot, 0, 2);
        plots.Controls.Add(_groupDelayPlot, 1, 2);
        plots.Margin = new Padding(0);
        plots.Padding = new Padding(0);

        return plots;
    }

    private Control BuildPropagationAnalysisPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildPropagationControlsPanel(), 0, 0);

        _propagationSummaryText.ReadOnly = true;
        _propagationSummaryText.Multiline = true;
        _propagationSummaryText.ScrollBars = ScrollBars.Vertical;
        _propagationSummaryText.Dock = DockStyle.Fill;
        _propagationSummaryText.Font = new Font("Consolas", 9);
        _propagationSummaryText.BackColor = SystemColors.Window;
        root.Controls.Add(_propagationSummaryText, 0, 1);

        var plots = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 33));

        ConfigurePlotView(_nodeVoltagePlot);
        ConfigurePlotView(_nodePhasePlot);
        ConfigurePlotView(_localPhaseShiftPlot);
        ConfigurePlotView(_slowWaveFactorPlot);
        ConfigurePlotView(_storedEnergyPlot);

        plots.Controls.Add(_nodeVoltagePlot, 0, 0);
        plots.Controls.Add(_nodePhasePlot, 1, 0);
        plots.Controls.Add(_localPhaseShiftPlot, 0, 1);
        plots.Controls.Add(_slowWaveFactorPlot, 1, 1);
        plots.Controls.Add(_storedEnergyPlot, 0, 2);
        plots.SetColumnSpan(_storedEnergyPlot, 2);
        root.Controls.Add(plots, 0, 2);

        return root;
    }

    private Control BuildPropagationControlsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 9,
            RowCount = 1,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 6)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ConfigureNumeric(_propagationSpacingMmInput, 0.001m, 1000, 5, 3, 0.5m);
        ConfigureNumeric(_propagationFrequencyMHzInput, 0.001m, 10000, 27.5m, 6, 0.1m);

        _propagationSweepFrequencyCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _propagationSweepFrequencyCombo.Dock = DockStyle.Fill;
        _propagationSweepFrequencyCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_propagationSweepFrequencyCombo.SelectedItem is SweepFrequencyItem item)
            {
                SetNumericValue(_propagationFrequencyMHzInput, (decimal)(item.FrequencyHz / 1e6));
                UpdatePropagationAnalysis();
            }
        };

        _propagationUseCenterButton.Text = "Use Sweep Center";
        _propagationUseCenterButton.AutoSize = true;
        _propagationUseCenterButton.Click += (_, _) =>
        {
            SetPropagationFrequencyToSweepCenter();
            UpdatePropagationAnalysis();
        };

        _propagationUpdateButton.Text = "Update";
        _propagationUpdateButton.AutoSize = true;
        _propagationUpdateButton.Click += (_, _) => UpdatePropagationAnalysis();

        _propagationStatusLabel.AutoSize = false;
        _propagationStatusLabel.Dock = DockStyle.Fill;
        _propagationStatusLabel.AutoEllipsis = true;
        _propagationStatusLabel.Text = "Run simulation to populate propagation analysis.";
        _propagationStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _propagationStatusLabel.BorderStyle = BorderStyle.FixedSingle;
        _propagationStatusLabel.Padding = new Padding(4, 0, 4, 0);

        panel.Controls.Add(new Label { Text = "Cell spacing mm", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.Controls.Add(_propagationSpacingMmInput, 1, 0);
        panel.Controls.Add(new Label { Text = "Frequency MHz", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        panel.Controls.Add(_propagationFrequencyMHzInput, 3, 0);
        panel.Controls.Add(_propagationSweepFrequencyCombo, 4, 0);
        panel.Controls.Add(_propagationUseCenterButton, 5, 0);
        panel.Controls.Add(_propagationUpdateButton, 6, 0);
        panel.Controls.Add(_propagationStatusLabel, 7, 0);
        panel.SetColumnSpan(_propagationStatusLabel, 2);

        return panel;
    }

    private Control BuildSmallAntennaModePanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildSmallAntennaControlsPanel(), 0, 0);

        _antennaSummaryText.ReadOnly = true;
        _antennaSummaryText.Multiline = true;
        _antennaSummaryText.ScrollBars = ScrollBars.Vertical;
        _antennaSummaryText.Dock = DockStyle.Fill;
        _antennaSummaryText.Font = new Font("Consolas", 9);
        _antennaSummaryText.BackColor = SystemColors.Window;
        root.Controls.Add(_antennaSummaryText, 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var sweepTab = new TabPage("Antenna Sweep");
        sweepTab.Controls.Add(BuildSmallAntennaSweepPlots());
        var compressionTab = new TabPage("Compression View");
        compressionTab.Controls.Add(BuildSmallAntennaCompressionPlots());
        tabs.TabPages.Add(sweepTab);
        tabs.TabPages.Add(compressionTab);
        root.Controls.Add(tabs, 0, 2);

        return root;
    }

    private Control BuildSmallAntennaControlsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 6)
        };

        ConfigureNumeric(_antennaRradInput, 0, 10000, 2, 3, 0.5m);
        ConfigureNumeric(_antennaRlossInput, 0, 10000, 1, 3, 0.5m);
        ConfigureNumeric(_antennaXantInput, -10000, 10000, -300, 2, 10);
        ConfigureNumeric(_antennaStartMHzInput, 0.001m, 10000, 5, 3, 1);
        ConfigureNumeric(_antennaStopMHzInput, 0.001m, 10000, 50, 3, 1);
        ConfigureNumeric(_antennaPointsInput, 11, 5001, 401, 0, 10);
        ConfigureNumeric(_antennaSpacingMmInput, 0.001m, 1000, 5, 3, 0.5m);
        ConfigureNumeric(_antennaTargetReturnLossInput, -80, 0, -10, 1, 0.5m);
        ConfigureNumeric(_antennaMaxVoltageInput, 1, 1000000, 500, 1, 50);
        ConfigureNumeric(_antennaOptimizerIterationsInput, 1, 500, 35, 0, 5);
        ConfigureNumeric(_antennaLauncherCellsInput, 0, 20, 2, 0, 1);
        ConfigureNumeric(_antennaMaxAverageReflectionInput, 0, 100, 50, 1, 5);
        ConfigureNumeric(_antennaLMinNhInput, 0.001m, 1000000, 10, 3, 10);
        ConfigureNumeric(_antennaLMaxNhInput, 0.001m, 1000000, 2000, 3, 10);
        ConfigureNumeric(_antennaCMinPfInput, 0.001m, 1000000, 0.5m, 3, 1);
        ConfigureNumeric(_antennaCMaxPfInput, 0.001m, 1000000, 470, 3, 1);

        AddCompactField(panel, "Rrad ohm", _antennaRradInput, 78);
        AddCompactField(panel, "Rloss ohm", _antennaRlossInput, 78);
        AddCompactField(panel, "Xant ohm", _antennaXantInput, 88);
        AddCompactField(panel, "Start MHz", _antennaStartMHzInput, 82);
        AddCompactField(panel, "Stop MHz", _antennaStopMHzInput, 82);
        AddCompactField(panel, "Points", _antennaPointsInput, 68);
        AddCompactField(panel, "Spacing mm", _antennaSpacingMmInput, 78);
        AddCompactField(panel, "Target RL dB", _antennaTargetReturnLossInput, 78);
        AddCompactField(panel, "Max V", _antennaMaxVoltageInput, 82);
        AddCompactField(panel, "Opt iters", _antennaOptimizerIterationsInput, 68);
        AddCompactField(panel, "Launcher cells", _antennaLauncherCellsInput, 68);
        AddCompactField(panel, "Max avg refl %", _antennaMaxAverageReflectionInput, 72);
        AddCompactField(panel, "L min nH", _antennaLMinNhInput, 78);
        AddCompactField(panel, "L max nH", _antennaLMaxNhInput, 78);
        AddCompactField(panel, "C min pF", _antennaCMinPfInput, 78);
        AddCompactField(panel, "C max pF", _antennaCMaxPfInput, 78);

        _antennaUseContinuousInput.Text = "Use continuous L/C values";
        _antennaUseContinuousInput.AutoSize = true;
        _antennaUseContinuousInput.Margin = new Padding(4, 4, 8, 0);
        panel.Controls.Add(_antennaUseContinuousInput);

        _antennaSmoothGradientInput.Text = "Smooth increasing gradient";
        _antennaSmoothGradientInput.AutoSize = true;
        _antennaSmoothGradientInput.Margin = new Padding(4, 4, 8, 0);
        panel.Controls.Add(_antennaSmoothGradientInput);

        _antennaSimulateButton.Text = "Analyze Antenna";
        _antennaSimulateButton.AutoSize = true;
        _antennaSimulateButton.Click += (_, _) => UpdateSmallAntennaMode();
        panel.Controls.Add(_antennaSimulateButton);

        _antennaOptimizeButton.Text = "Optimize Antenna Match";
        _antennaOptimizeButton.AutoSize = true;
        _antennaOptimizeButton.Click += (_, _) => OptimizeSmallAntennaMode(SmallAntennaObjectiveMode.AntennaMatch);
        panel.Controls.Add(_antennaOptimizeButton);

        _antennaOptimizeCompressionButton.Text = "Optimize Smooth Compression Medium";
        _antennaOptimizeCompressionButton.AutoSize = true;
        _antennaOptimizeCompressionButton.Click += (_, _) => OptimizeSmallAntennaMode(SmallAntennaObjectiveMode.SmoothCompression);
        panel.Controls.Add(_antennaOptimizeCompressionButton);

        _antennaApplyBestButton.Text = "Apply Antenna Best";
        _antennaApplyBestButton.AutoSize = true;
        _antennaApplyBestButton.Enabled = false;
        _antennaApplyBestButton.Click += (_, _) => ApplyBestSmallAntennaCandidate();
        panel.Controls.Add(_antennaApplyBestButton);

        _antennaExportBestJsonButton.Text = "Export Antenna Best JSON";
        _antennaExportBestJsonButton.AutoSize = true;
        _antennaExportBestJsonButton.Enabled = false;
        _antennaExportBestJsonButton.Click += (_, _) => ExportBestSmallAntennaCandidateJson();
        panel.Controls.Add(_antennaExportBestJsonButton);

        _antennaStatusLabel.AutoSize = false;
        _antennaStatusLabel.Width = 480;
        _antennaStatusLabel.Height = 26;
        _antennaStatusLabel.Text = "Ready";
        _antennaStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _antennaStatusLabel.BorderStyle = BorderStyle.FixedSingle;
        _antennaStatusLabel.Padding = new Padding(4, 0, 4, 0);
        panel.Controls.Add(_antennaStatusLabel);

        return panel;
    }

    private static void AddCompactField(FlowLayoutPanel panel, string label, Control control, int width)
    {
        var row = new Panel { Width = width + 72, Height = 28, Margin = new Padding(0, 0, 4, 2) };
        row.Controls.Add(new Label
        {
            Text = label,
            Location = new Point(0, 3),
            Size = new Size(70, 22),
            TextAlign = ContentAlignment.MiddleLeft
        });
        control.Dock = DockStyle.None;
        control.Location = new Point(72, 1);
        control.Size = new Size(width, 24);
        row.Controls.Add(control);
        panel.Controls.Add(row);
    }

    private Control BuildSmallAntennaSweepPlots()
    {
        var plots = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        ConfigurePlotView(_antennaReturnLossPlot);
        ConfigurePlotView(_antennaPowerPlot);
        ConfigurePlotView(_antennaCurrentPlot);
        ConfigurePlotView(_antennaVoltagePlot);

        plots.Controls.Add(_antennaReturnLossPlot, 0, 0);
        plots.Controls.Add(_antennaPowerPlot, 1, 0);
        plots.Controls.Add(_antennaCurrentPlot, 0, 1);
        plots.Controls.Add(_antennaVoltagePlot, 1, 1);
        return plots;
    }

    private Control BuildSmallAntennaCompressionPlots()
    {
        var plots = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        plots.RowStyles.Add(new RowStyle(SizeType.Percent, 33));

        ConfigurePlotView(_antennaNodeVoltagePlot);
        ConfigurePlotView(_antennaNodePhasePlot);
        ConfigurePlotView(_antennaLocalPhaseShiftPlot);
        ConfigurePlotView(_antennaSlowWaveFactorPlot);
        ConfigurePlotView(_antennaStoredEnergyPlot);

        plots.Controls.Add(_antennaNodeVoltagePlot, 0, 0);
        plots.Controls.Add(_antennaNodePhasePlot, 1, 0);
        plots.Controls.Add(_antennaLocalPhaseShiftPlot, 0, 1);
        plots.Controls.Add(_antennaSlowWaveFactorPlot, 1, 1);
        plots.Controls.Add(_antennaStoredEnergyPlot, 0, 2);
        plots.SetColumnSpan(_antennaStoredEnergyPlot, 2);
        return plots;
    }

    private Control BuildOptimizerPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var controlsPanel = BuildOptimizerControls();
        var resultsPanel = BuildResultsPanel();

        panel.Controls.Add(controlsPanel, 0, 0);
        panel.Controls.Add(resultsPanel, 1, 0);

        return panel;
    }

    private Control BuildOptimizerControls()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };

        Action<string, Control> AddFieldRow = (label, control) =>
        {
            var row = new Panel { Size = new Size(360, 32), Margin = new Padding(0, 0, 0, 2) };
            var lbl = new Label
            {
                Text = label,
                Location = new Point(0, 3),
                Size = new Size(150, 22),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            control.Location = new Point(155, 2);
            control.Size = new Size(200, 25);
            row.Controls.Add(lbl);
            row.Controls.Add(control);
            panel.Controls.Add(row);
        };

        AddFieldRow("Opt. Start MHz", _optStartMHzInput);
        _optStartMHzInput.Minimum = 1;
        _optStartMHzInput.Maximum = 500;
        _optStartMHzInput.Value = 5;

        AddFieldRow("Opt. Stop MHz", _optStopMHzInput);
        _optStopMHzInput.Minimum = 1;
        _optStopMHzInput.Maximum = 500;
        _optStopMHzInput.Value = 50;

        AddFieldRow("Sweep Points", _optPointsInput);
        _optPointsInput.Minimum = 11;
        _optPointsInput.Maximum = 5001;
        _optPointsInput.DecimalPlaces = 0;
        _optPointsInput.Value = 401;

        AddFieldRow("Target S11 (dB)", _targetS11DbInput);
        _targetS11DbInput.Minimum = -40;
        _targetS11DbInput.Maximum = 0;
        _targetS11DbInput.DecimalPlaces = 1;
        _targetS11DbInput.Increment = 0.5m;
        _targetS11DbInput.Value = -15;

        AddFieldRow("Min S21 (dB)", _minS21DbInput);
        _minS21DbInput.Minimum = -40;
        _minS21DbInput.Maximum = 0;
        _minS21DbInput.DecimalPlaces = 1;
        _minS21DbInput.Increment = 0.5m;
        _minS21DbInput.Value = -3;

        AddFieldRow("Target Phase (deg)", _targetPhaseRotationDegInput);
        _targetPhaseRotationDegInput.Minimum = 0;
        _targetPhaseRotationDegInput.Maximum = 3600;
        _targetPhaseRotationDegInput.DecimalPlaces = 0;
        _targetPhaseRotationDegInput.Increment = 90;
        _targetPhaseRotationDegInput.Value = 180;

        _enablePhaseObjectiveInput.Text = "Enable phase objective";
        _enablePhaseObjectiveInput.Checked = true;
        _enablePhaseObjectiveInput.AutoSize = true;
        AddFieldRow("Phase Objective", _enablePhaseObjectiveInput);

        _enableGroupDelayObjectiveInput.Text = "Enable group delay objective";
        _enableGroupDelayObjectiveInput.Checked = true;
        _enableGroupDelayObjectiveInput.AutoSize = true;
        AddFieldRow("Group Delay", _enableGroupDelayObjectiveInput);

        AddFieldRow("Phase Weight", _phaseObjectiveWeightInput);
        _phaseObjectiveWeightInput.Minimum = 0;
        _phaseObjectiveWeightInput.Maximum = 100;
        _phaseObjectiveWeightInput.DecimalPlaces = 2;
        _phaseObjectiveWeightInput.Increment = 0.25m;
        _phaseObjectiveWeightInput.Value = 1;

        AddFieldRow("Delay Weight", _groupDelayObjectiveWeightInput);
        _groupDelayObjectiveWeightInput.Minimum = 0;
        _groupDelayObjectiveWeightInput.Maximum = 100;
        _groupDelayObjectiveWeightInput.DecimalPlaces = 2;
        _groupDelayObjectiveWeightInput.Increment = 0.25m;
        _groupDelayObjectiveWeightInput.Value = 1;

        AddFieldRow("Structure Length", _structureLengthCombo);
        _structureLengthCombo.Items.AddRange(new object[] { "3L/2C (5 elem)", "5L/4C (9 elem)", "7L/6C (13 elem)", "9L/8C (17 elem)" });
        _structureLengthCombo.SelectedIndex = 0;
        _structureLengthCombo.DropDownStyle = ComboBoxStyle.DropDownList;

        AddFieldRow("Optimization Method", _optimizationMethodCombo);
        _optimizationMethodCombo.Items.AddRange(new object[] { "Sequential Local Search", "Random Restart" });
        _optimizationMethodCombo.SelectedIndex = 0;
        _optimizationMethodCombo.DropDownStyle = ComboBoxStyle.DropDownList;

        AddFieldRow("Max Iterations", _maxIterationsInput);
        _maxIterationsInput.Minimum = 1;
        _maxIterationsInput.Maximum = 1000;
        _maxIterationsInput.DecimalPlaces = 0;
        _maxIterationsInput.Value = 50;

        AddFieldRow("Random Restarts", _randomRestartsInput);
        _randomRestartsInput.Minimum = 1;
        _randomRestartsInput.Maximum = 10000;
        _randomRestartsInput.DecimalPlaces = 0;
        _randomRestartsInput.Value = 100;

        _optimizeCurrentButton.Text = "Optimize Current";
        _optimizeCurrentButton.Size = new Size(360, 30);
        _optimizeCurrentButton.Click += (_, _) => OptimizeCurrentStructure();
        panel.Controls.Add(_optimizeCurrentButton);

        _randomSearchButton.Text = "Random Search";
        _randomSearchButton.Size = new Size(360, 30);
        _randomSearchButton.Click += (_, _) => RunRandomRestartSearch();
        panel.Controls.Add(_randomSearchButton);

        _stopOptimizationButton.Text = "Stop Optimization";
        _stopOptimizationButton.Size = new Size(360, 30);
        _stopOptimizationButton.Enabled = false;
        _stopOptimizationButton.Click += (_, _) => StopOptimization();
        panel.Controls.Add(_stopOptimizationButton);

        var spacer1 = new Panel { Size = new Size(360, 4) };
        panel.Controls.Add(spacer1);

        var buttonRow = new Panel { Size = new Size(360, 32), Margin = new Padding(0, 2, 0, 2) };
        _applyBestButton.Text = "Apply Best";
        _applyBestButton.Location = new Point(0, 0);
        _applyBestButton.Size = new Size(170, 30);
        _applyBestButton.Click += (_, _) => ApplyBestResult();
        buttonRow.Controls.Add(_applyBestButton);

        _exportJsonButton.Text = "Export Results JSON";
        _exportJsonButton.Location = new Point(180, 0);
        _exportJsonButton.Size = new Size(180, 30);
        _exportJsonButton.Click += (_, _) => ExportResultsJson();
        buttonRow.Controls.Add(_exportJsonButton);
        panel.Controls.Add(buttonRow);

        _exportCsvButton.Text = "Export CSV";
        _exportCsvButton.Size = new Size(360, 30);
        _exportCsvButton.Click += (_, _) => ExportCandidatesCsv();
        panel.Controls.Add(_exportCsvButton);

        panel.Controls.Add(_exportTouchstoneButton);

        var spacer2 = new Panel { Size = new Size(360, 4) };
        panel.Controls.Add(spacer2);

        _optimizerStatusLabel.Size = new Size(360, 60);
        _optimizerStatusLabel.AutoSize = false;
        _optimizerStatusLabel.Text = "Ready";
        _optimizerStatusLabel.BorderStyle = BorderStyle.FixedSingle;
        panel.Controls.Add(_optimizerStatusLabel);

        return panel;
    }

    private Control BuildResultsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _resultsGrid.Dock = DockStyle.Fill;
        _resultsGrid.AllowUserToAddRows = false;
        _resultsGrid.AllowUserToDeleteRows = false;
        _resultsGrid.RowHeadersVisible = false;
        _resultsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _resultsGrid.MultiSelect = false;
        _resultsGrid.ReadOnly = true;
        _resultsGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _resultsGrid.CellClick += (_, e) => OnResultsGridCellClick(e);
        _resultsGrid.CellEnter += (_, e) => OnResultsGridCellClick(e);
        _resultsGrid.RowEnter += (_, e) => DisplayCandidateAt(e.RowIndex);
        _resultsGrid.CurrentCellChanged += (_, _) => DisplaySelectedCandidate();
        _resultsGrid.SelectionChanged += (_, _) => DisplaySelectedCandidate();
        _resultsGrid.MouseUp += (_, _) => DisplaySelectedCandidate();
        _resultsGrid.KeyUp += (_, _) => DisplaySelectedCandidate();

        _resultsGrid.Columns.Add("Rank", "Rank");
        _resultsGrid.Columns.Add("Score", "Score");
        _resultsGrid.Columns.Add("WorstS11dB", "Worst S11 (dB)");
        _resultsGrid.Columns.Add("AvgS11dB", "Avg S11 (dB)");
        _resultsGrid.Columns.Add("WorstS21dB", "Worst S21 (dB)");
        _resultsGrid.Columns.Add("RippleDb", "Ripple (dB)");
        _resultsGrid.Columns.Add("PhaseDeg", "Phase (deg)");
        _resultsGrid.Columns.Add("AvgDelayNs", "Avg GD (ns)");
        _resultsGrid.Columns.Add("Structure", "Structure");

        foreach (DataGridViewColumn col in _resultsGrid.Columns)
        {
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        }

        panel.Controls.Add(_resultsGrid, 0, 0);

        var statusPanel = new Label
        {
            Text = "Select a candidate to view its plots",
            Dock = DockStyle.Bottom,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.ControlLight
        };
        panel.Controls.Add(statusPanel, 0, 1);

        return panel;
    }

    private void OnResultsGridCellClick(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.RowIndex < _candidateResults.Count)
        {
            DisplayCandidateAt(e.RowIndex);
        }
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
        if (_presetCombo.SelectedIndex < 0)
        {
            return;
        }

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
        UpdateStructureSummary();
    }

    private void RunSimulation()
    {
        try
        {
            var structure = SimulationEngine.ParseStructure(_structureInput.Text);
            UpdateStructureSummary(structure);
            var sweep = SimulationEngine.Simulate(
                (double)_startMHzInput.Value * 1e6,
                (double)_stopMHzInput.Value * 1e6,
                (int)_pointsInput.Value,
                (double)_z0Input.Value,
                structure);
            _lastSweep = sweep;

            _smithPlot.Model = PlotFactory.BuildSmithModel(sweep);
            _s11DbPlot.Model = PlotFactory.BuildS11DbModel(sweep);
            _s21PolarPlot.Model = PlotFactory.BuildS21PolarModel(sweep);
            _s21DbPlot.Model = PlotFactory.BuildS21DbModel(sweep);
            _s21PhasePlot.Model = PlotFactory.BuildS21PhaseModel(sweep);
            _groupDelayPlot.Model = PlotFactory.BuildGroupDelayModel(sweep);

            RefreshPropagationFrequencyChoices(sweep);
            UpdatePropagationAnalysis();
            UpdateSmallAntennaMode();

            _statusLabel.Text = $"OK | {sweep.Count} points";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void RefreshPropagationFrequencyChoices(IReadOnlyList<SweepPoint> sweep)
    {
        var selectedMHz = (double)_propagationFrequencyMHzInput.Value;
        _propagationSweepFrequencyCombo.Items.Clear();

        if (sweep.Count == 0)
        {
            return;
        }

        var indices = new[] { 0, sweep.Count / 4, sweep.Count / 2, sweep.Count * 3 / 4, sweep.Count - 1 }
            .Distinct()
            .ToList();

        foreach (var index in indices)
        {
            _propagationSweepFrequencyCombo.Items.Add(new SweepFrequencyItem(sweep[index].FrequencyHz));
        }

        var nearestIndex = 0;
        var nearestDelta = double.PositiveInfinity;
        for (var i = 0; i < _propagationSweepFrequencyCombo.Items.Count; i++)
        {
            if (_propagationSweepFrequencyCombo.Items[i] is SweepFrequencyItem item)
            {
                var delta = Math.Abs(item.FrequencyHz / 1e6 - selectedMHz);
                if (delta < nearestDelta)
                {
                    nearestDelta = delta;
                    nearestIndex = i;
                }
            }
        }

        _propagationSweepFrequencyCombo.SelectedIndex = nearestIndex;
    }

    private void SetPropagationFrequencyToSweepCenter()
    {
        if (_lastSweep.Count == 0)
        {
            var centerMHz = ((double)_startMHzInput.Value + (double)_stopMHzInput.Value) / 2.0;
            SetNumericValue(_propagationFrequencyMHzInput, (decimal)centerMHz);
            return;
        }

        var centerIndex = _lastSweep.Count / 2;
        SetNumericValue(_propagationFrequencyMHzInput, (decimal)(_lastSweep[centerIndex].FrequencyHz / 1e6));
    }

    private void UpdatePropagationAnalysis()
    {
        try
        {
            if (_lastSweep.Count == 0)
            {
                _propagationSummaryText.Text = string.Empty;
                _propagationStatusLabel.Text = "Run simulation to populate propagation analysis.";
                return;
            }

            var structure = SimulationEngine.ParseStructure(_structureInput.Text);
            var result = PropagationAnalysis.Analyze(
                structure,
                (double)_propagationFrequencyMHzInput.Value * 1e6,
                (double)_z0Input.Value,
                (double)_propagationSpacingMmInput.Value * 1e-3,
                _lastSweep);

            _nodeVoltagePlot.Model = PlotFactory.BuildNodeVoltageMagnitudeModel(result);
            _nodePhasePlot.Model = PlotFactory.BuildNodePhaseModel(result);
            _localPhaseShiftPlot.Model = PlotFactory.BuildLocalPhaseShiftModel(result);
            _slowWaveFactorPlot.Model = PlotFactory.BuildSlowWaveFactorModel(result);
            _storedEnergyPlot.Model = PlotFactory.BuildStoredEnergyModel(result);
            _propagationSummaryText.Text = PropagationAnalysis.FormatSummary(result);
            _propagationStatusLabel.Text = string.IsNullOrWhiteSpace(result.Warning) ? "Propagation analysis OK" : result.Warning;
        }
        catch (Exception ex)
        {
            _propagationStatusLabel.Text = $"Propagation analysis unavailable: {ex.Message}";
            _propagationSummaryText.Text = ex.Message;
            _nodeVoltagePlot.Model = null;
            _nodePhasePlot.Model = null;
            _localPhaseShiftPlot.Model = null;
            _slowWaveFactorPlot.Model = null;
            _storedEnergyPlot.Model = null;
        }
    }

    private void UpdateSmallAntennaMode()
    {
        try
        {
            var structure = SimulationEngine.ParseStructureAllowEmpty(_structureInput.Text);
            var config = GetSmallAntennaConfig();
            var sweep = SmallAntennaMode.Simulate(
                (double)_antennaStartMHzInput.Value * 1e6,
                (double)_antennaStopMHzInput.Value * 1e6,
                (int)_antennaPointsInput.Value,
                structure,
                config);
            _lastAntennaSweep = sweep;

            var bestMatchFrequencyHz = sweep.OrderBy(p => p.InputReturnLossDb).First().FrequencyHz;
            PropagationResult? propagation = null;
            var propagationWarning = string.Empty;
            if (structure.Count > 0)
            {
                try
                {
                    propagation = PropagationAnalysis.AnalyzeTerminated(
                        structure,
                        bestMatchFrequencyHz,
                        SmallAntennaMode.SourceResistanceOhms,
                        config.AntennaImpedance,
                        (double)_antennaSpacingMmInput.Value * 1e-3,
                        _lastSweep);
                }
                catch (Exception ex)
                {
                    propagationWarning = $"Compression view unavailable: {ex.Message}";
                }
            }
            else
            {
                propagationWarning = "Compression view unavailable: direct antenna has no LC cells.";
            }

            var summary = SmallAntennaMode.Summarize(sweep, propagation, (double)_antennaMaxVoltageInput.Value);

            _antennaReturnLossPlot.Model = PlotFactory.BuildAntennaInputReturnLossModel(sweep);
            _antennaPowerPlot.Model = PlotFactory.BuildAntennaPowerModel(sweep);
            _antennaCurrentPlot.Model = PlotFactory.BuildAntennaCurrentModel(sweep);
            _antennaVoltagePlot.Model = PlotFactory.BuildAntennaVoltageModel(sweep);

            if (propagation is not null)
            {
                _antennaNodeVoltagePlot.Model = PlotFactory.BuildNodeVoltageMagnitudeModel(propagation);
                _antennaNodePhasePlot.Model = PlotFactory.BuildNodePhaseModel(propagation);
                _antennaLocalPhaseShiftPlot.Model = PlotFactory.BuildLocalPhaseShiftModel(propagation);
                _antennaSlowWaveFactorPlot.Model = PlotFactory.BuildSlowWaveFactorModel(propagation);
                _antennaStoredEnergyPlot.Model = PlotFactory.BuildStoredEnergyModel(propagation);
            }
            else
            {
                ClearSmallAntennaCompressionPlots();
            }

            var scoreBreakdown = SmallAntennaMode.CalculateScoreBreakdown(
                sweep,
                structure,
                GetSmallAntennaOptimizerConfig(_lastAntennaObjectiveMode));
            _antennaSummaryText.Text =
                SmallAntennaMode.FormatSummary(summary)
                + Environment.NewLine
                + SmallAntennaMode.FormatScoreBreakdown(scoreBreakdown);
            _antennaStatusLabel.Text = string.IsNullOrWhiteSpace(summary.VoltageWarning)
                ? string.IsNullOrWhiteSpace(propagationWarning) ? "Small antenna analysis OK" : propagationWarning
                : summary.VoltageWarning;
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Small antenna analysis error: {ex.Message}";
            _antennaSummaryText.Text = ex.Message;
            ClearSmallAntennaPlots();
        }
    }

    private void OptimizeSmallAntennaMode(SmallAntennaObjectiveMode objectiveMode)
    {
        if (_optimizationRunning)
        {
            _antennaStatusLabel.Text = "Optimizer is already running.";
            return;
        }

        try
        {
            var structure = SimulationEngine.ParseStructureAllowEmpty(_structureInput.Text);
            var config = GetSmallAntennaOptimizerConfig(objectiveMode);
            _lastAntennaObjectiveMode = objectiveMode;
            _optimizationRunning = true;
            _antennaOptimizeButton.Enabled = false;
            _antennaOptimizeCompressionButton.Enabled = false;
            _antennaApplyBestButton.Enabled = false;
            _antennaExportBestJsonButton.Enabled = false;
            _optimizationCancellation = new CancellationTokenSource();

            _ = Task.Run(() =>
            {
                try
                {
                    UpdateAntennaStatus(objectiveMode == SmallAntennaObjectiveMode.SmoothCompression
                        ? "Starting smooth-compression medium optimization..."
                        : "Starting antenna-match optimization...");
                    var candidate = SmallAntennaMode.Optimize(
                        structure,
                        config,
                        UpdateAntennaStatus,
                        _optimizationCancellation.Token);
                    _bestAntennaCandidate = candidate;

                    Invoke(() =>
                    {
                        _lastAntennaSweep = candidate.Sweep;
                        _antennaSummaryText.Text =
                            SmallAntennaMode.FormatSummary(candidate.Summary)
                            + Environment.NewLine
                            + SmallAntennaMode.FormatScoreBreakdown(candidate.ScoreBreakdown);
                        _antennaReturnLossPlot.Model = PlotFactory.BuildAntennaInputReturnLossModel(candidate.Sweep);
                        _antennaPowerPlot.Model = PlotFactory.BuildAntennaPowerModel(candidate.Sweep);
                        _antennaCurrentPlot.Model = PlotFactory.BuildAntennaCurrentModel(candidate.Sweep);
                        _antennaVoltagePlot.Model = PlotFactory.BuildAntennaVoltageModel(candidate.Sweep);
                        _antennaApplyBestButton.Enabled = true;
                        _antennaExportBestJsonButton.Enabled = true;
                        _antennaStatusLabel.Text = $"Antenna optimization complete. Score: {candidate.Score:F4} | {FormatStructureInline(candidate.Structure)}";
                    });
                }
                catch (OperationCanceledException)
                {
                    UpdateAntennaStatus("Antenna optimization cancelled.");
                }
                catch (Exception ex)
                {
                    UpdateAntennaStatus($"Antenna optimization error: {ex.Message}");
                }
                finally
                {
                    _optimizationRunning = false;
                    Invoke(() =>
                    {
                        _antennaOptimizeButton.Enabled = true;
                        _antennaOptimizeCompressionButton.Enabled = true;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Antenna optimization error: {ex.Message}";
            _optimizationRunning = false;
            _antennaOptimizeButton.Enabled = true;
            _antennaOptimizeCompressionButton.Enabled = true;
        }
    }

    private void ApplyBestSmallAntennaCandidate()
    {
        if (_bestAntennaCandidate is null)
        {
            _antennaStatusLabel.Text = "No antenna candidate available.";
            return;
        }

        _structureInput.Text = JsonSerializer.Serialize(_bestAntennaCandidate.Structure, new JsonSerializerOptions { WriteIndented = true });
        UpdateStructureSummary(_bestAntennaCandidate.Structure);
        _presetCombo.SelectedIndex = -1;
        RunSimulation();
        UpdateSmallAntennaMode();
        _antennaStatusLabel.Text = $"Applied antenna-mode best. Score: {_bestAntennaCandidate.Score:F4}";
    }

    private void ExportBestSmallAntennaCandidateJson()
    {
        if (_bestAntennaCandidate is null)
        {
            _antennaStatusLabel.Text = "No antenna candidate available.";
            return;
        }

        try
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                AddExtension = true,
                FileName = "small-antenna-best-structure.json"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var json = JsonSerializer.Serialize(_bestAntennaCandidate.Structure, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
            _antennaStatusLabel.Text = $"Exported antenna best: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Antenna export error: {ex.Message}";
        }
    }

    private SmallAntennaConfig GetSmallAntennaConfig()
    {
        return new SmallAntennaConfig
        {
            RadiationResistanceOhms = (double)_antennaRradInput.Value,
            LossResistanceOhms = (double)_antennaRlossInput.Value,
            ReactanceOhms = (double)_antennaXantInput.Value
        };
    }

    private SmallAntennaOptimizerConfig GetSmallAntennaOptimizerConfig(
        SmallAntennaObjectiveMode objectiveMode = SmallAntennaObjectiveMode.AntennaMatch)
    {
        return new SmallAntennaOptimizerConfig
        {
            StartHz = (double)_antennaStartMHzInput.Value * 1e6,
            StopHz = (double)_antennaStopMHzInput.Value * 1e6,
            Points = (int)_antennaPointsInput.Value,
            TargetInputReturnLossDb = (double)_antennaTargetReturnLossInput.Value,
            MaxAntennaVoltage = (double)_antennaMaxVoltageInput.Value,
            MaxIterations = (int)_antennaOptimizerIterationsInput.Value,
            UseContinuousValues = _antennaUseContinuousInput.Checked,
            ContinuousLMinHenries = (double)_antennaLMinNhInput.Value * 1e-9,
            ContinuousLMaxHenries = (double)_antennaLMaxNhInput.Value * 1e-9,
            ContinuousCMinFarads = (double)_antennaCMinPfInput.Value * 1e-12,
            ContinuousCMaxFarads = (double)_antennaCMaxPfInput.Value * 1e-12,
            UseSmoothIncreasingGradient = _antennaSmoothGradientInput.Checked,
            LauncherCellCount = (int)_antennaLauncherCellsInput.Value,
            MaxAverageInputReflectionPercent = (double)_antennaMaxAverageReflectionInput.Value,
            CellSpacingMeters = (double)_antennaSpacingMmInput.Value * 1e-3,
            ObjectiveMode = objectiveMode,
            AntennaConfig = GetSmallAntennaConfig()
        };
    }

    private void ClearSmallAntennaPlots()
    {
        _antennaReturnLossPlot.Model = null;
        _antennaPowerPlot.Model = null;
        _antennaCurrentPlot.Model = null;
        _antennaVoltagePlot.Model = null;
        ClearSmallAntennaCompressionPlots();
    }

    private void ClearSmallAntennaCompressionPlots()
    {
        _antennaNodeVoltagePlot.Model = null;
        _antennaNodePhasePlot.Model = null;
        _antennaLocalPhaseShiftPlot.Model = null;
        _antennaSlowWaveFactorPlot.Model = null;
        _antennaStoredEnergyPlot.Model = null;
    }

    private void UpdateAntennaStatus(string message)
    {
        Invoke(() => _antennaStatusLabel.Text = message);
    }

    private static string FormatStructureInline(IReadOnlyList<RfElement> structure)
    {
        return string.Join(" - ", structure.Select(e => $"{GetElementDisplayName(e.Type)} {FormatElementValue(e)}"));
    }

    private void SaveConfiguration()
    {
        try
        {
            var structure = SimulationEngine.ParseStructure(_structureInput.Text);
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
            SimulationEngine.ValidateStructure(config.Structure);

            SetNumericValue(_startMHzInput, (decimal)config.StartMHz);
            SetNumericValue(_stopMHzInput, (decimal)config.StopMHz);
            SetNumericValue(_pointsInput, config.Points);
            SetNumericValue(_z0Input, (decimal)config.Z0);

            _presetCombo.SelectedIndex = -1;
            _structureInput.Text = JsonSerializer.Serialize(config.Structure, new JsonSerializerOptions { WriteIndented = true });
            UpdateStructureSummary(config.Structure);
            RunSimulation();
            _statusLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Load error: {ex.Message}";
        }
    }

    private void ExportTouchstone()
    {
        try
        {
            var structure = SimulationEngine.ParseStructure(_structureInput.Text);
            using var dialog = new SaveFileDialog
            {
                Filter = "Touchstone 2-port files (*.s2p)|*.s2p",
                DefaultExt = "s2p",
                FileName = "rf-lc-simulation.s2p"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            const double touchstoneZ0 = 50.0;
            var sweep = SimulationEngine.SimulateSParameters(
                (double)_startMHzInput.Value * 1e6,
                (double)_stopMHzInput.Value * 1e6,
                (int)_pointsInput.Value,
                touchstoneZ0,
                structure);

            TouchstoneExporter.WriteS2P(dialog.FileName, sweep);
            _statusLabel.Text = $"Exported S2P: {Path.GetFileName(dialog.FileName)} | {sweep.Count} points | R 50";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"S2P export error: {ex.Message}";
        }
    }

    // Optimizer methods
    private OptimizerEngine.OptimizerConfig GetOptimizerConfig()
    {
        var lengthIndex = _structureLengthCombo.SelectedIndex;
        var numInductors = lengthIndex switch
        {
            0 => 3,
            1 => 5,
            2 => 7,
            3 => 9,
            _ => 3
        };

        return new OptimizerEngine.OptimizerConfig
        {
            StartHz = (double)_optStartMHzInput.Value * 1e6,
            StopHz = (double)_optStopMHzInput.Value * 1e6,
            Points = (int)_optPointsInput.Value,
            Z0 = 50,
            TargetS11Db = (double)_targetS11DbInput.Value,
            MinAcceptableS21Db = (double)_minS21DbInput.Value,
            StructureLength = numInductors,
            MaxIterations = (int)_maxIterationsInput.Value,
            RandomRestarts = (int)_randomRestartsInput.Value,
            TargetPhaseRotationDeg = (double)_targetPhaseRotationDegInput.Value,
            EnablePhaseObjective = _enablePhaseObjectiveInput.Checked,
            EnableGroupDelayObjective = _enableGroupDelayObjectiveInput.Checked,
            PhaseObjectiveWeight = (double)_phaseObjectiveWeightInput.Value,
            GroupDelayObjectiveWeight = (double)_groupDelayObjectiveWeightInput.Value
        };
    }

    private void OptimizeCurrentStructure()
    {
        if (_optimizationRunning)
            return;

        try
        {
            var structure = SimulationEngine.ParseStructure(_structureInput.Text);
            if (structure.Count == 0)
            {
                _optimizerStatusLabel.Text = "Error: Empty structure";
                return;
            }

            var config = GetOptimizerConfig();
            _optimizationRunning = true;
            _optimizeCurrentButton.Enabled = false;
            _randomSearchButton.Enabled = false;
            _stopOptimizationButton.Enabled = true;
            _optimizationCancellation = new CancellationTokenSource();

            var task = Task.Run(() =>
            {
                try
                {
                    UpdateStatus("Starting sequential search...");
                    var result = OptimizerEngine.SequentialLocalSearch(
                        structure,
                        s => SimulationEngine.Simulate(
                            config.StartHz,
                            config.StopHz,
                            config.Points,
                            config.Z0,
                            s),
                        config,
                        UpdateStatus,
                        _optimizationCancellation.Token);

                    _candidateResults = new List<OptimizerEngine.CandidateResult> { result };
                    UpdateResultsGrid();
                    DisplayCandidate(result);
                    UpdateStatus($"Optimization complete. Score: {result.Score:F6}");
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("Optimization cancelled.");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.Message}");
                }
                finally
                {
                    _optimizationRunning = false;
                    Invoke(() =>
                    {
                        _optimizeCurrentButton.Enabled = true;
                        _randomSearchButton.Enabled = true;
                        _stopOptimizationButton.Enabled = false;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _optimizerStatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void RunRandomRestartSearch()
    {
        if (_optimizationRunning)
            return;

        try
        {
            var config = GetOptimizerConfig();
            _optimizationRunning = true;
            _optimizeCurrentButton.Enabled = false;
            _randomSearchButton.Enabled = false;
            _stopOptimizationButton.Enabled = true;
            _optimizationCancellation = new CancellationTokenSource();

            var task = Task.Run(() =>
            {
                try
                {
                    UpdateStatus($"Starting random restart search with {config.RandomRestarts} restarts...");
                    var results = OptimizerEngine.RandomRestartLocalSearch(
                        s => SimulationEngine.Simulate(
                            config.StartHz,
                            config.StopHz,
                            config.Points,
                            config.Z0,
                            s),
                        config,
                        UpdateStatus,
                        _optimizationCancellation.Token);

                    _candidateResults = results;
                    UpdateResultsGrid();
                    if (results.Count > 0)
                    {
                        DisplayCandidate(results[0]);
                        UpdateStatus($"Search complete. Best score: {results[0].Score:F6}");
                    }
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("Search cancelled.");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.Message}");
                }
                finally
                {
                    _optimizationRunning = false;
                    Invoke(() =>
                    {
                        _optimizeCurrentButton.Enabled = true;
                        _randomSearchButton.Enabled = true;
                        _stopOptimizationButton.Enabled = false;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _optimizerStatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void StopOptimization()
    {
        _optimizationCancellation?.Cancel();
        _optimizerStatusLabel.Text = "Optimization stopped by user.";
    }

    private void UpdateStatus(string message)
    {
        Invoke(() => { _optimizerStatusLabel.Text = message; });
    }

    private void UpdateResultsGrid()
    {
        Invoke(() =>
        {
            _resultsGrid.Rows.Clear();
            _displayedCandidateIndex = -1;
            for (int i = 0; i < _candidateResults.Count; i++)
            {
                var candidate = _candidateResults[i];
                var structure = string.Join(" - ", candidate.Structure.Select(e =>
                    $"{GetElementDisplayName(e.Type)} {FormatElementValue(e)}"));

                _resultsGrid.Rows.Add(
                    i + 1,
                    candidate.Score.ToString("F6"),
                    candidate.WorstS11Db.ToString("F2"),
                    candidate.AverageS11Db.ToString("F2"),
                    candidate.WorstS21Db.ToString("F2"),
                    candidate.RippleDb.ToString("F2"),
                    candidate.TotalPhaseRotationDeg.ToString("F1"),
                    candidate.AverageGroupDelayNs.ToString("F2"),
                    structure);
            }

            if (_resultsGrid.Rows.Count > 0)
                _resultsGrid.Rows[0].Selected = true;
        });
    }

    private void DisplaySelectedCandidate()
    {
        var rowIndex = _resultsGrid.CurrentRow?.Index ?? -1;
        if (rowIndex < 0 && _resultsGrid.CurrentCell != null)
            rowIndex = _resultsGrid.CurrentCell.RowIndex;

        if (rowIndex < 0 && _resultsGrid.SelectedCells.Count > 0)
            rowIndex = _resultsGrid.SelectedCells[0].RowIndex;

        if (rowIndex < 0 && _resultsGrid.SelectedRows.Count > 0)
            rowIndex = _resultsGrid.SelectedRows[0].Index;

        if (rowIndex < 0)
            return;

        DisplayCandidateAt(rowIndex);
    }

    private void DisplayCandidateAt(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _candidateResults.Count)
            return;

        _displayedCandidateIndex = rowIndex;
        DisplayCandidate(_candidateResults[rowIndex]);
    }

    private void DisplayCandidate(OptimizerEngine.CandidateResult candidate)
    {
        if (InvokeRequired)
        {
            Invoke(() => DisplayCandidate(candidate));
            return;
        }

        _structureInput.Text = JsonSerializer.Serialize(candidate.Structure, new JsonSerializerOptions { WriteIndented = true });
        UpdateStructureSummary(candidate.Structure);
        _presetCombo.SelectedIndex = -1;
        RunSimulation();
        _statusLabel.Text =
            $"Candidate | S11: {candidate.WorstS11Db:F2} dB | S21: {candidate.WorstS21Db:F2} dB | Phase: {candidate.TotalPhaseRotationDeg:F1} deg | GD: {candidate.AverageGroupDelayNs:F2} ns | Score: {candidate.Score:F6}";
    }

    private void ApplyBestResult()
    {
        if (_candidateResults.Count == 0)
        {
            _optimizerStatusLabel.Text = "No candidates available.";
            return;
        }

        var best = _candidateResults[0];
        _structureInput.Text = JsonSerializer.Serialize(best.Structure, new JsonSerializerOptions { WriteIndented = true });
        UpdateStructureSummary(best.Structure);
        _presetCombo.SelectedIndex = -1;
        RunSimulation();
        _optimizerStatusLabel.Text = "Best result applied to simulator.";
    }

    private void ExportResultsJson()
    {
        if (_candidateResults.Count == 0)
        {
            _optimizerStatusLabel.Text = "No candidates to export.";
            return;
        }

        try
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = "json",
                FileName = "optimizer-results.json"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var export = _candidateResults.Select((candidate, index) => new
            {
                Rank = index + 1,
                candidate.Score,
                candidate.WorstS11Db,
                candidate.AverageS11Db,
                candidate.WorstS21Db,
                candidate.RippleDb,
                candidate.TotalPhaseRotationDeg,
                candidate.AverageGroupDelayNs,
                candidate.MaxGroupDelayNs,
                candidate.Structure
            }).ToList();

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
            _optimizerStatusLabel.Text = $"Exported: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _optimizerStatusLabel.Text = $"Export error: {ex.Message}";
        }
    }

    private void ExportCandidatesCsv()
    {
        if (_candidateResults.Count == 0)
        {
            _optimizerStatusLabel.Text = "No candidates to export.";
            return;
        }

        try
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = "csv",
                FileName = "optimizer-candidates.csv"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            using var writer = new StreamWriter(dialog.FileName);
            writer.WriteLine("Rank,Score,WorstS11dB,AverageS11dB,WorstS21dB,RippleDb,TotalPhaseRotationDeg,AverageGroupDelayNs,MaxGroupDelayNs,StructureJson");

            for (int i = 0; i < _candidateResults.Count; i++)
            {
                var candidate = _candidateResults[i];
                var structureJson = JsonSerializer.Serialize(candidate.Structure);
                writer.WriteLine(
                    $"{i + 1}," +
                    $"{candidate.Score}," +
                    $"{candidate.WorstS11Db}," +
                    $"{candidate.AverageS11Db}," +
                    $"{candidate.WorstS21Db}," +
                    $"{candidate.RippleDb}," +
                    $"{candidate.TotalPhaseRotationDeg}," +
                    $"{candidate.AverageGroupDelayNs}," +
                    $"{candidate.MaxGroupDelayNs}," +
                    $"\"{structureJson}\"");
            }

            writer.Close();
            _optimizerStatusLabel.Text = $"Exported: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _optimizerStatusLabel.Text = $"Export error: {ex.Message}";
        }
    }

    private static string GetElementDisplayName(string type)
    {
        return type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase) ? "L" :
            type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase) ? "C" :
            type;
    }

    private void UpdateStructureSummary()
    {
        try
        {
            UpdateStructureSummary(SimulationEngine.ParseStructure(_structureInput.Text));
        }
        catch
        {
            _structureSummaryInput.Text = "Invalid structure";
        }
    }

    private void UpdateStructureSummary(IReadOnlyList<RfElement> structure)
    {
        _structureSummaryInput.Text = string.Join(" - ", structure.Select(FormatElementValue));
    }

    private static string FormatElementValue(RfElement element)
    {
        if (element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase))
        {
            var nH = element.Value * 1e9;
            if (nH < 1000)
            {
                return $"{nH:G4} nH";
            }

            return $"{element.Value * 1e6:G4} uH";
        }

        if (element.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase))
        {
            var pF = element.Value * 1e12;
            if (pF < 1000)
            {
                return $"{pF:G4} pF";
            }

            return $"{element.Value * 1e9:G4} nF";
        }

        return $"{element.Value:G4}";
    }

    private static void SetNumericValue(NumericUpDown input, decimal value)
    {
        input.Value = Math.Min(input.Maximum, Math.Max(input.Minimum, value));
    }

    private sealed class SweepFrequencyItem
    {
        public SweepFrequencyItem(double frequencyHz)
        {
            FrequencyHz = frequencyHz;
        }

        public double FrequencyHz { get; }

        public override string ToString() => $"{FrequencyHz / 1e6:F3} MHz";
    }
}
