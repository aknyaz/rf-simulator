using System.Numerics;
using System.Text.Json;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace RfSimulator;

public partial class Form1
{
    private static readonly string[] ManualElementTypes =
    {
        "SeriesL", "ShuntC", "SeriesC", "ShuntL", "SeriesR", "ShuntR"
    };

    private readonly DataGridView _manualElementsGrid = new();
    private readonly TrackBar _manualValueSlider = new();
    private readonly NumericUpDown _manualValueInput = new();
    private readonly Label _manualValueUnitLabel = new();
    private readonly CheckBox _manualLockInput = new();
    private readonly NumericUpDown _manualTargetMHzInput = new();
    private readonly NumericUpDown _manualSpacingMmInput = new();
    private readonly TextBox _manualMetricsText = new();
    private readonly Label _manualStatusLabel = new();
    private readonly Button _manualAddCellButton = new();
    private readonly Button _manualRemoveCellButton = new();
    private readonly Button _manualDuplicateCellButton = new();
    private readonly Button _manualInsertBeforeButton = new();
    private readonly Button _manualInsertAfterButton = new();
    private readonly Button _manualResetSelectedButton = new();
    private readonly Button _manualSetBaselineButton = new();
    private readonly Button _manualRevertBaselineButton = new();
    private readonly Button _manualSaveButton = new();
    private readonly Button _manualLoadButton = new();
    private readonly PlotView _manualSmithPlot = new();
    private readonly PlotView _manualS11Plot = new();
    private readonly PlotView _manualS21Plot = new();
    private readonly PlotView _manualPhasePlot = new();
    private readonly PlotView _manualGroupDelayPlot = new();
    private readonly PlotView _manualNodeVoltagePlot = new();
    private readonly PlotView _manualNodePhasePlot = new();
    private readonly PlotView _manualLocalPhasePlot = new();
    private readonly PlotView _manualSlowWavePlot = new();
    private readonly PlotView _manualEnergyPlot = new();
    private readonly System.Windows.Forms.Timer _manualLiveUpdateTimer = new() { Interval = 120 };

    private List<ManualElementState> _manualElements = new();
    private List<RfElement> _manualBaseline = new();
    private ManualTargetMetrics? _manualBaselineMetrics;
    private bool _manualEditorUpdating;

