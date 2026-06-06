using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace RfSimulator;

public partial class Form1
{
    private const double SpeedOfLightMetersPerSecond = 299_792_458.0;

    private readonly NumericUpDown _singleAntennaFrequencyMHzInput = new();
    private readonly NumericUpDown _singleAntennaLengthMInput = new();
    private readonly ComboBox _singleAntennaTypeInput = new();
    private readonly ComboBox _singleAntennaGroundInput = new();
    private readonly NumericUpDown _singleAntennaRlossInput = new();
    private readonly NumericUpDown _singleAntennaCantPfInput = new();
    private readonly CheckBox _singleAntennaUseCurrentStructureInput = new();
    private readonly CheckBox _singleAntennaUseCustomInput = new();
    private readonly NumericUpDown _singleAntennaCustomRradInput = new();
    private readonly NumericUpDown _singleAntennaCustomXantInput = new();
    private readonly NumericUpDown _singleAntennaCustomRlossInput = new();
    private readonly ComboBox _singleAntennaOptimizationScopeInput = new();
    private readonly Button _singleAntennaAnalyzeButton = new();
    private readonly Button _singleAntennaOptimizeTerminalButton = new();
    private readonly Button _singleAntennaApplyTerminalButton = new();
    private readonly Button _singleAntennaExportJsonButton = new();
    private readonly Button _singleAntennaExploreButton = new();
    private readonly NumericUpDown _singleAntennaExploreStartMHzInput = new();
    private readonly NumericUpDown _singleAntennaExploreStopMHzInput = new();
    private readonly NumericUpDown _singleAntennaExploreStepMHzInput = new();
    private readonly NumericUpDown _singleAntennaExploreRradStartInput = new();
    private readonly NumericUpDown _singleAntennaExploreRradStopInput = new();
    private readonly NumericUpDown _singleAntennaExploreRradStepInput = new();
    private readonly NumericUpDown _singleAntennaExploreXantStartInput = new();
    private readonly NumericUpDown _singleAntennaExploreXantStopInput = new();
    private readonly NumericUpDown _singleAntennaExploreXantStepInput = new();
    private readonly NumericUpDown _singleAntennaExploreRlossInput = new();
    private readonly NumericUpDown _singleAntennaExploreAcceptedMinInput = new();
    private readonly NumericUpDown _singleAntennaExploreBestCountInput = new();
    private readonly CheckBox _singleAntennaExploreOptimizeTerminalInput = new();
    private readonly PlotView _singleAntennaPowerFlowPlot = new();
    private readonly ToolTip _singleAntennaToolTip = new();

    private SingleFrequencyAntennaResult? _lastSingleAntennaResult;
    private List<RfElement>? _terminalCouplingCandidate;
    private List<SingleAntennaExplorationCase> _lastExplorationCases = new();

