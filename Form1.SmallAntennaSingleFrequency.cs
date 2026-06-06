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
    private readonly PlotView _singleAntennaPowerFlowPlot = new();
    private readonly ToolTip _singleAntennaToolTip = new();

    private SingleFrequencyAntennaResult? _lastSingleAntennaResult;
    private List<RfElement>? _terminalCouplingCandidate;

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
        var lengthRatio = lengthMeters / lambda0;
        var thetaDeg = 360.0 * lengthRatio;
        var antenna = BuildShortAntennaModel(frequencyHz, lengthMeters, lambda0);
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
            GetSingleAntennaSpacingMeters(),
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
            AntennaType = _singleAntennaTypeInput.Text,
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
        var failures = new List<string>();
        if (result.Propagation is null)
            failures.Add("Effective wavelength compression cannot be evaluated without an LC propagation result.");
        if (result.RadiatedPowerRatio < 1e-6)
            failures.Add("Antenna radiated power is extremely small.");
        if (result.AntennaVoltageRms > 1000)
            failures.Add("Antenna voltage is very high compared with radiated power.");
        if (result.Propagation is not null)
        {
            var slowWave = result.Propagation.Cells
                .Select(cell => cell.SlowWaveFactor)
                .Where(value => double.IsFinite(value) && value > 0)
                .ToList();
            if (slowWave.Count > 1 && slowWave.Max() > 4.0 * Math.Max(slowWave.Average(), 1e-30))
                failures.Add("SWF has an isolated spike.");
            var energies = result.Propagation.Cells.Select((cell, index) =>
                cell.InductorEnergyJoules + (index < result.Propagation.Capacitors.Count
                    ? result.Propagation.Capacitors[index].CapacitorEnergyJoules
                    : 0)).ToList();
            var totalEnergy = energies.Sum();
            if (totalEnergy > 0 && energies.Max() / totalEnergy > 0.35)
                failures.Add("Stored energy is localized in one element/cell.");
        }
        if (result.CompressionRatio <= 1)
            failures.Add("Effective wavelength compression is not present.");
        if (failures.Count == 0)
        {
            return "PASS:\r\n- Node phase is usable\r\n- No severe SWF or energy localization diagnostic tripped\r\n- Antenna receives measurable power\r\n- Effective wavelength compression exists";
        }
        return "FAIL:\r\n- " + string.Join("\r\n- ", failures);
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
            using var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = "single-frequency-antenna-test.json"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            File.WriteAllText(
                dialog.FileName,
                JsonSerializer.Serialize(
                    SingleFrequencyAntennaExport.FromResult(result),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    }));
            _antennaStatusLabel.Text = $"Exported antenna test: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _antennaStatusLabel.Text = $"Antenna test export error: {ex.Message}";
        }
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

    private sealed class SingleFrequencyAntennaExport
    {
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
        public List<RfElement> Structure { get; set; } = new();

        public static SingleFrequencyAntennaExport FromResult(SingleFrequencyAntennaResult result) =>
            new()
            {
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
                Structure = result.Structure
            };
    }
}