    private Control BuildManualTuningPanel()
    {
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1000, 600),
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 370,
            Panel1MinSize = 330,
            SplitterWidth = 8
        };
        root.Panel1.Controls.Add(BuildManualEditorPanel());
        root.Panel2.Controls.Add(BuildManualPlotsPanel());

        _manualLiveUpdateTimer.Tick += (_, _) =>
        {
            _manualLiveUpdateTimer.Stop();
            RunSimulation();
        };
        LoadManualStructureFromMainEditor();
        return root;
    }

    private Control BuildManualEditorPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(0, 0, 6, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildManualTargetPanel(), 0, 0);
        root.Controls.Add(BuildManualElementsGrid(), 0, 1);
        root.Controls.Add(BuildManualValueEditor(), 0, 2);
        root.Controls.Add(BuildManualActionPanel(), 0, 3);

        _manualMetricsText.Dock = DockStyle.Fill;
        _manualMetricsText.Multiline = true;
        _manualMetricsText.ReadOnly = true;
        _manualMetricsText.ScrollBars = ScrollBars.Vertical;
        _manualMetricsText.Font = new Font("Consolas", 9);
        _manualMetricsText.BackColor = SystemColors.Window;
        root.Controls.Add(_manualMetricsText, 0, 4);

        _manualStatusLabel.Dock = DockStyle.Fill;
        _manualStatusLabel.AutoEllipsis = true;
        _manualStatusLabel.BorderStyle = BorderStyle.FixedSingle;
        _manualStatusLabel.Padding = new Padding(4, 3, 4, 3);
        _manualStatusLabel.Text = "Ready";
        root.Controls.Add(_manualStatusLabel, 0, 5);
        return root;
    }

    private Control BuildManualTargetPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        ConfigureNumeric(_manualTargetMHzInput, 0.001m, 10000, 27.5m, 4, 0.1m);
        ConfigureNumeric(_manualSpacingMmInput, 0.001m, 1000, 5, 3, 0.5m);
        _manualSpacingMmInput.Dock = DockStyle.None;
        _manualSpacingMmInput.Width = 90;
        _manualSpacingMmInput.ValueChanged += (_, _) => ScheduleManualLiveSimulation();
        panel.Controls.Add(new Label { Text = "Model spacing (mm)", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        panel.Controls.Add(_manualSpacingMmInput);
        new ToolTip().SetToolTip(_manualSpacingMmInput, "Shared spatial interpretation only; does not change circuit S-parameters or antenna efficiency.");
        return panel;
    }

    private Control BuildManualElementsGrid()
    {
        _manualElementsGrid.Dock = DockStyle.Fill;
        _manualElementsGrid.AllowUserToAddRows = false;
        _manualElementsGrid.AllowUserToDeleteRows = false;
        _manualElementsGrid.AllowUserToResizeRows = false;
        _manualElementsGrid.MultiSelect = false;
        _manualElementsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _manualElementsGrid.RowHeadersVisible = false;
        _manualElementsGrid.AutoGenerateColumns = false;
        _manualElementsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Index",
            HeaderText = "#",
            Width = 36,
            ReadOnly = true
        });
        _manualElementsGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Type",
            HeaderText = "Type",
            DataSource = ManualElementTypes,
            Width = 84
        });
        _manualElementsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            HeaderText = "Value",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });
        _manualElementsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Locked",
            HeaderText = "Lock",
            Width = 44
        });
        _manualElementsGrid.SelectionChanged += (_, _) => UpdateManualSelectedEditor();
        _manualElementsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_manualElementsGrid.IsCurrentCellDirty)
                _manualElementsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _manualElementsGrid.CellValueChanged += (_, e) => HandleManualGridValueChanged(e.RowIndex, e.ColumnIndex);
        return _manualElementsGrid;
    }

    private Control BuildManualValueEditor()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _manualValueSlider.Dock = DockStyle.Fill;
        _manualValueSlider.Minimum = 0;
        _manualValueSlider.Maximum = 1000;
        _manualValueSlider.TickFrequency = 100;
        _manualValueSlider.ValueChanged += (_, _) => HandleManualSliderChanged();
        _manualValueInput.Dock = DockStyle.Fill;
        _manualValueInput.DecimalPlaces = 4;
        _manualValueInput.Minimum = 0.0001m;
        _manualValueInput.Maximum = 1000000;
        _manualValueInput.Increment = 0.1m;
        _manualValueInput.ValueChanged += (_, _) => HandleManualNumericChanged();
        _manualLockInput.Text = "Locked";
        _manualLockInput.AutoSize = true;
        _manualLockInput.CheckedChanged += (_, _) => HandleManualLockChanged();
        panel.Controls.Add(new Label { Text = "Log value", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.Controls.Add(_manualValueSlider, 1, 0);
        panel.Controls.Add(_manualValueInput, 2, 0);
        panel.Controls.Add(_manualValueUnitLabel, 3, 0);
        panel.Controls.Add(_manualLockInput, 1, 1);
        return panel;
    }

    private Control BuildManualActionPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 6)
        };
        ConfigureManualButton(_manualAddCellButton, "Add LC Cell", AddManualLcCell);
        ConfigureManualButton(_manualRemoveCellButton, "Remove Cell", RemoveManualCell);
        ConfigureManualButton(_manualDuplicateCellButton, "Duplicate Cell", DuplicateManualCell);
        ConfigureManualButton(_manualInsertBeforeButton, "Insert Before", () => InsertManualElement(false));
        ConfigureManualButton(_manualInsertAfterButton, "Insert After", () => InsertManualElement(true));
        ConfigureManualButton(_manualResetSelectedButton, "Reset Selected", ResetManualSelected);
        ConfigureManualButton(_manualSetBaselineButton, "Set Baseline", SetManualBaseline);
        ConfigureManualButton(_manualRevertBaselineButton, "Revert Baseline", RevertManualBaseline);
        ConfigureManualButton(_manualSaveButton, "Save Structure", SaveManualStructure);
        ConfigureManualButton(_manualLoadButton, "Load Structure", LoadManualStructure);
        panel.Controls.AddRange(new Control[]
        {
            _manualAddCellButton, _manualRemoveCellButton, _manualDuplicateCellButton,
            _manualInsertBeforeButton, _manualInsertAfterButton, _manualResetSelectedButton,
            _manualSetBaselineButton, _manualRevertBaselineButton, _manualSaveButton, _manualLoadButton
        });
        return panel;
    }

    private Control BuildManualPlotsPanel()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var sweepTab = new TabPage("Live S-Parameters");
        var propagationTab = new TabPage("Propagation (model-dependent)");
        sweepTab.Controls.Add(BuildManualSweepPlots());
        propagationTab.Controls.Add(BuildManualPropagationPlots());
        tabs.TabPages.Add(sweepTab);
        tabs.TabPages.Add(propagationTab);
        return tabs;
    }

    private Control BuildManualSweepPlots()
    {
        var plots = BuildManualPlotGrid(2, 3);
        ConfigurePlotView(_manualSmithPlot);
        ConfigurePlotView(_manualS11Plot);
        ConfigurePlotView(_manualS21Plot);
        ConfigurePlotView(_manualPhasePlot);
        ConfigurePlotView(_manualGroupDelayPlot);
        plots.Controls.Add(_manualSmithPlot, 0, 0);
        plots.Controls.Add(_manualS11Plot, 1, 0);
        plots.Controls.Add(_manualS21Plot, 0, 1);
        plots.Controls.Add(_manualPhasePlot, 1, 1);
        plots.Controls.Add(_manualGroupDelayPlot, 0, 2);
        plots.SetColumnSpan(_manualGroupDelayPlot, 2);
        return plots;
    }

    private Control BuildManualPropagationPlots()
    {
        var plots = BuildManualPlotGrid(2, 3);
        ConfigurePlotView(_manualNodeVoltagePlot);
        ConfigurePlotView(_manualNodePhasePlot);
        ConfigurePlotView(_manualLocalPhasePlot);
        ConfigurePlotView(_manualSlowWavePlot);
        ConfigurePlotView(_manualEnergyPlot);
        plots.Controls.Add(_manualNodeVoltagePlot, 0, 0);
        plots.Controls.Add(_manualNodePhasePlot, 1, 0);
        plots.Controls.Add(_manualLocalPhasePlot, 0, 1);
        plots.Controls.Add(_manualSlowWavePlot, 1, 1);
        plots.Controls.Add(_manualEnergyPlot, 0, 2);
        plots.SetColumnSpan(_manualEnergyPlot, 2);
        return plots;
    }

    private static TableLayoutPanel BuildManualPlotGrid(int columns, int rows)
    {
        var plots = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            RowCount = rows,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        for (var column = 0; column < columns; column++)
            plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        for (var row = 0; row < rows; row++)
            plots.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        return plots;
    }

    private static void ConfigureManualButton(Button button, string text, Action handler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Click += (_, _) => handler();
    }

    private void LoadManualStructureFromMainEditor()
    {
        try
        {
            SetManualStructure(SimulationEngine.ParseStructure(_structureInput.Text), true);
        }
        catch
        {
            SetManualStructure(new List<RfElement>
            {
                new() { Type = "SeriesL", Value = 100e-9 },
                new() { Type = "ShuntC", Value = 51e-12 }
            }, true);
        }
    }

    private void SyncManualTuningFromStructure(IReadOnlyList<RfElement> structure)
    {
        if (_manualElementsGrid.Columns.Count == 0)
            return;
        SetManualStructure(structure, true);
    }

    private void SetManualStructure(IReadOnlyList<RfElement> structure, bool setBaseline)
    {
        _manualElements = structure.Select(element => new ManualElementState
        {
            Element = new RfElement { Type = element.Type, Value = element.Value }
        }).ToList();
        if (setBaseline)
        {
            _manualBaseline = CloneManualStructure();
            _manualBaselineMetrics = null;
        }
        RefreshManualElementsGrid();
        SyncManualStructureToMainEditor();
        ScheduleManualLiveSimulation();
    }

    private void RefreshManualElementsGrid(int selectedIndex = -1)
    {
        _manualEditorUpdating = true;
        try
        {
            if (selectedIndex < 0)
                selectedIndex = GetManualSelectedIndex();
            _manualElementsGrid.Rows.Clear();
            for (var index = 0; index < _manualElements.Count; index++)
            {
                var state = _manualElements[index];
                _manualElementsGrid.Rows.Add(index, state.Element.Type, FormatManualValue(state.Element), state.Locked);
            }
            if (_manualElements.Count > 0)
            {
                selectedIndex = Math.Clamp(selectedIndex, 0, _manualElements.Count - 1);
                _manualElementsGrid.Rows[selectedIndex].Selected = true;
                _manualElementsGrid.CurrentCell = _manualElementsGrid.Rows[selectedIndex].Cells[0];
            }
        }
        finally
        {
            _manualEditorUpdating = false;
        }
        UpdateManualSelectedEditor();
    }

    private void UpdateManualSelectedEditor()
    {
        var index = GetManualSelectedIndex();
        _manualEditorUpdating = true;
        try
        {
            var enabled = index >= 0;
            _manualValueSlider.Enabled = enabled;
            _manualValueInput.Enabled = enabled;
            _manualLockInput.Enabled = enabled;
            if (!enabled)
                return;
            var state = _manualElements[index];
            _manualValueSlider.Enabled = !state.Locked;
            _manualValueInput.Enabled = !state.Locked;
            var (minimum, maximum, scale, unit) = GetManualValueRange(state.Element.Type);
            _manualValueUnitLabel.Text = unit;
            _manualValueInput.Minimum = (decimal)minimum;
            _manualValueInput.Maximum = (decimal)maximum;
            _manualValueInput.Value = ClampDecimal(state.Element.Value / scale, _manualValueInput.Minimum, _manualValueInput.Maximum);
            _manualValueSlider.Value = ValueToManualSlider(state.Element.Value / scale, minimum, maximum);
            _manualLockInput.Checked = state.Locked;
        }
        finally
        {
            _manualEditorUpdating = false;
        }
    }

    private void HandleManualGridValueChanged(int rowIndex, int columnIndex)
    {
        if (_manualEditorUpdating || rowIndex < 0 || rowIndex >= _manualElements.Count)
            return;
        var state = _manualElements[rowIndex];
        if (_manualElementsGrid.Columns[columnIndex].Name == "Type")
        {
            if (state.Locked) { RefreshManualElementsGrid(rowIndex); return; }
            state.Element.Type = _manualElementsGrid.Rows[rowIndex].Cells[columnIndex].Value?.ToString() ?? state.Element.Type;
            state.Element.Value = GetDefaultManualValue(state.Element.Type);
        }
        else if (_manualElementsGrid.Columns[columnIndex].Name == "Locked")
        {
            state.Locked = Convert.ToBoolean(_manualElementsGrid.Rows[rowIndex].Cells[columnIndex].Value);
        }
        RefreshManualElementsGrid(rowIndex);
        ManualStructureChanged();
    }

    private void HandleManualSliderChanged()
    {
        var index = GetManualSelectedIndex();
        if (_manualEditorUpdating || index < 0 || _manualElements[index].Locked)
            return;
        var (minimum, maximum, scale, _) = GetManualValueRange(_manualElements[index].Element.Type);
        var normalized = (double)_manualValueSlider.Value / _manualValueSlider.Maximum;
        _manualElements[index].Element.Value = Math.Exp(Math.Log(minimum) + normalized * (Math.Log(maximum) - Math.Log(minimum))) * scale;
        RefreshManualElementsGrid(index);
        ManualStructureChanged();
    }

    private void HandleManualNumericChanged()
    {
        var index = GetManualSelectedIndex();
        if (_manualEditorUpdating || index < 0 || _manualElements[index].Locked)
            return;
        var (_, _, scale, _) = GetManualValueRange(_manualElements[index].Element.Type);
        _manualElements[index].Element.Value = (double)_manualValueInput.Value * scale;
        RefreshManualElementsGrid(index);
        ManualStructureChanged();
    }

    private void HandleManualLockChanged()
    {
        var index = GetManualSelectedIndex();
        if (_manualEditorUpdating || index < 0)
            return;
        _manualElements[index].Locked = _manualLockInput.Checked;
        RefreshManualElementsGrid(index);
        ScheduleCurrentRefresh();
    }

    private void AddManualLcCell()
    {
        var insertIndex = Math.Max(0, GetManualSelectedIndex() + 1);
        _manualElements.Insert(insertIndex, new ManualElementState { Element = new RfElement { Type = "SeriesL", Value = 100e-9 } });
        _manualElements.Insert(insertIndex + 1, new ManualElementState { Element = new RfElement { Type = "ShuntC", Value = 51e-12 } });
        RefreshManualElementsGrid(insertIndex);
        ManualStructureChanged();
    }

    private void RemoveManualCell()
    {
        var index = GetManualSelectedIndex();
        if (index < 0 || _manualElements.Count <= 2)
        {
            _manualStatusLabel.Text = "Keep at least one LC cell in the structure.";
            return;
        }
        var start = index;
        if (_manualElements[index].Element.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase) && index > 0)
            start = index - 1;
        var count = Math.Min(2, _manualElements.Count - start);
        _manualElements.RemoveRange(start, count);
        RefreshManualElementsGrid(Math.Min(start, _manualElements.Count - 1));
        ManualStructureChanged();
    }

    private void DuplicateManualCell()
    {
        var index = GetManualSelectedIndex();
        if (index < 0)
            return;
        var start = _manualElements[index].Element.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase) && index > 0 ? index - 1 : index;
        var count = Math.Min(2, _manualElements.Count - start);
        var copies = _manualElements.Skip(start).Take(count).Select(CloneManualState).ToList();
        _manualElements.InsertRange(start + count, copies);
        RefreshManualElementsGrid(start + count);
        ManualStructureChanged();
    }

    private void InsertManualElement(bool after)
    {
        var selected = GetManualSelectedIndex();
        var insertIndex = selected < 0 ? _manualElements.Count : selected + (after ? 1 : 0);
        _manualElements.Insert(insertIndex, new ManualElementState { Element = new RfElement { Type = "SeriesL", Value = 100e-9 } });
        RefreshManualElementsGrid(insertIndex);
        ManualStructureChanged();
    }

    private void ResetManualSelected()
    {
        var index = GetManualSelectedIndex();
        if (index < 0 || _manualElements[index].Locked)
            return;
        var baseline = index < _manualBaseline.Count ? _manualBaseline[index] : null;
        _manualElements[index].Element.Value = baseline is not null
            && baseline.Type.Equals(_manualElements[index].Element.Type, StringComparison.OrdinalIgnoreCase)
            ? baseline.Value
            : GetDefaultManualValue(_manualElements[index].Element.Type);
        RefreshManualElementsGrid(index);
        ManualStructureChanged();
    }

    private void SetManualBaseline()
    {
        _manualBaseline = CloneManualStructure();
        _manualBaselineMetrics = CalculateManualTargetMetrics(_manualBaseline);
        _manualStatusLabel.Text = "Baseline updated.";
        ScheduleManualLiveSimulation();
    }

    private void RevertManualBaseline()
    {
        if (_manualBaseline.Count == 0)
            return;
        SetManualStructure(_manualBaseline, false);
        _manualStatusLabel.Text = "Reverted to baseline.";
    }

    private void SaveManualStructure()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = "manual-tuned-structure.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(CurrentExportConfiguration(), new JsonSerializerOptions { WriteIndented = true }));
        _manualStatusLabel.Text = $"Saved: {Path.GetFileName(dialog.FileName)}";
    }

    private void LoadManualStructure()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try { LoadSharedProject(File.ReadAllText(dialog.FileName)); }
        catch (Exception ex) { _manualStatusLabel.Text = "Load error: " + ex.Message; }
    }

    private void ManualStructureChanged()
    {
        SyncManualStructureToMainEditor();
        ScheduleManualLiveSimulation();
    }

    private void SyncManualStructureToMainEditor()
    {
        var structure = CloneManualStructure();
        _structureInput.Text = JsonSerializer.Serialize(structure, new JsonSerializerOptions { WriteIndented = true });
        UpdateStructureSummary(structure);
    }

    private void ScheduleManualLiveSimulation()
    {
        ScheduleCurrentRefresh();
    }

    private void RunManualLiveSimulation()
    {
        try
        {
            var structure = _workspace.Current!.Structure;
            var sweep = _lastSweep;
            var targetMHz = _resolvedMarkerMHz;
            var metrics = CalculateManualTargetMetrics(structure, sweep);
            var baselineSweep = _manualBaseline.Count > 0
                ? SimulationEngine.Simulate(
                    (double)_startMHzInput.Value * 1e6,
                    (double)_stopMHzInput.Value * 1e6,
                    (int)_pointsInput.Value,
                    (double)_z0Input.Value,
                    _manualBaseline)
                : sweep;
            _manualBaselineMetrics = _manualBaseline.Count > 0
                ? CalculateManualTargetMetrics(_manualBaseline, baselineSweep)
                : metrics;
            _manualMetricsText.Text = $"Current r{_workspace.Revision} at {targetMHz:F6} MHz (nearest point)\r\n" + FormatManualMetrics(metrics, _manualBaselineMetrics);
            _manualSmithPlot.Model = AddManualSmithBaseline(
                AddManualTargetMarker(PlotFactory.BuildSmithModel(sweep), sweep, targetMHz, false),
                baselineSweep);
            _manualS11Plot.Model = AddManualFrequencyBaseline(
                AddManualTargetMarker(PlotFactory.BuildS11DbModel(sweep), sweep, targetMHz, true),
                baselineSweep,
                point => RfMath.ToDbMagnitude(point.S11));
            _manualS21Plot.Model = AddManualFrequencyBaseline(
                AddManualTargetMarker(PlotFactory.BuildS21DbModel(sweep), sweep, targetMHz, true),
                baselineSweep,
                point => RfMath.ToDbMagnitude(point.S21));
            _manualPhasePlot.Model = AddManualFrequencyBaseline(
                AddManualTargetMarker(PlotFactory.BuildS21PhaseModel(sweep), sweep, targetMHz, true),
                baselineSweep,
                BuildManualPhaseDegrees(baselineSweep));
            _manualGroupDelayPlot.Model = AddManualFrequencyBaseline(
                AddManualTargetMarker(PlotFactory.BuildGroupDelayModel(sweep), sweep, targetMHz, true),
                baselineSweep,
                RfMath.CalculateGroupDelayNs(baselineSweep));
            TryUpdateManualPropagationPlots(metrics, _manualBaselineMetrics);
            InvalidateManualPlots();
            _manualStatusLabel.Text = $"Live update: {structure.Count} elements at {targetMHz:F3} MHz";
        }
        catch (Exception ex)
        {
            _manualStatusLabel.Text = $"Live update error: {ex.Message}";
        }
    }

    private ManualTargetMetrics CalculateManualTargetMetrics(IReadOnlyList<RfElement> structure, IReadOnlyList<SweepPoint>? existingSweep = null)
    {
        var sweep = existingSweep ?? SimulationEngine.Simulate(
            (double)_startMHzInput.Value * 1e6,
            (double)_stopMHzInput.Value * 1e6,
            (int)_pointsInput.Value,
            (double)_z0Input.Value,
            structure);
        var targetHz = _resolvedMarkerMHz * 1e6;
        var targetIndex = FindManualNearestSweepIndex(sweep, targetHz);
        var point = sweep[targetIndex];
        var phase = RfMath.GetUnwrappedS21PhaseRadians(sweep);
        var delay = RfMath.CalculateGroupDelayNs(sweep);
        PropagationResult? propagation = null;
        try
        {
            propagation = PropagationAnalysis.Analyze(
                structure,
                point.FrequencyHz,
                (double)_z0Input.Value,
                (double)_manualSpacingMmInput.Value * 1e-3,
                sweep);
        }
        catch
        {
            // Spatial metrics are intentionally unavailable for non-LC ladders.
        }
        var energies = propagation is null
            ? new List<double>()
            : propagation.Cells.Select((cell, index) =>
                cell.InductorEnergyJoules + (index < propagation.Capacitors.Count
                    ? propagation.Capacitors[index].CapacitorEnergyJoules
                    : 0)).ToList();
        var totalEnergy = energies.Sum();
        return new ManualTargetMetrics
        {
            TargetS11Db = RfMath.ToDbMagnitude(point.S11),
            TargetS21Db = RfMath.ToDbMagnitude(point.S21),
            PhaseDeg = phase[targetIndex] * 180.0 / Math.PI,
            GroupDelayNs = delay[targetIndex],
            InputImpedanceOhms = (double)_z0Input.Value * (Complex.One + point.S11) / (Complex.One - point.S11),
            OutputLoadPowerWatts = propagation?.Diagnostics.PowerDeliveredToLoadWatts,
            MaxSlowWaveFactor = propagation?.Cells.Where(cell => double.IsFinite(cell.SlowWaveFactor))
                .Select(cell => cell.SlowWaveFactor).DefaultIfEmpty(0).Max(),
            EnergyConcentrationRatio = totalEnergy > 0 ? energies.Max() / totalEnergy : null,
            Propagation = propagation
        };
    }

    private void TryUpdateManualPropagationPlots(
        ManualTargetMetrics metrics,
        ManualTargetMetrics? baselineMetrics)
    {
        var propagation = metrics.Propagation;
        if (propagation is null)
        {
            _manualNodeVoltagePlot.Model = null;
            _manualNodePhasePlot.Model = null;
            _manualLocalPhasePlot.Model = null;
            _manualSlowWavePlot.Model = null;
            _manualEnergyPlot.Model = null;
            return;
        }
        _manualNodeVoltagePlot.Model = PlotFactory.BuildNodeVoltageMagnitudeModel(propagation);
        _manualNodePhasePlot.Model = PlotFactory.BuildNodePhaseModel(propagation);
        _manualLocalPhasePlot.Model = PlotFactory.BuildLocalPhaseShiftModel(propagation);
        _manualSlowWavePlot.Model = PlotFactory.BuildSlowWaveFactorModel(propagation);
        _manualEnergyPlot.Model = PlotFactory.BuildStoredEnergyModel(propagation);
        if (baselineMetrics?.Propagation is { } baseline)
        {
            AddManualIndexBaseline(
                _manualNodeVoltagePlot.Model,
                baseline.Nodes.Select(node => new DataPoint(node.Index, node.VoltageMagnitude)));
            AddManualIndexBaseline(
                _manualNodePhasePlot.Model,
                baseline.Nodes.Select(node => new DataPoint(node.Index, node.PhaseDeg)));
            AddManualIndexBaseline(
                _manualLocalPhasePlot.Model,
                baseline.Cells.Select(cell => new DataPoint(cell.Index, cell.DeltaPhaseDeg)));
            AddManualIndexBaseline(
                _manualSlowWavePlot.Model,
                baseline.Cells.Where(cell => double.IsFinite(cell.SlowWaveFactor))
                    .Select(cell => new DataPoint(cell.Index, cell.SlowWaveFactor)));
            AddManualEnergyBaseline(_manualEnergyPlot.Model, baseline);
        }
    }

    private static PlotModel AddManualSmithBaseline(PlotModel model, IReadOnlyList<SweepPoint> baselineSweep)
    {
        model.Series.Add(BuildManualBaselineSeries(
            baselineSweep.Select(point => new DataPoint(point.S11.Real, point.S11.Imaginary))));
        model.IsLegendVisible = true;
        return model;
    }

    private static PlotModel AddManualFrequencyBaseline(
        PlotModel model,
        IReadOnlyList<SweepPoint> baselineSweep,
        Func<SweepPoint, double> selector)
    {
        model.Series.Add(BuildManualBaselineSeries(
            baselineSweep.Select(point => new DataPoint(point.FrequencyHz / 1e6, selector(point)))));
        IncludeBaselineAxisRange(model);
        model.IsLegendVisible = true;
        return model;
    }

    private static PlotModel AddManualFrequencyBaseline(
        PlotModel model,
        IReadOnlyList<SweepPoint> baselineSweep,
        IReadOnlyList<double> values)
    {
        model.Series.Add(BuildManualBaselineSeries(
            baselineSweep.Select((point, index) => new DataPoint(point.FrequencyHz / 1e6, values[index]))));
        IncludeBaselineAxisRange(model);
        model.IsLegendVisible = true;
        return model;
    }

    private static void AddManualIndexBaseline(PlotModel model, IEnumerable<DataPoint> points)
    {
        model.Series.Add(BuildManualBaselineSeries(points));
        IncludeBaselineAxisRange(model);
        model.IsLegendVisible = true;
    }

    private static void IncludeBaselineAxisRange(PlotModel model)
    {
        var axis = model.Axes.FirstOrDefault(a => a.Position == OxyPlot.Axes.AxisPosition.Left);
        if (axis is null) return;
        // OxyPlot will fit all series, including a baseline outside the candidate's range.
        axis.Minimum = axis.Maximum = double.NaN;
        axis.MajorStep = axis.MinorStep = double.NaN;
    }

    private static void AddManualEnergyBaseline(PlotModel model, PropagationResult baseline)
    {
        model.Series.Add(BuildManualBaselineSeries(
            baseline.Cells.Select(cell => new DataPoint(cell.Index, cell.InductorEnergyJoules)),
            "Baseline L energy"));
        model.Series.Add(BuildManualBaselineSeries(
            baseline.Capacitors.Select(capacitor => new DataPoint(capacitor.Index, capacitor.CapacitorEnergyJoules)),
            "Baseline C energy"));
        model.IsLegendVisible = true;
    }

    private static LineSeries BuildManualBaselineSeries(IEnumerable<DataPoint> points, string title = "Baseline")
    {
        var series = new LineSeries
        {
            Title = title,
            Color = OxyColors.DodgerBlue,
            LineStyle = LineStyle.Dash,
            StrokeThickness = 1.4
        };
        foreach (var point in points)
            series.Points.Add(point);
        return series;
    }

    private static IReadOnlyList<double> BuildManualPhaseDegrees(IReadOnlyList<SweepPoint> sweep) =>
        RfMath.GetUnwrappedS21PhaseRadians(sweep).Select(value => value * 180.0 / Math.PI).ToList();

    private void InvalidateManualPlots()
    {
        foreach (var plot in new[]
                 {
                     _manualSmithPlot, _manualS11Plot, _manualS21Plot, _manualPhasePlot, _manualGroupDelayPlot,
                     _manualNodeVoltagePlot, _manualNodePhasePlot, _manualLocalPhasePlot, _manualSlowWavePlot,
                     _manualEnergyPlot
                 })
        {
            plot.InvalidatePlot(true);
        }
    }

    private static PlotModel AddManualTargetMarker(PlotModel model, IReadOnlyList<SweepPoint> sweep, double targetMHz, bool frequencyAxis)
    {
        if (frequencyAxis)
        {
            model.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = targetMHz,
                Color = OxyColors.IndianRed,
                StrokeThickness = 1.4
            });
            return model;
        }
        if (sweep.Count == 0)
            return model;
        var targetIndex = FindManualNearestSweepIndex(sweep, targetMHz * 1e6);
        var point = sweep[targetIndex];
        var marker = new ScatterSeries { MarkerType = MarkerType.Circle, MarkerFill = OxyColors.IndianRed, MarkerSize = 4 };
        marker.Points.Add(new ScatterPoint(point.S11.Real, point.S11.Imaginary));
        model.Series.Add(marker);
        return model;
    }

    private static string FormatManualMetrics(ManualTargetMetrics current, ManualTargetMetrics? baseline)
    {
        var impedance = current.InputImpedanceOhms.HasValue
            ? $"{current.InputImpedanceOhms.Value.Real:F2} {(current.InputImpedanceOhms.Value.Imaginary < 0 ? "-" : "+")} j{Math.Abs(current.InputImpedanceOhms.Value.Imaginary):F2} ohm"
            : "n/a";
        return $"Marker S11: {current.TargetS11Db:F2} dB{FormatManualDelta(current.TargetS11Db, baseline?.TargetS11Db)}\r\n"
               + $"Marker S21: {current.TargetS21Db:F2} dB{FormatManualDelta(current.TargetS21Db, baseline?.TargetS21Db)}\r\n"
               + $"Phase: {current.PhaseDeg:F2} deg{FormatManualDelta(current.PhaseDeg, baseline?.PhaseDeg)}\r\n"
               + $"Group delay: {current.GroupDelayNs:F2} ns{FormatManualDelta(current.GroupDelayNs, baseline?.GroupDelayNs)}\r\n"
               + $"Input impedance: {impedance}\r\n"
               + $"Output/load power: {FormatManualOptional(current.OutputLoadPowerWatts, "F6")} W\r\n"
               + $"Model-dependent max SWF: {FormatManualOptional(current.MaxSlowWaveFactor, "F2")}\r\n"
               + $"Model-dependent energy concentration: {FormatManualOptional(current.EnergyConcentrationRatio, "P2")}\r\n"
               + (current.Propagation is null ? "Propagation: unavailable for this element topology" : $"Sections: {current.Propagation.Classification.Interpretation}");
    }

    private static string FormatManualDelta(double current, double? baseline) =>
        baseline.HasValue ? $"  (delta {current - baseline.Value:+0.00;-0.00;0.00})" : string.Empty;

    private static string FormatManualOptional(double? value, string format) =>
        value.HasValue ? value.Value.ToString(format) : "n/a";

    private List<RfElement> CloneManualStructure() =>
        _manualElements.Select(state => new RfElement { Type = state.Element.Type, Value = state.Element.Value }).ToList();

    private static ManualElementState CloneManualState(ManualElementState state) =>
        new()
        {
            Element = new RfElement { Type = state.Element.Type, Value = state.Element.Value },
            Locked = state.Locked
        };

    private int GetManualSelectedIndex() =>
        _manualElementsGrid.SelectedRows.Count > 0 ? _manualElementsGrid.SelectedRows[0].Index : -1;

    private static int FindManualNearestSweepIndex(IReadOnlyList<SweepPoint> sweep, double frequencyHz) =>
        Enumerable.Range(0, sweep.Count).OrderBy(index => Math.Abs(sweep[index].FrequencyHz - frequencyHz)).First();

    private static (double Minimum, double Maximum, double Scale, string Unit) GetManualValueRange(string type)
    {
        if (type.EndsWith("L", StringComparison.OrdinalIgnoreCase))
            return (1, 5000, 1e-9, "nH");
        if (type.EndsWith("C", StringComparison.OrdinalIgnoreCase))
            return (0.1, 1000, 1e-12, "pF");
        return (0.01, 10000, 1, "ohm");
    }

    private static double GetDefaultManualValue(string type)
    {
        var (_, _, scale, _) = GetManualValueRange(type);
        if (type.EndsWith("L", StringComparison.OrdinalIgnoreCase))
            return 100 * scale;
        if (type.EndsWith("C", StringComparison.OrdinalIgnoreCase))
            return 51 * scale;
        return 50 * scale;
    }

    private static int ValueToManualSlider(double value, double minimum, double maximum)
    {
        var normalized = (Math.Log(Math.Max(value, minimum)) - Math.Log(minimum))
                         / (Math.Log(maximum) - Math.Log(minimum));
        return Math.Clamp((int)Math.Round(normalized * 1000), 0, 1000);
    }

    private static decimal ClampDecimal(double value, decimal minimum, decimal maximum) =>
        Math.Clamp((decimal)value, minimum, maximum);

    private static string FormatManualValue(RfElement element)
    {
        var (_, _, scale, unit) = GetManualValueRange(element.Type);
        return $"{element.Value / scale:G6} {unit}";
    }

    private sealed class ManualElementState
    {
        public RfElement Element { get; set; } = new();
        public bool Locked { get; set; }
    }

    private sealed class ManualTargetMetrics
    {
        public double TargetS11Db { get; set; }
        public double TargetS21Db { get; set; }
        public double PhaseDeg { get; set; }
        public double GroupDelayNs { get; set; }
        public Complex? InputImpedanceOhms { get; set; }
        public double? OutputLoadPowerWatts { get; set; }
        public double? MaxSlowWaveFactor { get; set; }
        public double? EnergyConcentrationRatio { get; set; }
        public PropagationResult? Propagation { get; set; }
    }
}