    private Control BuildSingleFrequencySmallAntennaPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildSingleAntennaControlsPanel(), 0, 0);

        _antennaSummaryText.ReadOnly = true;
        _antennaSummaryText.Multiline = true;
        _antennaSummaryText.ScrollBars = ScrollBars.Vertical;
        _antennaSummaryText.Dock = DockStyle.Fill;
        _antennaSummaryText.Font = new Font("Consolas", 9);
        _antennaSummaryText.BackColor = SystemColors.Window;
        root.Controls.Add(_antennaSummaryText, 0, 1);

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
        ConfigurePlotView(_singleAntennaPowerFlowPlot);
        plots.Controls.Add(_antennaNodeVoltagePlot, 0, 0);
        plots.Controls.Add(_antennaNodePhasePlot, 1, 0);
        plots.Controls.Add(_antennaLocalPhaseShiftPlot, 0, 1);
        plots.Controls.Add(_antennaSlowWaveFactorPlot, 1, 1);
        plots.Controls.Add(_antennaStoredEnergyPlot, 0, 2);
        plots.Controls.Add(_singleAntennaPowerFlowPlot, 1, 2);
        root.Controls.Add(plots, 0, 2);

        return root;
    }

    private Control BuildSingleAntennaControlsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 6)
        };

        ConfigureNumeric(_singleAntennaFrequencyMHzInput, 0.001m, 10000, 5, 4, 0.1m);
        ConfigureNumeric(_singleAntennaLengthMInput, 0.001m, 10000, 1, 3, 0.1m);
        ConfigureNumeric(_singleAntennaRlossInput, 0, 10000, 1, 3, 0.1m);
        ConfigureNumeric(_singleAntennaCantPfInput, 0.001m, 1000000, 10, 3, 1);
        ConfigureNumeric(_singleAntennaCustomRradInput, 0, 10000, 0.11m, 4, 0.1m);
        ConfigureNumeric(_singleAntennaCustomXantInput, -1000000, 1000000, -3183, 2, 10);
        ConfigureNumeric(_singleAntennaCustomRlossInput, 0, 10000, 1, 3, 0.1m);
        ConfigureNumeric(_antennaSpacingMmInput, 0.001m, 1000, 5, 3, 0.5m);
        ConfigureNumeric(_singleAntennaExploreStartMHzInput, 0.001m, 10000, 1, 3, 0.25m);
        ConfigureNumeric(_singleAntennaExploreStopMHzInput, 0.001m, 10000, 15, 3, 0.25m);
        ConfigureNumeric(_singleAntennaExploreStepMHzInput, 0.001m, 1000, 0.25m, 3, 0.25m);
        ConfigureNumeric(_singleAntennaExploreRradStartInput, 0, 10000, 5, 3, 5);
        ConfigureNumeric(_singleAntennaExploreRradStopInput, 0, 10000, 50, 3, 5);
        ConfigureNumeric(_singleAntennaExploreRradStepInput, 0.001m, 10000, 5, 3, 1);
        ConfigureNumeric(_singleAntennaExploreXantStartInput, -1000000, 1000000, 0, 2, 10);
        ConfigureNumeric(_singleAntennaExploreXantStopInput, -1000000, 1000000, -100, 2, 10);
        ConfigureNumeric(_singleAntennaExploreXantStepInput, 0.001m, 1000000, 10, 2, 10);
        ConfigureNumeric(_singleAntennaExploreRlossInput, 0, 10000, 1, 3, 0.1m);
        ConfigureNumeric(_singleAntennaExploreAcceptedMinInput, 0, 1, 0.2m, 3, 0.05m);
        ConfigureNumeric(_singleAntennaExploreBestCountInput, 1, 100, 20, 0, 1);

        _singleAntennaTypeInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _singleAntennaTypeInput.Items.AddRange(new object[] { "Short monopole", "Short dipole" });
        _singleAntennaTypeInput.SelectedIndex = 0;

        _singleAntennaGroundInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _singleAntennaGroundInput.Items.AddRange(new object[] { "Ideal ground", "Lossy ground / custom loss" });
        _singleAntennaGroundInput.SelectedIndex = 0;

        _singleAntennaOptimizationScopeInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _singleAntennaOptimizationScopeInput.Items.AddRange(new object[]
        {
            "None", "Last 1 element only", "Last 2 elements only", "Last 3 elements only", "Whole structure"
        });
        _singleAntennaOptimizationScopeInput.SelectedIndex = 2;

        _singleAntennaUseCurrentStructureInput.Text = "Use current LC structure";
        _singleAntennaUseCurrentStructureInput.Checked = true;
        _singleAntennaUseCurrentStructureInput.Enabled = false;
        _singleAntennaUseCurrentStructureInput.AutoSize = true;
        _singleAntennaUseCustomInput.Text = "Use custom antenna impedance";
        _singleAntennaUseCustomInput.AutoSize = true;
        _singleAntennaExploreOptimizeTerminalInput.Text = "Optimize terminal section";
        _singleAntennaExploreOptimizeTerminalInput.AutoSize = true;

        AddCompactField(panel, "Freq MHz", _singleAntennaFrequencyMHzInput, 78);
        AddCompactField(panel, "Length m", _singleAntennaLengthMInput, 78);
        AddComboField(panel, "Antenna", _singleAntennaTypeInput, 130);
        AddComboField(panel, "Ground", _singleAntennaGroundInput, 155);
        AddCompactField(panel, "Rloss ohm", _singleAntennaRlossInput, 78);
        AddCompactField(panel, "Cant pF", _singleAntennaCantPfInput, 78);
        panel.Controls.Add(_singleAntennaUseCurrentStructureInput);
        panel.Controls.Add(_singleAntennaUseCustomInput);
        AddCompactField(panel, "Rrad Ω", _singleAntennaCustomRradInput, 78);
        AddCompactField(panel, "Xant Ω", _singleAntennaCustomXantInput, 90);
        AddCompactField(panel, "Rloss Ω", _singleAntennaCustomRlossInput, 78);
        AddCompactField(panel, "Spacing mm", _antennaSpacingMmInput, 78);
        AddComboField(panel, "Opt scope", _singleAntennaOptimizationScopeInput, 145);

        _singleAntennaToolTip.SetToolTip(
            _singleAntennaCustomRradInput,
            "Radiation resistance of the antenna. Useful radiated-power resistance.");
        _singleAntennaToolTip.SetToolTip(
            _singleAntennaCustomXantInput,
            "Antenna reactive impedance in ohms. Electrically-short antennas are usually capacitive, so Xant is negative.");
        _singleAntennaToolTip.SetToolTip(
            _singleAntennaCustomRlossInput,
            "Non-radiating loss resistance in ohms. Represents conductor, ground, and dissipative losses.");
        _singleAntennaToolTip.SetToolTip(
            _singleAntennaUseCustomInput,
            "When enabled, Zant = Rrad + Rloss + jXant. Example: Rrad 50, Xant 0, Rloss 1 gives Zant = 51 + j0 ohm.");
        _singleAntennaToolTip.SetToolTip(
            _singleAntennaExploreOptimizeTerminalInput,
            "Optional exploration mode: also try small +/-10% changes in the last 2-3 LC elements only. The main medium remains fixed.");

        _singleAntennaAnalyzeButton.Text = "Analyze Current Structure With Antenna";
        _singleAntennaAnalyzeButton.AutoSize = true;
        _singleAntennaAnalyzeButton.Click += (_, _) => UpdateSingleFrequencySmallAntennaMode();
        _singleAntennaOptimizeTerminalButton.Text = "Optimize Terminal Coupling Only";
        _singleAntennaOptimizeTerminalButton.AutoSize = true;
        _singleAntennaOptimizeTerminalButton.Click += (_, _) => OptimizeSingleAntennaTerminalCoupling();
        _singleAntennaApplyTerminalButton.Text = "Apply Terminal Coupling";
        _singleAntennaApplyTerminalButton.AutoSize = true;
        _singleAntennaApplyTerminalButton.Enabled = false;
        _singleAntennaApplyTerminalButton.Click += (_, _) => ApplySingleAntennaTerminalCoupling();
        _singleAntennaExportJsonButton.Text = "Export Antenna Test JSON";
        _singleAntennaExportJsonButton.AutoSize = true;
        _singleAntennaExportJsonButton.Click += (_, _) => ExportSingleAntennaJson();
        _singleAntennaExploreButton.Text = "Explore Regimes";
        _singleAntennaExploreButton.AutoSize = true;
        _singleAntennaExploreButton.Click += (_, _) => ExploreSingleAntennaPropagationRegimes();

        EventHandler updateAntennaLoad = (_, _) => UpdateSingleFrequencySmallAntennaMode();
        _singleAntennaFrequencyMHzInput.ValueChanged += updateAntennaLoad;
        _singleAntennaLengthMInput.ValueChanged += updateAntennaLoad;
        _singleAntennaRlossInput.ValueChanged += updateAntennaLoad;
        _singleAntennaCantPfInput.ValueChanged += updateAntennaLoad;
        _singleAntennaCustomRradInput.ValueChanged += updateAntennaLoad;
        _singleAntennaCustomXantInput.ValueChanged += updateAntennaLoad;
        _singleAntennaCustomRlossInput.ValueChanged += updateAntennaLoad;
        _singleAntennaUseCustomInput.CheckedChanged += updateAntennaLoad;
        _singleAntennaTypeInput.SelectedIndexChanged += updateAntennaLoad;
        _singleAntennaGroundInput.SelectedIndexChanged += updateAntennaLoad;
        _antennaSpacingMmInput.ValueChanged += updateAntennaLoad;
        panel.Controls.Add(_singleAntennaAnalyzeButton);
        panel.Controls.Add(_singleAntennaOptimizeTerminalButton);
        panel.Controls.Add(_singleAntennaApplyTerminalButton);
        panel.Controls.Add(_singleAntennaExportJsonButton);
        panel.Controls.Add(_singleAntennaExploreButton);
        AddCompactField(panel, "F start", _singleAntennaExploreStartMHzInput, 64);
        AddCompactField(panel, "F stop", _singleAntennaExploreStopMHzInput, 64);
        AddCompactField(panel, "F step", _singleAntennaExploreStepMHzInput, 64);
        AddCompactField(panel, "R start", _singleAntennaExploreRradStartInput, 64);
        AddCompactField(panel, "R stop", _singleAntennaExploreRradStopInput, 64);
        AddCompactField(panel, "R step", _singleAntennaExploreRradStepInput, 64);
        AddCompactField(panel, "X start", _singleAntennaExploreXantStartInput, 70);
        AddCompactField(panel, "X stop", _singleAntennaExploreXantStopInput, 70);
        AddCompactField(panel, "X step", _singleAntennaExploreXantStepInput, 64);
        AddCompactField(panel, "Exp Rloss", _singleAntennaExploreRlossInput, 64);
        AddCompactField(panel, "Min acc", _singleAntennaExploreAcceptedMinInput, 64);
        AddCompactField(panel, "Best n", _singleAntennaExploreBestCountInput, 54);
        panel.Controls.Add(_singleAntennaExploreOptimizeTerminalInput);

        _antennaStatusLabel.AutoSize = false;
        _antennaStatusLabel.Width = 520;
        _antennaStatusLabel.Height = 26;
        _antennaStatusLabel.Text = "Ready";
        _antennaStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _antennaStatusLabel.BorderStyle = BorderStyle.FixedSingle;
        _antennaStatusLabel.Padding = new Padding(4, 0, 4, 0);
        panel.Controls.Add(_antennaStatusLabel);

        return panel;
    }

    private static void AddComboField(FlowLayoutPanel panel, string label, ComboBox control, int width)
    {
        var row = new Panel { Width = width + 72, Height = 28, Margin = new Padding(0, 0, 4, 2) };
        row.Controls.Add(new Label
        {
            Text = label,
            Location = new Point(0, 3),
            Size = new Size(70, 22),
            TextAlign = ContentAlignment.MiddleLeft
        });
        control.Location = new Point(72, 1);
        control.Size = new Size(width, 24);
        row.Controls.Add(control);
        panel.Controls.Add(row);
    }

    private void UpdateSingleFrequencySmallAntennaMode()
    {
        try
        {
            var result = AnalyzeSingleFrequencyAntenna(GetSingleAntennaStructure());
            _lastSingleAntennaResult = result;
            RenderSingleAntennaResult(result);
            _antennaStatusLabel.Text = result.Propagation is null
                ? result.PropagationWarning
                : $"Single-frequency antenna loading analysis OK. Propagation cells: {result.Propagation.Cells.Count}.";
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Single-frequency antenna error: {ex.Message}";
            _antennaSummaryText.Text = ex.Message;
            var unavailable = BuildSingleAntennaUnavailablePlot(ex.Message);
            _antennaNodeVoltagePlot.Model = unavailable;
            _antennaNodePhasePlot.Model = BuildSingleAntennaUnavailablePlot(ex.Message);
            _antennaLocalPhaseShiftPlot.Model = BuildSingleAntennaUnavailablePlot(ex.Message);
            _antennaSlowWaveFactorPlot.Model = BuildSingleAntennaUnavailablePlot(ex.Message);
            _antennaStoredEnergyPlot.Model = BuildSingleAntennaUnavailablePlot(ex.Message);
            _singleAntennaPowerFlowPlot.Model = null;
        }
    }

    private SingleFrequencyAntennaResult AnalyzeSingleFrequencyAntenna(IReadOnlyList<RfElement> structure)
    {
        var frequencyHz = (double)_singleAntennaFrequencyMHzInput.Value * 1e6;
        var lengthMeters = (double)_singleAntennaLengthMInput.Value;
        var lambda0 = SpeedOfLightMetersPerSecond / frequencyHz;
        var antenna = BuildShortAntennaModel(frequencyHz, lengthMeters, lambda0);
        return AnalyzeSingleFrequencyAntenna(
            structure,
            frequencyHz,
            lengthMeters,
            _singleAntennaTypeInput.Text,
            antenna,
            GetSingleAntennaSpacingMeters());
    }

    private SingleFrequencyAntennaResult AnalyzeSingleFrequencyAntenna(
        IReadOnlyList<RfElement> structure,
        double frequencyHz,
        double lengthMeters,
        string antennaType,
        ShortAntennaModel antenna,
        double spacingMeters)
    {
        var lambda0 = SpeedOfLightMetersPerSecond / frequencyHz;
        var lengthRatio = lengthMeters / lambda0;
        var thetaDeg = 360.0 * lengthRatio;
        var config = new SmallAntennaConfig
        {
            RadiationResistanceOhms = antenna.RadiationResistanceOhms,
            LossResistanceOhms = antenna.LossResistanceOhms,
            ReactanceOhms = antenna.ReactanceOhms
        };

        if (structure.Count == 0)
        {
            throw new InvalidOperationException("Small Antenna Mode requires the currently loaded LC structure.");
        }

        var propagation = PropagationAnalysis.AnalyzeTerminated(
            structure,
            frequencyHz,
            SmallAntennaMode.SourceResistanceOhms,
            config.AntennaImpedance,
            spacingMeters,
            Array.Empty<SweepPoint>());
        var propagationWarning = propagation.Warning;

        var z0 = SmallAntennaMode.SourceResistanceOhms;
        var availablePower = SmallAntennaMode.AvailableSourcePowerWatts;
        var sourceVoltageRms = Math.Sqrt(4.0 * z0 * availablePower);
        var antennaImpedance = config.AntennaImpedance;
        Complex inputImpedance;
        Complex reflectionCoefficient;
        Complex antennaVoltage;
        Complex antennaCurrent;
        var propagationScale = Complex.One;

        inputImpedance = propagation.Diagnostics.InputImpedanceOhms;
        reflectionCoefficient = (inputImpedance - z0) / (inputImpedance + z0);
        var inputVoltage = sourceVoltageRms * inputImpedance / (z0 + inputImpedance);
        propagationScale = propagation.Nodes[0].Voltage.Magnitude > 1e-30
            ? inputVoltage / propagation.Nodes[0].Voltage
            : Complex.One;
        antennaVoltage = propagation.Nodes[^1].Voltage * propagationScale;
        antennaCurrent = antennaVoltage / antennaImpedance;

        var reflectedPowerRatio = Math.Min(1.0, Math.Pow(reflectionCoefficient.Magnitude, 2));
        var acceptedPowerRatio = Math.Max(0.0, 1.0 - reflectedPowerRatio);
        var currentSquared = antennaCurrent.Magnitude * antennaCurrent.Magnitude;
        var deliveredPower = Math.Max(0.0, (antennaVoltage * Complex.Conjugate(antennaCurrent)).Real);
        var radiatedPower = currentSquared * antenna.RadiationResistanceOhms;
        var lossPower = currentSquared * antenna.LossResistanceOhms;
        var deliveredPowerRatio = deliveredPower / availablePower;
        var radiatedPowerRatio = radiatedPower / availablePower;
        var lossPowerRatio = lossPower / availablePower;
        var radiationEfficiency = deliveredPower > 0 ? radiatedPower / deliveredPower : 0;
        var conservationError = Math.Abs(1.0 - (reflectedPowerRatio + radiatedPowerRatio + lossPowerRatio));
        var returnLossDb = RfMath.ToDbMagnitude(reflectionCoefficient);
        var vswr = reflectionCoefficient.Magnitude < 1.0
            ? (1.0 + reflectionCoefficient.Magnitude) / Math.Max(1.0 - reflectionCoefficient.Magnitude, 1e-30)
            : double.PositiveInfinity;
        var scaledPropagation = ScalePropagationToSourceDrive(propagation, propagationScale);

        var physicalLength = scaledPropagation.Summary.PhysicalLengthMeters;
        var phaseDeg = scaledPropagation.Summary.TotalPhaseShiftDeg;
        var lambdaEff = physicalLength > 0 && Math.Abs(phaseDeg) > 1e-12
            ? 360.0 * physicalLength / Math.Abs(phaseDeg)
            : double.PositiveInfinity;
        var compressionRatio = double.IsFinite(lambdaEff) && lambdaEff > 0 ? lambda0 / lambdaEff : 0;
        var electricalLengthWithMediumDeg = double.IsFinite(lambdaEff) && lambdaEff > 0
            ? 360.0 * lengthMeters / lambdaEff
            : thetaDeg;

        return new SingleFrequencyAntennaResult
        {
            FrequencyHz = frequencyHz,
            AntennaType = antennaType,
            PhysicalAntennaLengthMeters = lengthMeters,
            Lambda0Meters = lambda0,
            AntennaLengthRatio = lengthRatio,
            ElectricalLengthDeg = thetaDeg,
            RadiationResistanceOhms = antenna.RadiationResistanceOhms,
            LossResistanceOhms = antenna.LossResistanceOhms,
            ReactanceOhms = antenna.ReactanceOhms,
            CapacitancePf = antenna.CapacitanceFarads * 1e12,
            AntennaImpedance = antennaImpedance,
            InputReturnLossDb = returnLossDb,
            InputImpedance = inputImpedance,
            InputReflectionCoefficient = reflectionCoefficient,
            Vswr = vswr,
            AvailablePowerWatts = availablePower,
            ReflectedPowerRatio = reflectedPowerRatio,
            AcceptedPowerRatio = acceptedPowerRatio,
            DeliveredPowerRatio = deliveredPowerRatio,
            RadiatedPowerRatio = radiatedPowerRatio,
            LossPowerRatio = lossPowerRatio,
            PowerConservationError = conservationError,
            RadiationEfficiency = radiationEfficiency,
            AntennaVoltageRms = antennaVoltage.Magnitude,
            AntennaCurrentRms = antennaCurrent.Magnitude,
            TotalPhaseDeg = phaseDeg,
            AverageSlowWaveFactor = scaledPropagation.Summary.AverageSlowWaveFactor,
            EffectiveWavelengthMeters = lambdaEff,
            CompressionRatio = compressionRatio,
            EquivalentFreeSpaceElectricalLengthDeg = electricalLengthWithMediumDeg,
            Propagation = scaledPropagation,
            PropagationWarning = propagationWarning,
            Structure = structure.ToList()
        };
    }

    private static PropagationResult ScalePropagationToSourceDrive(PropagationResult result, Complex voltageScale)
    {
        var powerScale = voltageScale.Magnitude * voltageScale.Magnitude;
        var nodes = result.Nodes
            .Select(node =>
            {
                var voltage = node.Voltage * voltageScale;
                return node with
                {
                    Voltage = voltage,
                    VoltageMagnitude = voltage.Magnitude
                };
            })
            .ToList();
        var cells = result.Cells
            .Select(cell =>
            {
                var current = cell.InductorCurrent * voltageScale;
                return cell with
                {
                    InductorCurrent = current,
                    InductorEnergyJoules = cell.InductorEnergyJoules * powerScale
                };
            })
            .ToList();
        var capacitors = result.Capacitors
            .Select(capacitor =>
            {
                var voltage = capacitor.Voltage * voltageScale;
                return capacitor with
                {
                    Voltage = voltage,
                    CapacitorEnergyJoules = capacitor.CapacitorEnergyJoules * powerScale
                };
            })
            .ToList();
        var diagnostics = result.Diagnostics with
        {
            PowerEnteringStructureWatts = result.Diagnostics.PowerEnteringStructureWatts * powerScale,
            PowerDeliveredToLoadWatts = result.Diagnostics.PowerDeliveredToLoadWatts * powerScale,
            ReflectedPowerFromInputWatts = result.Diagnostics.ReflectedPowerFromInputWatts * powerScale,
            ReflectedPowerFromOutputLoadWatts = result.Diagnostics.ReflectedPowerFromOutputLoadWatts * powerScale,
            Cells = result.Diagnostics.Cells
                .Select(cell => cell with
                {
                    NodeVoltageMagnitude = cell.NodeVoltageMagnitude * voltageScale.Magnitude,
                    NodeCurrentMagnitude = cell.NodeCurrentMagnitude * voltageScale.Magnitude
                })
                .ToList()
        };

        return result with
        {
            Nodes = nodes,
            Cells = cells,
            Capacitors = capacitors,
            Diagnostics = diagnostics
        };
    }

    private double GetSingleAntennaSpacingMeters()
    {
        if (_antennaSpacingMmInput.Value <= 0)
        {
            _antennaSpacingMmInput.Value = 5;
        }

        return Math.Max((double)_antennaSpacingMmInput.Value * 1e-3, 1e-9);
    }

    private ShortAntennaModel BuildShortAntennaModel(double frequencyHz, double lengthMeters, double lambda0)
    {
        if (_singleAntennaUseCustomInput.Checked)
        {
            var xant = (double)_singleAntennaCustomXantInput.Value;
            return new ShortAntennaModel(
                (double)_singleAntennaCustomRradInput.Value,
                (double)_singleAntennaCustomRlossInput.Value,
                xant,
                ReactanceToCapacitance(frequencyHz, xant));
        }

        var ratio = lengthMeters / lambda0;
        var rrad = _singleAntennaTypeInput.SelectedIndex == 1
            ? 80.0 * Math.PI * Math.PI * ratio * ratio
            : 40.0 * Math.PI * Math.PI * ratio * ratio;
        var rloss = (double)_singleAntennaRlossInput.Value;
        var capacitance = (double)_singleAntennaCantPfInput.Value * 1e-12;
        var reactance = -1.0 / (2.0 * Math.PI * frequencyHz * capacitance);
        return new ShortAntennaModel(rrad, rloss, reactance, capacitance);
    }

    private IReadOnlyList<RfElement> GetSingleAntennaStructure()
    {
        _singleAntennaUseCurrentStructureInput.Checked = true;
        return SimulationEngine.ParseStructureAllowEmpty(_structureInput.Text);
    }

    private void RenderSingleAntennaResult(SingleFrequencyAntennaResult result)
    {
        _antennaSummaryText.Text = FormatSingleAntennaSummary(result);
        if (result.Propagation is not null)
        {
            _antennaNodeVoltagePlot.Model = PlotFactory.BuildNodeVoltageMagnitudeModel(result.Propagation);
            _antennaNodePhasePlot.Model = PlotFactory.BuildNodePhaseModel(result.Propagation);
            _antennaLocalPhaseShiftPlot.Model = PlotFactory.BuildLocalPhaseShiftModel(result.Propagation);
            _antennaSlowWaveFactorPlot.Model = PlotFactory.BuildSlowWaveFactorModel(result.Propagation);
            _antennaStoredEnergyPlot.Model = PlotFactory.BuildStoredEnergyModel(result.Propagation);
        }
        else
        {
            var unavailable = BuildSingleAntennaUnavailablePlot(result.PropagationWarning);
            _antennaNodeVoltagePlot.Model = unavailable;
            _antennaNodePhasePlot.Model = BuildSingleAntennaUnavailablePlot(result.PropagationWarning);
            _antennaLocalPhaseShiftPlot.Model = BuildSingleAntennaUnavailablePlot(result.PropagationWarning);
            _antennaSlowWaveFactorPlot.Model = BuildSingleAntennaUnavailablePlot(result.PropagationWarning);
            _antennaStoredEnergyPlot.Model = BuildSingleAntennaUnavailablePlot(result.PropagationWarning);
        }
        _singleAntennaPowerFlowPlot.Model = BuildSingleAntennaPowerFlowModel(result);
    }

    private static PlotModel BuildSingleAntennaUnavailablePlot(string reason) =>
        new()
        {
            Title = string.IsNullOrWhiteSpace(reason) ? "Propagation unavailable" : reason,
            PlotAreaBorderColor = OxyColors.Gray
        };

    private string FormatSingleAntennaSummary(SingleFrequencyAntennaResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Operating frequency: {result.FrequencyHz / 1e6:F6} MHz");
        sb.AppendLine($"Free-space wavelength: {result.Lambda0Meters:F3} m");
        sb.AppendLine($"Physical antenna length: {result.PhysicalAntennaLengthMeters:F3} m");
        sb.AppendLine($"Antenna is lambda0 / {(result.AntennaLengthRatio > 0 ? 1.0 / result.AntennaLengthRatio : 0):F2}");
        sb.AppendLine($"Electrical length: {result.ElectricalLengthDeg:F3} deg");
        sb.AppendLine();
        sb.AppendLine($"Antenna model: {result.AntennaType}");
        sb.AppendLine($"Rrad: {result.RadiationResistanceOhms:F6} ohm");
        sb.AppendLine($"Rloss: {result.LossResistanceOhms:F6} ohm");
        sb.AppendLine($"Xant: {result.ReactanceOhms:F3} ohm");
        sb.AppendLine($"Cant: {result.CapacitancePf:F3} pF");
        sb.AppendLine($"Zant: {FormatComplex(result.AntennaImpedance)} ohm");
        sb.AppendLine();
        sb.AppendLine("Debug network:");
        sb.AppendLine($"Zsource: {SmallAntennaMode.SourceResistanceOhms:F4} ohm");
        sb.AppendLine($"Zin: {FormatComplex(result.InputImpedance)} ohm");
        sb.AppendLine($"Zant: {FormatComplex(result.AntennaImpedance)} ohm");
        sb.AppendLine($"Gamma: {FormatComplex(result.InputReflectionCoefficient)} |Gamma|={result.InputReflectionCoefficient.Magnitude:F6}");
        sb.AppendLine($"VSWR: {FormatFinite(result.Vswr, "F3")}");
        sb.AppendLine();
        sb.AppendLine($"Input return loss: {result.InputReturnLossDb:F2} dB");
        sb.AppendLine($"Input impedance: {FormatComplex(result.InputImpedance)} ohm");
        sb.AppendLine($"Reflection coefficient magnitude: {result.InputReflectionCoefficient.Magnitude:F6}");
        sb.AppendLine($"Available source power: {result.AvailablePowerWatts:F6} W");
        sb.AppendLine($"Reflected power ratio: {result.ReflectedPowerRatio:F6} ({result.ReflectedPowerRatio * 100.0:F3} %)");
        sb.AppendLine($"Accepted power ratio: {result.AcceptedPowerRatio:F6} ({result.AcceptedPowerRatio * 100.0:F3} %)");
        sb.AppendLine($"Delivered power ratio: {result.DeliveredPowerRatio:F6} ({ToPowerDb(result.DeliveredPowerRatio):F2} dB)");
        sb.AppendLine($"Radiated power ratio: {result.RadiatedPowerRatio:F6} ({ToPowerDb(result.RadiatedPowerRatio):F2} dB)");
        sb.AppendLine($"Loss power ratio: {result.LossPowerRatio:F6} ({ToPowerDb(result.LossPowerRatio):F2} dB)");
        sb.AppendLine($"Power conservation error: {result.PowerConservationError:E3}");
        sb.AppendLine($"Radiation efficiency: {result.RadiationEfficiency * 100.0:F2} %");
        sb.AppendLine($"Antenna voltage RMS: {result.AntennaVoltageRms:F3} V");
        sb.AppendLine($"Antenna current RMS: {result.AntennaCurrentRms:F6} A");
        sb.AppendLine();
        sb.AppendLine($"LC total phase accumulation: {result.TotalPhaseDeg:F2} deg");
        sb.AppendLine($"Average slow-wave factor: {result.AverageSlowWaveFactor:F3}");
        sb.AppendLine($"Effective wavelength in medium: {FormatFinite(result.EffectiveWavelengthMeters, "F6")} m");
        sb.AppendLine($"Compression ratio: {result.CompressionRatio:F3}");
        sb.AppendLine($"Effective antenna length through medium: {result.PhysicalAntennaLengthMeters / Math.Max(result.EffectiveWavelengthMeters, 1e-30):F6} lambda_eff");
        sb.AppendLine($"Equivalent free-space electrical length: {result.EquivalentFreeSpaceElectricalLengthDeg:F3} deg");
        sb.AppendLine();
        sb.AppendLine(FormatSingleAntennaDiagnostic(result));
        if (!string.IsNullOrWhiteSpace(result.PropagationWarning))
        {
            sb.AppendLine();
            sb.AppendLine(result.PropagationWarning);
        }
        return sb.ToString();
    }

    private static string FormatSingleAntennaDiagnostic(SingleFrequencyAntennaResult result)
    {
        var diagnostics = AnalyzeSingleAntennaPropagationDiagnostics(result);
        return FormatPropagationDiagnosticReport(diagnostics);
    }

    private static SingleAntennaPropagationDiagnostics AnalyzeSingleAntennaPropagationDiagnostics(SingleFrequencyAntennaResult result)
    {
        var good = new List<string>();
        var warnings = new List<string>();
        var failures = new List<string>();
        var notes = new List<string>();

        if (result.Propagation is null)
        {
            failures.Add("Travelling-wave propagation cannot be evaluated because the loaded propagation solver did not return a result.");
            return new SingleAntennaPropagationDiagnostics
            {
                Verdict = "FAIL",
                Interpretation = "Propagation analysis unavailable.",
                GoodSigns = good,
                WarningSigns = warnings,
                FailureSigns = failures,
                HeuristicMetrics = notes,
                ReflectedPowerPercent = result.ReflectedPowerRatio * 100.0,
                AcceptedPowerPercent = result.AcceptedPowerRatio * 100.0
            };
        }

        var propagation = result.Propagation;
        var phase = propagation.Nodes.Select(node => node.PhaseDeg).Where(double.IsFinite).ToList();
        var localPhase = propagation.Cells.Select(cell => cell.DeltaPhaseDeg).Where(double.IsFinite).ToList();
        var localPhaseAbs = localPhase.Select(Math.Abs).ToList();
        var slowWave = propagation.Cells
            .Select(cell => cell.SlowWaveFactor)
            .Where(value => double.IsFinite(value) && value > 0)
            .ToList();
        var energies = propagation.Cells.Select((cell, index) =>
            cell.InductorEnergyJoules + (index < propagation.Capacitors.Count
                ? propagation.Capacitors[index].CapacitorEnergyJoules
                : 0)).ToList();

        var phaseMonotonicity = CalculateMonotonicityFraction(phase);
        var localPhaseSmoothness = CalculateSmoothnessRatio(localPhaseAbs);
        var phaseFlatteningFraction = localPhaseAbs.Count == 0
            ? 1.0
            : localPhaseAbs.Count(value => value < 1.0) / (double)localPhaseAbs.Count;
        var maxSwf = slowWave.Count > 0 ? slowWave.Max() : 0;
        var medianSwf = Median(slowWave);
        var swfPeakWidth = slowWave.Count > 0 && maxSwf > 0
            ? slowWave.Count(value => value >= 0.5 * maxSwf)
            : 0;
        var swfSpike = slowWave.Count > 2 && maxSwf > 3.0 * Math.Max(medianSwf, 1e-30) && swfPeakWidth <= 2;
        var energyTotal = energies.Sum();
        var maxEnergyFraction = energyTotal > 0 ? energies.Max() / energyTotal : 1.0;
        var energyEntropy = CalculateNormalizedEntropy(energies);
        var reflected = result.ReflectedPowerRatio;
        var accepted = result.AcceptedPowerRatio;
        var terminalReflectionSevere = reflected > 0.80;
        var terminalReflectionModerate = reflected > 0.25 && reflected <= 0.80;
        var travellingWaveScore = 0;

        if (phaseMonotonicity >= 0.85)
        {
            good.Add("Smooth node phase accumulation is present.");
            travellingWaveScore++;
        }
        else if (phaseMonotonicity >= 0.60)
        {
            warnings.Add("Partial standing-wave formation: node phase is only partly monotonic.");
        }
        else
        {
            failures.Add("Node phase is not monotonic enough; travelling-wave propagation is likely lost.");
        }

        if (localPhaseSmoothness >= 0.55 && phaseFlatteningFraction < 0.50)
        {
            good.Add("Local phase shift remains distributed across the structure.");
            travellingWaveScore++;
        }
        else if (phaseFlatteningFraction >= 0.70)
        {
            failures.Add("Local phase shift is near zero over most cells; propagation has partially collapsed.");
        }
        else
        {
            warnings.Add("Local phase distribution is uneven; reflection influence or localized compression may be present.");
        }

        if (slowWave.Count == 0 || result.CompressionRatio <= 1)
        {
            failures.Add("Effective wavelength compression is no longer meaningful.");
        }
        else if (swfSpike)
        {
            failures.Add("Single-cell or narrow SWF spike detected; compression is resonance-localized.");
        }
        else if (swfPeakWidth >= Math.Max(2, slowWave.Count / 3))
        {
            good.Add("Broad slow-wave compression region is present.");
            travellingWaveScore++;
        }
        else
        {
            warnings.Add("SWF is becoming localized; compression region is narrowing.");
        }

        if (energyEntropy >= 0.70 && maxEnergyFraction <= 0.30)
        {
            good.Add("Stored energy is distributed across many cells.");
            travellingWaveScore++;
        }
        else if (maxEnergyFraction >= 0.55)
        {
            failures.Add("Strong resonance localization: stored energy is trapped in one cell or terminal region.");
        }
        else
        {
            warnings.Add("Stored-energy hill is narrowing; monitor for resonance localization.");
        }

        if (terminalReflectionSevere)
        {
            failures.Add("Severe terminal/source reflection: most available power is reflected.");
        }
        else if (terminalReflectionModerate)
        {
            warnings.Add("Moderate terminal reflection observed; standing-wave influence may be visible near the output.");
        }
        else
        {
            good.Add("Terminal load is reasonably matched to the medium.");
            travellingWaveScore++;
        }

        if (accepted > 0.50)
        {
            good.Add("Good wave penetration into the terminal region.");
        }
        else if (accepted > 0.10)
        {
            warnings.Add("Wave penetration into the terminal region is limited by mismatch.");
        }
        else
        {
            failures.Add("Wave penetration into the terminal region is poor.");
        }

        if (result.RadiatedPowerRatio < 1e-6)
            warnings.Add("Antenna radiated power is extremely small at this operating point.");
        if (result.AntennaVoltageRms > 1000)
            warnings.Add("Antenna voltage is very high compared with radiated power.");

        notes.Add($"Phase monotonicity: {phaseMonotonicity * 100.0:F1} %");
        notes.Add($"Local phase smoothness ratio: {localPhaseSmoothness:F3}");
        notes.Add($"Phase-flattened cells: {phaseFlatteningFraction * 100.0:F1} %");
        notes.Add($"SWF peak width: {swfPeakWidth} of {slowWave.Count} cells");
        notes.Add($"Max energy fraction: {maxEnergyFraction * 100.0:F1} %");
        notes.Add($"Energy entropy: {energyEntropy:F3}");
        notes.Add($"Reflected / accepted power: {reflected * 100.0:F1} % / {accepted * 100.0:F1} %");

        string verdict;
        string interpretation;
        if (failures.Count > 0 && travellingWaveScore >= 3)
        {
            verdict = "FAIL";
            interpretation = "Distributed behavior is present, but a severe propagation/load issue was detected.";
        }
        else if (failures.Count > 0)
        {
            verdict = "FAIL";
            interpretation = "Propagation has partially collapsed into standing-wave or localized-resonance behavior.";
        }
        else if (warnings.Count > 0)
        {
            verdict = "WARNING";
            interpretation = "Distributed travelling-wave slow-wave propagation is present with reflection/localization concerns.";
        }
        else
        {
            verdict = "PASS";
            interpretation = "Distributed travelling-wave slow-wave propagation is still present.";
        }

        return new SingleAntennaPropagationDiagnostics
        {
            Verdict = verdict,
            Interpretation = interpretation,
            PhaseMonotonicityPercent = phaseMonotonicity * 100.0,
            LocalPhaseSmoothnessRatio = localPhaseSmoothness,
            PhaseFlattenedCellsPercent = phaseFlatteningFraction * 100.0,
            SwfPeakWidthCells = swfPeakWidth,
            SwfTotalCells = slowWave.Count,
            MaxEnergyFraction = maxEnergyFraction,
            EnergyEntropy = energyEntropy,
            ReflectedPowerPercent = reflected * 100.0,
            AcceptedPowerPercent = accepted * 100.0,
            GoodSigns = good,
            WarningSigns = warnings,
            FailureSigns = failures,
            HeuristicMetrics = notes
        };
    }

    private static string FormatPropagationDiagnosticReport(SingleAntennaPropagationDiagnostics diagnostics)
    {
        var verdictLine = diagnostics.Verdict + ": " + diagnostics.Interpretation;
        var sb = new StringBuilder();
        sb.AppendLine("Propagation interpretation:");
        sb.AppendLine(verdictLine);
        AppendDiagnosticSection(sb, "Good signs", diagnostics.GoodSigns);
        AppendDiagnosticSection(sb, "Warning signs", diagnostics.WarningSigns);
        AppendDiagnosticSection(sb, "Failure signs", diagnostics.FailureSigns);
        AppendDiagnosticSection(sb, "Heuristic metrics", diagnostics.HeuristicMetrics);
        return sb.ToString().TrimEnd();
    }

    private static void AppendDiagnosticSection(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine(title + ":");
        foreach (var item in items)
        {
            sb.AppendLine("- " + item);
        }
    }

    private static double CalculateMonotonicityFraction(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var deltas = values.Zip(values.Skip(1), (left, right) => right - left)
            .Where(delta => Math.Abs(delta) > 1e-9)
            .ToList();
        if (deltas.Count == 0)
        {
            return 0;
        }

        var positive = deltas.Count(delta => delta > 0);
        var negative = deltas.Count - positive;
        return Math.Max(positive, negative) / (double)deltas.Count;
    }

    private static double CalculateSmoothnessRatio(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return values.Count == 1 && values[0] > 0 ? 1.0 : 0.0;
        }

        var mean = values.Average();
        if (mean <= 1e-30)
        {
            return 0;
        }

        var rmsError = Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2)));
        return 1.0 / (1.0 + rmsError / mean);
    }

    private static double CalculateNormalizedEntropy(IReadOnlyList<double> values)
    {
        var positive = values.Where(value => value > 0 && double.IsFinite(value)).ToList();
        if (positive.Count <= 1)
        {
            return 0;
        }

        var total = positive.Sum();
        if (total <= 0)
        {
            return 0;
        }

        var entropy = positive
            .Select(value => value / total)
            .Where(probability => probability > 0)
            .Sum(probability => -probability * Math.Log(probability));
        return entropy / Math.Log(positive.Count);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : 0.5 * (sorted[middle - 1] + sorted[middle]);
    }

    private PlotModel BuildSingleAntennaPowerFlowModel(SingleFrequencyAntennaResult result)
    {
        var model = new PlotModel { Title = "Power Flow / Distribution", PlotAreaBorderColor = OxyColors.Gray };
        var labels = new[] { "Available", "Reflected", "Accepted", "Radiated", "Loss" };
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = -0.5,
            Maximum = labels.Length - 0.5,
            MajorStep = 1,
            MinorStep = 1,
            LabelFormatter = value =>
            {
                var index = (int)Math.Round(value);
                return index >= 0 && index < labels.Length ? labels[index] : string.Empty;
            }
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Power ratio",
            Minimum = 0
        });
        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(184, 161, 0),
            MarkerFill = OxyColor.FromRgb(184, 161, 0),
            MarkerStroke = OxyColors.Gray,
            MarkerType = MarkerType.Square,
            StrokeThickness = 2
        };
        series.Points.Add(new DataPoint(0, 1.0));
        series.Points.Add(new DataPoint(1, result.ReflectedPowerRatio));
        series.Points.Add(new DataPoint(2, result.AcceptedPowerRatio));
        series.Points.Add(new DataPoint(3, result.RadiatedPowerRatio));
        series.Points.Add(new DataPoint(4, result.LossPowerRatio));
        model.Series.Add(series);
        return model;
    }

    private void ExploreSingleAntennaPropagationRegimes()
    {
        try
        {
            var baseStructure = GetSingleAntennaStructure().ToList();
            if (baseStructure.Count == 0)
            {
                _antennaStatusLabel.Text = "Regime exploration requires an LC structure.";
                return;
            }

            var outputFolder = SelectExplorationOutputFolder();
            var frequenciesMHz = BuildSweepValues(
                (double)_singleAntennaExploreStartMHzInput.Value,
                (double)_singleAntennaExploreStopMHzInput.Value,
                (double)_singleAntennaExploreStepMHzInput.Value);
            var radiationResistances = BuildSweepValues(
                (double)_singleAntennaExploreRradStartInput.Value,
                (double)_singleAntennaExploreRradStopInput.Value,
                (double)_singleAntennaExploreRradStepInput.Value);
            var reactances = BuildSweepValues(
                (double)_singleAntennaExploreXantStartInput.Value,
                (double)_singleAntennaExploreXantStopInput.Value,
                (double)_singleAntennaExploreXantStepInput.Value);
            var lengthMeters = (double)_singleAntennaLengthMInput.Value;
            var spacingMeters = GetSingleAntennaSpacingMeters();
            var lossResistance = (double)_singleAntennaExploreRlossInput.Value;
            var minimumAcceptedPower = (double)_singleAntennaExploreAcceptedMinInput.Value;
            var bestCount = (int)_singleAntennaExploreBestCountInput.Value;
            var optimizeTerminal = _singleAntennaExploreOptimizeTerminalInput.Checked;

            var cases = new List<SingleAntennaExplorationCase>();
            var totalCases = frequenciesMHz.Count * radiationResistances.Count * reactances.Count;
            var evaluated = 0;
            Cursor.Current = Cursors.WaitCursor;
            _singleAntennaExploreButton.Enabled = false;
            _antennaStatusLabel.Text = $"Exploring {totalCases} antenna-loaded propagation cases...";
            Application.DoEvents();

            foreach (var frequencyMHz in frequenciesMHz)
            {
                var frequencyHz = frequencyMHz * 1e6;
                foreach (var rrad in radiationResistances)
                {
                    foreach (var xant in reactances)
                    {
                        var candidate = EvaluateExplorationLoad(
                            baseStructure,
                            frequencyHz,
                            lengthMeters,
                            rrad,
                            lossResistance,
                            xant,
                            spacingMeters,
                            minimumAcceptedPower,
                            optimizeTerminal);
                        cases.Add(candidate);
                        evaluated++;
                        if (evaluated % 100 == 0)
                        {
                            _antennaStatusLabel.Text = $"Exploring propagation regimes: {evaluated} / {totalCases}";
                            Application.DoEvents();
                        }
                    }
                }
            }

            _lastExplorationCases = cases
                .OrderByDescending(item => item.Score)
                .ToList();
            var accepted = _lastExplorationCases
                .Where(item => !item.Rejected)
                .Take(bestCount)
                .ToList();
            var exportedFiles = outputFolder is null
                ? new Dictionary<SingleAntennaExplorationCase, string>()
                : ExportBestExplorationCases(accepted, outputFolder);

            var best = accepted.FirstOrDefault() ?? _lastExplorationCases.FirstOrDefault();
            if (best is not null)
            {
                _lastSingleAntennaResult = best.Result;
                RenderSingleAntennaResult(best.Result);
                _antennaSummaryText.Text = FormatExplorationSummary(
                    _lastExplorationCases,
                    accepted,
                    exportedFiles,
                    totalCases,
                    outputFolder);
            }

            _antennaStatusLabel.Text = accepted.Count > 0
                ? $"Regime exploration complete. Accepted {accepted.Count} ranked cases from {totalCases} loads."
                : $"Regime exploration complete. No accepted cases from {totalCases} loads.";
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Regime exploration error: {ex.Message}";
            _antennaSummaryText.Text = ex.ToString();
        }
        finally
        {
            Cursor.Current = Cursors.Default;
            _singleAntennaExploreButton.Enabled = true;
        }
    }

    private string? SelectExplorationOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select folder for best propagation-regime JSON exports. Cancel to only show ranked results.",
            UseDescriptionForTitle = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private SingleAntennaExplorationCase EvaluateExplorationLoad(
        IReadOnlyList<RfElement> baseStructure,
        double frequencyHz,
        double lengthMeters,
        double radiationResistance,
        double lossResistance,
        double reactance,
        double spacingMeters,
        double minimumAcceptedPower,
        bool optimizeTerminal)
    {
        var antenna = new ShortAntennaModel(
            radiationResistance,
            lossResistance,
            reactance,
            ReactanceToCapacitance(frequencyHz, reactance));
        var structures = optimizeTerminal
            ? BuildTerminalExplorationVariants(baseStructure)
            : new List<List<RfElement>> { OptimizerEngine.CloneStructure(baseStructure) };

        SingleAntennaExplorationCase? best = null;
        foreach (var structure in structures)
        {
            var result = AnalyzeSingleFrequencyAntenna(
                structure,
                frequencyHz,
                lengthMeters,
                "Explored antenna load",
                antenna,
                spacingMeters);
            var diagnostics = AnalyzeSingleAntennaPropagationDiagnostics(result);
            var score = ScoreExplorationCase(result, diagnostics);
            var rejectionReason = GetExplorationRejectionReason(result, diagnostics, minimumAcceptedPower);
            var candidate = new SingleAntennaExplorationCase
            {
                Result = result,
                Diagnostics = diagnostics,
                Score = score,
                Rejected = rejectionReason.Length > 0,
                RejectionReason = rejectionReason,
                TerminalWasVaried = !ReferenceEquals(structure, structures[0])
            };
            if (best is null || candidate.Score > best.Score || (best.Rejected && !candidate.Rejected))
            {
                best = candidate;
            }
        }

        return best ?? throw new InvalidOperationException("No exploration candidate was evaluated.");
    }

    private static List<List<RfElement>> BuildTerminalExplorationVariants(IReadOnlyList<RfElement> baseStructure)
    {
        var variants = new List<List<RfElement>> { OptimizerEngine.CloneStructure(baseStructure) };
        var startIndex = Math.Max(0, baseStructure.Count - 3);
        for (var index = startIndex; index < baseStructure.Count; index++)
        {
            foreach (var factor in new[] { 0.9, 1.1 })
            {
                var variant = OptimizerEngine.CloneStructure(baseStructure);
                variant[index].Value = Math.Max(variant[index].Value * factor, 1e-18);
                variants.Add(variant);
            }
        }

        return variants;
    }

    private static double ScoreExplorationCase(
        SingleFrequencyAntennaResult result,
        SingleAntennaPropagationDiagnostics diagnostics)
    {
        var accepted = Clamp01(result.AcceptedPowerRatio);
        var reflected = Clamp01(result.ReflectedPowerRatio);
        var localPhaseSmoothness = Clamp01(diagnostics.LocalPhaseSmoothnessRatio);
        var swfWidth = diagnostics.SwfTotalCells > 0
            ? Clamp01(diagnostics.SwfPeakWidthCells / (double)diagnostics.SwfTotalCells)
            : 0;
        var entropy = Clamp01(diagnostics.EnergyEntropy);
        var phaseNorm = Clamp01(Math.Abs(result.TotalPhaseDeg) / 360.0);
        var swfLocalizationPenalty = diagnostics.SwfPeakWidthCells < 2
            ? 1.0
            : Clamp01(0.25 - swfWidth);
        var energyLocalizationPenalty = Clamp01((diagnostics.MaxEnergyFraction - 0.25) / 0.35);

        return 2.0 * accepted
               + 1.5 * localPhaseSmoothness
               + 1.5 * swfWidth
               + entropy
               + phaseNorm
               - 2.0 * reflected
               - 2.0 * swfLocalizationPenalty
               - 2.0 * energyLocalizationPenalty;
    }

    private static string GetExplorationRejectionReason(
        SingleFrequencyAntennaResult result,
        SingleAntennaPropagationDiagnostics diagnostics,
        double minimumAcceptedPower)
    {
        var reasons = new List<string>();
        if (result.Propagation is null)
            reasons.Add("propagation unavailable");
        if (result.AcceptedPowerRatio < minimumAcceptedPower)
            reasons.Add($"accepted power below {minimumAcceptedPower:P0}");
        if (result.ReflectedPowerRatio > 0.85)
            reasons.Add("extreme reflection");
        if (diagnostics.SwfPeakWidthCells < 2)
            reasons.Add("isolated SWF spike or missing broad compression");
        if (diagnostics.MaxEnergyFraction > 0.55)
            reasons.Add("energy localized in one cell");
        if (diagnostics.PhaseFlattenedCellsPercent > 70)
            reasons.Add("phase collapse");
        if (diagnostics.Verdict.Equals("FAIL", StringComparison.OrdinalIgnoreCase))
            reasons.Add("diagnostic verdict FAIL");
        return string.Join("; ", reasons.Distinct());
    }

    private static List<double> BuildSweepValues(double start, double stop, double step)
    {
        if (step <= 0 || !double.IsFinite(step))
        {
            throw new InvalidOperationException("Sweep step must be positive.");
        }

        var values = new List<double>();
        var direction = stop >= start ? 1.0 : -1.0;
        var signedStep = Math.Abs(step) * direction;
        var value = start;
        for (var index = 0; index < 100_000; index++)
        {
            if (direction > 0 && value > stop + 1e-12)
                break;
            if (direction < 0 && value < stop - 1e-12)
                break;
            values.Add(value);
            value += signedStep;
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException("Sweep range did not produce any values.");
        }

        return values;
    }

    private Dictionary<SingleAntennaExplorationCase, string> ExportBestExplorationCases(
        IReadOnlyList<SingleAntennaExplorationCase> cases,
        string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var files = new Dictionary<SingleAntennaExplorationCase, string>();
        foreach (var item in cases)
        {
            var caseLabel = BuildSingleAntennaCaseLabel(item.Result, item.Diagnostics);
            var scoreLabel = FormatScoreLabel(item.Score);
            var fileName = SanitizeCaseLabel($"best_{caseLabel}_score{scoreLabel}") + ".json";
            var path = Path.Combine(outputFolder, fileName);
            var export = SingleAntennaExplorationExport.FromCase(item, caseLabel);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    export,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    }));
            files[item] = fileName;
        }

        return files;
    }

    private static string FormatExplorationSummary(
        IReadOnlyList<SingleAntennaExplorationCase> allCases,
        IReadOnlyList<SingleAntennaExplorationCase> acceptedCases,
        IReadOnlyDictionary<SingleAntennaExplorationCase, string> exportedFiles,
        int totalCases,
        string? outputFolder)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Propagation-regime exploration");
        sb.AppendLine($"Evaluated loads: {totalCases}");
        sb.AppendLine($"Accepted ranked cases: {acceptedCases.Count}");
        sb.AppendLine($"Rejected cases: {allCases.Count(item => item.Rejected)}");
        sb.AppendLine(outputFolder is null
            ? "Export: skipped by user"
            : $"Export folder: {outputFolder}");
        sb.AppendLine();

        if (acceptedCases.Count == 0)
        {
            sb.AppendLine("No accepted distributed propagation regimes found.");
            sb.AppendLine("Best rejected cases:");
            AppendExplorationTable(sb, allCases.Take(10).ToList(), exportedFiles);
            return sb.ToString();
        }

        sb.AppendLine("Ranked best distributed propagation regimes:");
        AppendExplorationTable(sb, acceptedCases, exportedFiles);
        sb.AppendLine();
        sb.AppendLine("Best-case propagation interpretation:");
        sb.AppendLine(FormatPropagationDiagnosticReport(acceptedCases[0].Diagnostics));
        return sb.ToString();
    }

    private static void AppendExplorationTable(
        StringBuilder sb,
        IReadOnlyList<SingleAntennaExplorationCase> cases,
        IReadOnlyDictionary<SingleAntennaExplorationCase, string> exportedFiles)
    {
        sb.AppendLine("Rank  Score   F MHz    Zant ohm             Accepted  SWF width  Verdict  File/Reason");
        sb.AppendLine("----  ------  -------  -------------------  --------  ---------  -------  ------------------------------");
        for (var index = 0; index < cases.Count; index++)
        {
            var item = cases[index];
            var fileOrReason = exportedFiles.TryGetValue(item, out var file)
                ? file
                : item.RejectionReason;
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,4}  {1,6:F3}  {2,7:F3}  {3,-19}  {4,8:P1}  {5,3}/{6,-3}    {7,-7}  {8}",
                index + 1,
                item.Score,
                item.Result.FrequencyHz / 1e6,
                FormatComplex(item.Result.AntennaImpedance),
                item.Result.AcceptedPowerRatio,
                item.Diagnostics.SwfPeakWidthCells,
                item.Diagnostics.SwfTotalCells,
                item.Diagnostics.Verdict,
                fileOrReason));
        }
    }

    private static string FormatScoreLabel(double score)
    {
        var sign = score < 0 ? "m" : string.Empty;
        return sign + Math.Abs(score).ToString("0.###", CultureInfo.InvariantCulture).Replace(".", "p");
    }

    private static double Clamp01(double value) =>
        double.IsFinite(value) ? Math.Min(1.0, Math.Max(0.0, value)) : 0.0;

    private void OptimizeSingleAntennaTerminalCoupling()
    {
        try
        {
            var initial = GetSingleAntennaStructure().ToList();
            if (initial.Count == 0)
            {
                _antennaStatusLabel.Text = "Terminal optimization requires an LC structure.";
                return;
            }
            var scope = GetTerminalScopeElementCount(initial.Count);
            if (scope == 0)
            {
                _antennaStatusLabel.Text = "Optimization scope is None.";
                return;
            }

            var best = initial;
            var bestScore = ScoreSingleAntennaTerminalCandidate(AnalyzeSingleFrequencyAntenna(best));
            var startIndex = Math.Max(0, initial.Count - scope);
            for (var index = startIndex; index < initial.Count; index++)
            {
                var element = initial[index];
                var values = element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
                    ? OptimizerEngine.AvailableInductors
                    : element.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase)
                        ? OptimizerEngine.AvailableCapacitors
                        : Array.Empty<double>();
                foreach (var value in values)
                {
                    var candidate = OptimizerEngine.CloneStructure(best);
                    candidate[index].Value = value;
                    var score = ScoreSingleAntennaTerminalCandidate(AnalyzeSingleFrequencyAntenna(candidate));
                    if (score < bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
            }

            _terminalCouplingCandidate = best;
            _singleAntennaApplyTerminalButton.Enabled = true;
            var result = AnalyzeSingleFrequencyAntenna(best);
            _lastSingleAntennaResult = result;
            RenderSingleAntennaResult(result);
            _antennaStatusLabel.Text = $"Terminal coupling candidate ready. Score: {bestScore:F4}";
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Terminal optimization error: {ex.Message}";
        }
    }

    private int GetTerminalScopeElementCount(int elementCount)
    {
        return _singleAntennaOptimizationScopeInput.SelectedIndex switch
        {
            0 => 0,
            1 => Math.Min(1, elementCount),
            2 => Math.Min(2, elementCount),
            3 => Math.Min(3, elementCount),
            _ => elementCount
        };
    }

    private static double ScoreSingleAntennaTerminalCandidate(SingleFrequencyAntennaResult result)
    {
        var reflection = Math.Pow(10.0, result.InputReturnLossDb / 20.0);
        var score = 25.0 * reflection
                    - 120.0 * result.RadiatedPowerRatio
                    - 30.0 * result.DeliveredPowerRatio
                    + Math.Pow(Math.Max(0, result.AntennaVoltageRms / 1000.0 - 1.0), 2) * 50.0;
        if (result.Propagation is not null)
        {
            var slowWave = result.Propagation.Cells
                .Select(cell => cell.SlowWaveFactor)
                .Where(value => double.IsFinite(value) && value > 0)
                .ToList();
            if (slowWave.Count > 1)
            {
                score += slowWave.Zip(slowWave.Skip(1), (a, b) => Math.Abs(b - a) / Math.Max(a, 1.0)).Sum();
            }
        }
        return score;
    }

    private void ApplySingleAntennaTerminalCoupling()
    {
        if (_terminalCouplingCandidate is null)
            return;
        _structureInput.Text = JsonSerializer.Serialize(_terminalCouplingCandidate, new JsonSerializerOptions { WriteIndented = true });
        UpdateStructureSummary(_terminalCouplingCandidate);
        SyncManualTuningFromStructure(_terminalCouplingCandidate);
        UpdateSingleFrequencySmallAntennaMode();
        _antennaStatusLabel.Text = "Terminal coupling candidate applied.";
    }

    private void ExportSingleAntennaJson()
    {
        try
        {
            var result = _lastSingleAntennaResult ?? AnalyzeSingleFrequencyAntenna(GetSingleAntennaStructure());
            var diagnostics = AnalyzeSingleAntennaPropagationDiagnostics(result);
            var caseLabel = BuildSingleAntennaCaseLabel(result, diagnostics);
            using var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|CSV summary (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"small_antenna_{caseLabel}.json"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            if (Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(dialog.FileName, BuildSingleAntennaCsvSummary(result, diagnostics, caseLabel));
            }
            else
            {
                File.WriteAllText(
                    dialog.FileName,
                    JsonSerializer.Serialize(
                        SingleFrequencyAntennaExport.FromResult(result, diagnostics, caseLabel),
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        }));
            }
            _antennaStatusLabel.Text = $"Exported antenna test: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Antenna test export error: {ex.Message}";
        }
    }

    private static string BuildSingleAntennaCaseLabel(
        SingleFrequencyAntennaResult result,
        SingleAntennaPropagationDiagnostics diagnostics)
    {
        var frequencyMHz = FormatCaseLabelNumber(result.FrequencyHz / 1e6);
        var resistance = FormatCaseLabelNumber(result.RadiationResistanceOhms + result.LossResistanceOhms);
        var reactanceMagnitude = FormatCaseLabelNumber(Math.Abs(result.ReactanceOhms));
        var reactance = result.ReactanceOhms < 0
            ? "-j" + reactanceMagnitude
            : "pj" + reactanceMagnitude;
        return SanitizeCaseLabel($"{frequencyMHz}MHz_Z{resistance}{reactance}_{diagnostics.Verdict}");
    }

    private static string FormatCaseLabelNumber(double value)
    {
        var rounded = Math.Round(value);
        var text = Math.Abs(value - rounded) < 1e-9
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
        return text.Replace(".", "p");
    }

    private static string SanitizeCaseLabel(string label)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(label.Length);
        foreach (var ch in label)
        {
            builder.Append(char.IsWhiteSpace(ch) || invalid.Contains(ch) || ch == '+'
                ? '_'
                : ch);
        }

        return builder.ToString();
    }

    private static string BuildSingleAntennaCsvSummary(
        SingleFrequencyAntennaResult result,
        SingleAntennaPropagationDiagnostics diagnostics,
        string caseLabel)
    {
        var header = string.Join(",",
            "case_label",
            "frequency_hz",
            "z_ant",
            "z_in",
            "input_return_loss_db",
            "vswr",
            "accepted_power_ratio",
            "reflected_power_ratio",
            "radiated_power_ratio",
            "total_phase_deg",
            "average_swf",
            "compression_ratio",
            "phase_monotonicity_percent",
            "local_phase_smoothness_ratio",
            "swf_peak_width_cells",
            "swf_total_cells",
            "energy_entropy",
            "max_energy_fraction",
            "verdict");
        var row = string.Join(",",
            Csv(caseLabel),
            Csv(result.FrequencyHz),
            Csv(FormatComplex(result.AntennaImpedance)),
            Csv(FormatComplex(result.InputImpedance)),
            Csv(result.InputReturnLossDb),
            Csv(result.Vswr),
            Csv(result.AcceptedPowerRatio),
            Csv(result.ReflectedPowerRatio),
            Csv(result.RadiatedPowerRatio),
            Csv(result.TotalPhaseDeg),
            Csv(result.AverageSlowWaveFactor),
            Csv(result.CompressionRatio),
            Csv(diagnostics.PhaseMonotonicityPercent),
            Csv(diagnostics.LocalPhaseSmoothnessRatio),
            Csv(diagnostics.SwfPeakWidthCells),
            Csv(diagnostics.SwfTotalCells),
            Csv(diagnostics.EnergyEntropy),
            Csv(diagnostics.MaxEnergyFraction),
            Csv(diagnostics.Verdict));
        return header + Environment.NewLine + row + Environment.NewLine;
    }

    private static string Csv(object value)
    {
        var text = value switch
        {
            double number => number.ToString("G17", CultureInfo.InvariantCulture),
            float number => number.ToString("G9", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static double ReactanceToCapacitance(double frequencyHz, double reactanceOhms) =>
        reactanceOhms == 0 ? 0 : 1.0 / (2.0 * Math.PI * frequencyHz * Math.Abs(reactanceOhms));

    private static string FormatComplex(Complex value) =>
        $"{value.Real:F4}{(value.Imaginary < 0 ? "-" : "+")}j{Math.Abs(value.Imaginary):F4}";

    private static string FormatFinite(double value, string format) =>
        double.IsFinite(value) ? value.ToString(format) : "n/a";

    private static double ToPowerDb(double ratio) => 10.0 * Math.Log10(Math.Max(ratio, 1e-30));

    private readonly record struct ShortAntennaModel(
        double RadiationResistanceOhms,
        double LossResistanceOhms,
        double ReactanceOhms,
        double CapacitanceFarads);

    private sealed class SingleAntennaPropagationDiagnostics
    {
        public string Verdict { get; set; } = "FAIL";
        public string Interpretation { get; set; } = string.Empty;
        public double PhaseMonotonicityPercent { get; set; }
        public double LocalPhaseSmoothnessRatio { get; set; }
        public double PhaseFlattenedCellsPercent { get; set; }
        public int SwfPeakWidthCells { get; set; }
        public int SwfTotalCells { get; set; }
        public double MaxEnergyFraction { get; set; }
        public double EnergyEntropy { get; set; }
        public double ReflectedPowerPercent { get; set; }
        public double AcceptedPowerPercent { get; set; }
        public List<string> GoodSigns { get; set; } = new();
        public List<string> WarningSigns { get; set; } = new();
        public List<string> FailureSigns { get; set; } = new();
        public List<string> HeuristicMetrics { get; set; } = new();
    }

    private sealed class SingleFrequencyAntennaResult
    {
        public double FrequencyHz { get; set; }
        public string AntennaType { get; set; } = string.Empty;
        public double PhysicalAntennaLengthMeters { get; set; }
        public double Lambda0Meters { get; set; }
        public double AntennaLengthRatio { get; set; }
        public double ElectricalLengthDeg { get; set; }
        public double RadiationResistanceOhms { get; set; }
        public double LossResistanceOhms { get; set; }
        public double ReactanceOhms { get; set; }
        public double CapacitancePf { get; set; }
        public Complex AntennaImpedance { get; set; }
        public double InputReturnLossDb { get; set; }
        public Complex InputImpedance { get; set; }
        public Complex InputReflectionCoefficient { get; set; }
        public double Vswr { get; set; }
        public double AvailablePowerWatts { get; set; }
        public double ReflectedPowerRatio { get; set; }
        public double AcceptedPowerRatio { get; set; }
        public double DeliveredPowerRatio { get; set; }
        public double RadiatedPowerRatio { get; set; }
        public double LossPowerRatio { get; set; }
        public double PowerConservationError { get; set; }
        public double RadiationEfficiency { get; set; }
        public double AntennaVoltageRms { get; set; }
        public double AntennaCurrentRms { get; set; }
        public double TotalPhaseDeg { get; set; }
        public double AverageSlowWaveFactor { get; set; }
        public double EffectiveWavelengthMeters { get; set; }
        public double CompressionRatio { get; set; }
        public double EquivalentFreeSpaceElectricalLengthDeg { get; set; }
        public PropagationResult? Propagation { get; set; }
        public string PropagationWarning { get; set; } = string.Empty;
        public List<RfElement> Structure { get; set; } = new();
    }

    private sealed class SingleAntennaExplorationCase
    {
        public SingleFrequencyAntennaResult Result { get; set; } = new();
        public SingleAntennaPropagationDiagnostics Diagnostics { get; set; } = new();
        public double Score { get; set; }
        public bool Rejected { get; set; }
        public string RejectionReason { get; set; } = string.Empty;
        public bool TerminalWasVaried { get; set; }
    }

    private sealed class SingleAntennaExplorationExport
    {
        public string CaseLabel { get; set; } = string.Empty;
        public double PropagationRegimeScore { get; set; }
        public bool Rejected { get; set; }
        public string RejectionReason { get; set; } = string.Empty;
        public bool TerminalWasVaried { get; set; }
        public SingleFrequencyAntennaExport Analysis { get; set; } = new();

        public static SingleAntennaExplorationExport FromCase(
            SingleAntennaExplorationCase item,
            string caseLabel) =>
            new()
            {
                CaseLabel = caseLabel,
                PropagationRegimeScore = item.Score,
                Rejected = item.Rejected,
                RejectionReason = item.RejectionReason,
                TerminalWasVaried = item.TerminalWasVaried,
                Analysis = SingleFrequencyAntennaExport.FromResult(item.Result, item.Diagnostics, caseLabel)
            };
    }

    private sealed class SingleFrequencyAntennaExport
    {
        public string CaseLabel { get; set; } = string.Empty;
        public double FrequencyHz { get; set; }
        public string AntennaType { get; set; } = string.Empty;
        public double AntennaLengthM { get; set; }
        public double Lambda0M { get; set; }
        public double AntennaLengthRatio { get; set; }
        public double RRadOhm { get; set; }
        public double RLossOhm { get; set; }
        public double XAntOhm { get; set; }
        public string ZAnt { get; set; } = string.Empty;
        public string ZIn { get; set; } = string.Empty;
        public string InputReflectionCoefficient { get; set; } = string.Empty;
        public double InputReflectionMagnitude { get; set; }
        public double Vswr { get; set; }
        public double AvailablePowerWatts { get; set; }
        public double ReflectedPowerRatio { get; set; }
        public double AcceptedPowerRatio { get; set; }
        public double InputReturnLossDb { get; set; }
        public double DeliveredPowerRatio { get; set; }
        public double RadiatedPowerRatio { get; set; }
        public double LossPowerRatio { get; set; }
        public double PowerConservationError { get; set; }
        public double RadiationEfficiency { get; set; }
        public double TotalPhaseDeg { get; set; }
        public double LambdaEffM { get; set; }
        public double CompressionRatio { get; set; }
        public double AverageSwf { get; set; }
        public SingleFrequencyAntennaExportDiagnostics Diagnostics { get; set; } = new();
        public List<RfElement> Structure { get; set; } = new();

        public static SingleFrequencyAntennaExport FromResult(
            SingleFrequencyAntennaResult result,
            SingleAntennaPropagationDiagnostics diagnostics,
            string caseLabel) =>
            new()
            {
                CaseLabel = caseLabel,
                FrequencyHz = result.FrequencyHz,
                AntennaType = result.AntennaType.Replace(' ', '_').ToLowerInvariant(),
                AntennaLengthM = result.PhysicalAntennaLengthMeters,
                Lambda0M = result.Lambda0Meters,
                AntennaLengthRatio = result.AntennaLengthRatio,
                RRadOhm = result.RadiationResistanceOhms,
                RLossOhm = result.LossResistanceOhms,
                XAntOhm = result.ReactanceOhms,
                ZAnt = FormatComplex(result.AntennaImpedance),
                ZIn = FormatComplex(result.InputImpedance),
                InputReflectionCoefficient = FormatComplex(result.InputReflectionCoefficient),
                InputReflectionMagnitude = result.InputReflectionCoefficient.Magnitude,
                Vswr = result.Vswr,
                AvailablePowerWatts = result.AvailablePowerWatts,
                ReflectedPowerRatio = result.ReflectedPowerRatio,
                AcceptedPowerRatio = result.AcceptedPowerRatio,
                InputReturnLossDb = result.InputReturnLossDb,
                DeliveredPowerRatio = result.DeliveredPowerRatio,
                RadiatedPowerRatio = result.RadiatedPowerRatio,
                LossPowerRatio = result.LossPowerRatio,
                PowerConservationError = result.PowerConservationError,
                RadiationEfficiency = result.RadiationEfficiency,
                TotalPhaseDeg = result.TotalPhaseDeg,
                LambdaEffM = result.EffectiveWavelengthMeters,
                CompressionRatio = result.CompressionRatio,
                AverageSwf = result.AverageSlowWaveFactor,
                Diagnostics = SingleFrequencyAntennaExportDiagnostics.FromDiagnostics(diagnostics),
                Structure = result.Structure
            };
    }

    private sealed class SingleFrequencyAntennaExportDiagnostics
    {
        public string Verdict { get; set; } = string.Empty;
        public string Interpretation { get; set; } = string.Empty;
        public double PhaseMonotonicityPercent { get; set; }
        public double LocalPhaseSmoothnessRatio { get; set; }
        public double PhaseFlattenedCellsPercent { get; set; }
        public int SwfPeakWidthCells { get; set; }
        public int SwfTotalCells { get; set; }
        public double MaxEnergyFraction { get; set; }
        public double EnergyEntropy { get; set; }
        public double ReflectedPowerPercent { get; set; }
        public double AcceptedPowerPercent { get; set; }

        public static SingleFrequencyAntennaExportDiagnostics FromDiagnostics(
            SingleAntennaPropagationDiagnostics diagnostics) =>
            new()
            {
                Verdict = diagnostics.Verdict,
                Interpretation = diagnostics.Interpretation,
                PhaseMonotonicityPercent = diagnostics.PhaseMonotonicityPercent,
                LocalPhaseSmoothnessRatio = diagnostics.LocalPhaseSmoothnessRatio,
                PhaseFlattenedCellsPercent = diagnostics.PhaseFlattenedCellsPercent,
                SwfPeakWidthCells = diagnostics.SwfPeakWidthCells,
                SwfTotalCells = diagnostics.SwfTotalCells,
                MaxEnergyFraction = diagnostics.MaxEnergyFraction,
                EnergyEntropy = diagnostics.EnergyEntropy,
                ReflectedPowerPercent = diagnostics.ReflectedPowerPercent,
                AcceptedPowerPercent = diagnostics.AcceptedPowerPercent
            };
    }
}
