using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RfSimulator;

namespace RfSimulator.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(CliOptions.HelpText);
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(options.RunTestsDirectory))
            {
                return RunRegressionTests(options.RunTestsDirectory);
            }

            var result = Run(options);
            WriteJson(result);
            return result.PassFailFlags.GetValueOrDefault("overall") ? 0 : 2;
        }
        catch (Exception ex)
        {
            WriteJson(new CliError { Error = ex.Message });
            return 1;
        }
    }

    private static CliMetrics Run(CliOptions options)
    {
        var structure = !string.IsNullOrWhiteSpace(options.InputPath)
            ? LoadStructure(options.InputPath)
            : CreateDefaultStructure(options.ElementCount);

        return options.Mode switch
        {
            "antenna" => Analyze(structure, options, SmallAntennaObjectiveMode.AntennaMatch),
            "compression" => Analyze(structure, options, SmallAntennaObjectiveMode.SmoothCompression),
            "optimize-antenna" => Optimize(structure, options, SmallAntennaObjectiveMode.AntennaMatch),
            "optimize-compression" => Optimize(structure, options, SmallAntennaObjectiveMode.SmoothCompression),
            _ => throw new InvalidOperationException($"Unsupported mode '{options.Mode}'.")
        };
    }

    private static CliMetrics Analyze(
        IReadOnlyList<RfElement> structure,
        CliOptions options,
        SmallAntennaObjectiveMode objectiveMode)
    {
        var sweep = SmallAntennaMode.Simulate(
            options.FrequencyStartMHz * 1e6,
            options.FrequencyStopMHz * 1e6,
            options.Points,
            structure,
            options.AntennaConfig);
        return BuildMetrics(structure, sweep, options, objectiveMode);
    }

    private static CliMetrics Optimize(
        IReadOnlyList<RfElement> structure,
        CliOptions options,
        SmallAntennaObjectiveMode objectiveMode)
    {
        var optimizerConfig = options.CreateOptimizerConfig(objectiveMode);
        var candidate = SmallAntennaMode.Optimize(structure, optimizerConfig);
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var json = JsonSerializer.Serialize(candidate.Structure, JsonOptions);
            File.WriteAllText(options.OutputPath, json);
        }

        var metrics = BuildMetrics(candidate.Structure, candidate.Sweep, options, objectiveMode);
        metrics.OptimizerScore = candidate.Score;
        metrics.ScoreBreakdown = candidate.ScoreBreakdown;
        metrics.OutputPath = options.OutputPath;
        metrics.Structure = candidate.Structure;
        return metrics;
    }

    private static CliMetrics BuildMetrics(
        IReadOnlyList<RfElement> structure,
        IReadOnlyList<SmallAntennaPoint> sweep,
        CliOptions options,
        SmallAntennaObjectiveMode objectiveMode)
    {
        var bestMatch = sweep.OrderBy(point => point.InputReturnLossDb).First();
        var strongestRadiation = sweep.OrderByDescending(point => point.RadiatedPowerWatts).First();
        var selectedFrequencyHz = options.FrequencyMHz.HasValue
            ? options.FrequencyMHz.Value * 1e6
            : strongestRadiation.FrequencyHz;
        var selectedIndex = FindNearestIndex(sweep, selectedFrequencyHz);
        var groupDelayNs = CalculateAntennaGroupDelayNs(sweep, selectedIndex);

        PropagationResult? propagation = null;
        var propagationSupported = structure.Count > 0;
        if (propagationSupported)
        {
            try
            {
                propagation = PropagationAnalysis.AnalyzeTerminated(
                    structure,
                    selectedFrequencyHz,
                    SmallAntennaMode.SourceResistanceOhms,
                    options.AntennaConfig.AntennaImpedance,
                    options.SpacingMm * 1e-3,
                    Array.Empty<SweepPoint>());
            }
            catch
            {
                propagationSupported = false;
            }
        }

        var slowWave = propagation?.Cells
            .Where(cell => double.IsFinite(cell.SlowWaveFactor) && cell.SlowWaveFactor > 0)
            .ToList() ?? new List<PropagationCell>();
        var slowWavePeak = slowWave.OrderByDescending(cell => cell.SlowWaveFactor).FirstOrDefault();
        var energies = propagation is null ? new List<double>() : BuildEnergyProfile(propagation);
        var totalEnergy = energies.Sum();
        var physicalLengthMeters = propagation?.Summary.PhysicalLengthMeters ?? 0;
        var compressionRatio = groupDelayNs > 0 && physicalLengthMeters > 0
            ? 299_792_458.0 * groupDelayNs * 1e-9 / physicalLengthMeters
            : 0;

        var score = SmallAntennaMode.CalculateScoreBreakdown(
            sweep,
            structure,
            options.CreateOptimizerConfig(objectiveMode));
        var metrics = new CliMetrics
        {
            Mode = objectiveMode == SmallAntennaObjectiveMode.AntennaMatch ? "antenna" : "compression",
            BestReturnLossDb = bestMatch.InputReturnLossDb,
            FrequencyOfBestMatchMHz = bestMatch.FrequencyHz / 1e6,
            DeliveredPowerDb = strongestRadiation.DeliveredPowerDb,
            RadiatedPowerDb = strongestRadiation.RadiatedPowerDb,
            RadiationEfficiencyPercent = strongestRadiation.RadiationEfficiency * 100.0,
            TotalPhaseShiftDeg = propagation?.Summary.TotalPhaseShiftDeg ?? 0,
            TotalGroupDelayNs = groupDelayNs,
            AverageSlowWaveFactor = slowWave.Count > 0 ? slowWave.Average(cell => cell.SlowWaveFactor) : 0,
            MaxSlowWaveFactor = slowWave.Count > 0 ? slowWave.Max(cell => cell.SlowWaveFactor) : 0,
            SlowWaveFactorPeakCell = slowWave.Count > 0 ? slowWavePeak.Index : -1,
            EnergyConcentrationRatio = totalEnergy > 0 ? energies.Max() / totalEnergy : 0,
            PhaseSmoothnessError = propagation is null ? 0 : CalculatePhaseSmoothnessError(propagation),
            LcSmoothnessError = CalculateLcSmoothnessError(structure),
            CompressionRatio = compressionRatio,
            OptimizerScore = score.TotalScore,
            ScoreBreakdown = score,
            Structure = structure.ToList()
        };

        metrics.PassFailFlags["finite_metrics"] = metrics.AllFinite();
        metrics.PassFailFlags["target_return_loss"] = metrics.BestReturnLossDb <= options.TargetReturnLossDb;
        metrics.PassFailFlags["propagation_supported"] = propagationSupported;
        metrics.PassFailFlags["slow_wave_bounded"] = metrics.MaxSlowWaveFactor <= options.MaxSlowWaveFactor;
        metrics.PassFailFlags["energy_distributed"] = metrics.EnergyConcentrationRatio <= options.MaxEnergyConcentrationRatio;
        metrics.PassFailFlags["overall"] = objectiveMode == SmallAntennaObjectiveMode.AntennaMatch
            ? metrics.PassFailFlags["finite_metrics"] && metrics.PassFailFlags["target_return_loss"]
            : metrics.PassFailFlags["finite_metrics"]
              && metrics.PassFailFlags["propagation_supported"]
              && metrics.PassFailFlags["slow_wave_bounded"]
              && metrics.PassFailFlags["energy_distributed"];
        return metrics;
    }

    private static int RunRegressionTests(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Regression-test directory not found: {directory}");
        }

        var results = new List<RegressionResult>();
        foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(path);
            if (!LooksLikeRegressionCase(json))
            {
                continue;
            }

            var testCase = JsonSerializer.Deserialize<RegressionCase>(json, JsonOptions);
            if (testCase?.Structure is null || string.IsNullOrWhiteSpace(testCase.Mode))
            {
                continue;
            }

            var options = CliOptions.FromRegressionCase(testCase);
            var metrics = RunWithStructure(testCase.Structure, options);
            var failures = ValidateExpectedRanges(metrics, testCase.ExpectedMetricRanges);
            if (testCase.ExpectOverallPass.HasValue
                && metrics.PassFailFlags.GetValueOrDefault("overall") != testCase.ExpectOverallPass.Value)
            {
                failures.Add(
                    $"overall expected {testCase.ExpectOverallPass.Value} but was {metrics.PassFailFlags.GetValueOrDefault("overall")}");
            }

            results.Add(new RegressionResult
            {
                Name = testCase.Name ?? Path.GetFileNameWithoutExtension(path),
                File = Path.GetRelativePath(directory, path),
                AnalysisPassed = metrics.PassFailFlags.GetValueOrDefault("overall"),
                AnalysisStatus = metrics.PassFailFlags.GetValueOrDefault("overall") ? "PASS" : "FAIL",
                ExpectedOverallPass = testCase.ExpectOverallPass,
                Passed = failures.Count == 0,
                Failures = failures,
                Metrics = metrics
            });
        }

        var output = new RegressionSuiteResult
        {
            TestDirectory = directory,
            Passed = results.Count > 0 && results.All(result => result.Passed),
            Total = results.Count,
            PassedCount = results.Count(result => result.Passed),
            FailedCount = results.Count(result => !result.Passed),
            SummaryLines = results
                .Select(result => $"{Path.GetFileNameWithoutExtension(result.File)}: {result.AnalysisStatus}")
                .ToList(),
            Results = results
        };
        WriteJson(output);
        return output.Passed ? 0 : 3;
    }

    private static bool LooksLikeRegressionCase(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var propertyNames = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return propertyNames.Contains("structure")
               && propertyNames.Contains("mode")
               && propertyNames.Contains("expected_metric_ranges");
    }

    private static CliMetrics RunWithStructure(IReadOnlyList<RfElement> structure, CliOptions options)
    {
        return options.Mode switch
        {
            "antenna" => Analyze(structure, options, SmallAntennaObjectiveMode.AntennaMatch),
            "compression" => Analyze(structure, options, SmallAntennaObjectiveMode.SmoothCompression),
            _ => throw new InvalidOperationException($"Regression mode '{options.Mode}' is not supported.")
        };
    }

    private static List<string> ValidateExpectedRanges(
        CliMetrics metrics,
        Dictionary<string, MetricRange>? ranges)
    {
        var failures = new List<string>();
        if (ranges is null)
        {
            return failures;
        }

        var values = metrics.ToMetricDictionary();
        foreach (var pair in ranges.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!values.TryGetValue(pair.Key, out var actual))
            {
                failures.Add($"unknown metric '{pair.Key}'");
                continue;
            }

            if (pair.Value.Min.HasValue && actual < pair.Value.Min.Value)
            {
                failures.Add($"{pair.Key} {actual:G17} is below min {pair.Value.Min.Value:G17}");
            }
            if (pair.Value.Max.HasValue && actual > pair.Value.Max.Value)
            {
                failures.Add($"{pair.Key} {actual:G17} is above max {pair.Value.Max.Value:G17}");
            }
        }

        return failures;
    }

    private static List<RfElement> LoadStructure(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.Deserialize<List<RfElement>>(JsonOptions) ?? new List<RfElement>();
        }

        if (root.TryGetProperty("structure", out var structure)
            || root.TryGetProperty("Structure", out structure))
        {
            return structure.Deserialize<List<RfElement>>(JsonOptions) ?? new List<RfElement>();
        }

        throw new InvalidOperationException($"Input JSON does not contain a structure array: {path}");
    }

    private static List<RfElement> CreateDefaultStructure(int elementCount)
    {
        var inductors = Math.Max(2, (elementCount + 1) / 2);
        var structure = new List<RfElement>();
        for (var index = 0; index < inductors; index++)
        {
            var fraction = inductors == 1 ? 0 : (double)index / (inductors - 1);
            structure.Add(new RfElement { Type = "SeriesL", Value = (80.0 + 320.0 * fraction) * 1e-9 });
            if (index < inductors - 1)
            {
                structure.Add(new RfElement { Type = "ShuntC", Value = (20.0 + 100.0 * fraction) * 1e-12 });
            }
        }

        return structure;
    }

    private static int FindNearestIndex(IReadOnlyList<SmallAntennaPoint> sweep, double frequencyHz)
    {
        var nearestIndex = 0;
        var nearestDelta = double.PositiveInfinity;
        for (var index = 0; index < sweep.Count; index++)
        {
            var delta = Math.Abs(sweep[index].FrequencyHz - frequencyHz);
            if (delta < nearestDelta)
            {
                nearestIndex = index;
                nearestDelta = delta;
            }
        }

        return nearestIndex;
    }

    private static double CalculateAntennaGroupDelayNs(IReadOnlyList<SmallAntennaPoint> sweep, int index)
    {
        if (sweep.Count < 2)
        {
            return 0;
        }

        var phase = RfMath.UnwrapPhase(
            sweep.Select(point => Math.Atan2(point.AntennaVoltage.Imaginary, point.AntennaVoltage.Real)).ToList());
        var left = index == 0 ? 0 : index - 1;
        var right = index == sweep.Count - 1 ? sweep.Count - 1 : index + 1;
        var deltaOmega = 2.0 * Math.PI * (sweep[right].FrequencyHz - sweep[left].FrequencyHz);
        return deltaOmega > 0 ? -(phase[right] - phase[left]) / deltaOmega * 1e9 : 0;
    }

    private static List<double> BuildEnergyProfile(PropagationResult propagation)
    {
        var energies = new List<double>();
        for (var index = 0; index < propagation.Cells.Count; index++)
        {
            var energy = propagation.Cells[index].InductorEnergyJoules;
            if (index < propagation.Capacitors.Count)
            {
                energy += propagation.Capacitors[index].CapacitorEnergyJoules;
            }
            energies.Add(Math.Max(energy, 1e-30));
        }
        return energies;
    }

    private static double CalculatePhaseSmoothnessError(PropagationResult propagation)
    {
        var phase = propagation.Cells.Select(cell => Math.Max(1e-30, Math.Abs(cell.DeltaPhaseRad))).ToList();
        return RelativeSmoothness(phase);
    }

    private static double CalculateLcSmoothnessError(IReadOnlyList<RfElement> structure)
    {
        return RelativeSmoothness(structure.Where(element => element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase))
                   .Select(element => element.Value).ToList())
               + RelativeSmoothness(structure.Where(element => element.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase))
                   .Select(element => element.Value).ToList());
    }

    private static double RelativeSmoothness(IReadOnlyList<double> values)
    {
        var error = 0.0;
        for (var index = 0; index < values.Count - 1; index++)
        {
            error += Math.Pow(Math.Log(Math.Max(values[index + 1], 1e-30) / Math.Max(values[index], 1e-30)), 2);
        }
        return error;
    }

    private static void WriteJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }
}

internal sealed class CliOptions
{
    public string Mode { get; private set; } = "antenna";
    public string? InputPath { get; private set; }
    public string? OutputPath { get; private set; }
    public string? RunTestsDirectory { get; private set; }
    public bool ShowHelp { get; private set; }
    public double FrequencyStartMHz { get; private set; } = 5;
    public double FrequencyStopMHz { get; private set; } = 50;
    public double? FrequencyMHz { get; private set; }
    public int Points { get; private set; } = 401;
    public double SpacingMm { get; private set; } = 5;
    public int Iterations { get; private set; } = 100;
    public int ElementCount { get; private set; } = 9;
    public double TargetReturnLossDb { get; private set; } = -10;
    public double MaxVoltage { get; private set; } = 500;
    public double MaxSlowWaveFactor { get; private set; } = 100;
    public double MaxEnergyConcentrationRatio { get; private set; } = 0.5;
    public bool UseContinuousValues { get; private set; }
    public bool UseSmoothGradient { get; private set; }
    public double LMinNh { get; private set; } = 10;
    public double LMaxNh { get; private set; } = 2000;
    public double CMinPf { get; private set; } = 0.5;
    public double CMaxPf { get; private set; } = 470;
    public SmallAntennaConfig AntennaConfig { get; private set; } = new();

    public static string HelpText =>
        """
        rf_sim_cli --input structure.json --mode antenna --freq-start 5 --freq-stop 50 --points 401
        rf_sim_cli --input structure.json --mode compression --freq 27.5 --spacing-mm 5
        rf_sim_cli --mode optimize-antenna --iters 1000 --output best_antenna.json
        rf_sim_cli --mode optimize-compression --iters 1000 --output best_medium.json
        rf_sim_cli --run-tests tests/cli

        Optional antenna arguments:
          --rrad 2 --rloss 1 --xant -300 --target-rl -10 --max-voltage 500
          --continuous --smooth-gradient --l-min-nh 10 --l-max-nh 2000 --c-min-pf 0.5 --c-max-pf 470
          --elements 9 --max-slow-wave 100 --max-energy-concentration 0.5
        """;

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected argument '{arg}'.");
            }

            if (arg is "--help" or "-h" or "--continuous" or "--smooth-gradient")
            {
                values[arg] = null;
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new InvalidOperationException($"Missing value for '{arg}'.");
            }
            values[arg] = args[++index];
        }

        var options = new CliOptions
        {
            ShowHelp = values.ContainsKey("--help") || values.ContainsKey("-h"),
            Mode = Get(values, "--mode", "antenna"),
            InputPath = GetNullable(values, "--input"),
            OutputPath = GetNullable(values, "--output"),
            RunTestsDirectory = GetNullable(values, "--run-tests"),
            FrequencyStartMHz = GetDouble(values, "--freq-start", 5),
            FrequencyStopMHz = GetDouble(values, "--freq-stop", 50),
            FrequencyMHz = GetNullableDouble(values, "--freq"),
            Points = GetInt(values, "--points", 401),
            SpacingMm = GetDouble(values, "--spacing-mm", 5),
            Iterations = GetInt(values, "--iters", 100),
            ElementCount = GetInt(values, "--elements", 9),
            TargetReturnLossDb = GetDouble(values, "--target-rl", -10),
            MaxVoltage = GetDouble(values, "--max-voltage", 500),
            MaxSlowWaveFactor = GetDouble(values, "--max-slow-wave", 100),
            MaxEnergyConcentrationRatio = GetDouble(values, "--max-energy-concentration", 0.5),
            UseContinuousValues = values.ContainsKey("--continuous"),
            UseSmoothGradient = values.ContainsKey("--smooth-gradient"),
            LMinNh = GetDouble(values, "--l-min-nh", 10),
            LMaxNh = GetDouble(values, "--l-max-nh", 2000),
            CMinPf = GetDouble(values, "--c-min-pf", 0.5),
            CMaxPf = GetDouble(values, "--c-max-pf", 470),
            AntennaConfig = new SmallAntennaConfig
            {
                RadiationResistanceOhms = GetDouble(values, "--rrad", 2),
                LossResistanceOhms = GetDouble(values, "--rloss", 1),
                ReactanceOhms = GetDouble(values, "--xant", -300)
            }
        };
        return options;
    }

    public static CliOptions FromRegressionCase(RegressionCase testCase)
    {
        var options = new CliOptions
        {
            Mode = testCase.Mode,
            FrequencyStartMHz = testCase.FrequencyStartMHz ?? 5,
            FrequencyStopMHz = testCase.FrequencyStopMHz ?? 50,
            FrequencyMHz = testCase.FrequencyMHz,
            Points = testCase.Points ?? 401,
            SpacingMm = testCase.SpacingMm ?? 5,
            TargetReturnLossDb = testCase.TargetReturnLossDb ?? -10,
            MaxVoltage = testCase.MaxVoltage ?? 500,
            MaxSlowWaveFactor = testCase.MaxSlowWaveFactor ?? 100,
            MaxEnergyConcentrationRatio = testCase.MaxEnergyConcentrationRatio ?? 0.5,
            AntennaConfig = testCase.Antenna ?? new SmallAntennaConfig()
        };
        return options;
    }

    public SmallAntennaOptimizerConfig CreateOptimizerConfig(SmallAntennaObjectiveMode mode)
    {
        return new SmallAntennaOptimizerConfig
        {
            StartHz = FrequencyStartMHz * 1e6,
            StopHz = FrequencyStopMHz * 1e6,
            Points = Points,
            TargetInputReturnLossDb = TargetReturnLossDb,
            MaxAntennaVoltage = MaxVoltage,
            MaxIterations = Iterations,
            UseContinuousValues = UseContinuousValues,
            ContinuousLMinHenries = LMinNh * 1e-9,
            ContinuousLMaxHenries = LMaxNh * 1e-9,
            ContinuousCMinFarads = CMinPf * 1e-12,
            ContinuousCMaxFarads = CMaxPf * 1e-12,
            UseSmoothIncreasingGradient = UseSmoothGradient,
            CellSpacingMeters = SpacingMm * 1e-3,
            ObjectiveMode = mode,
            AntennaConfig = AntennaConfig
        };
    }

    private static string Get(Dictionary<string, string?> values, string name, string defaultValue) =>
        values.TryGetValue(name, out var value) && value is not null ? value : defaultValue;

    private static string? GetNullable(Dictionary<string, string?> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static double GetDouble(Dictionary<string, string?> values, string name, double defaultValue) =>
        values.TryGetValue(name, out var value) && value is not null
            ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : defaultValue;

    private static double? GetNullableDouble(Dictionary<string, string?> values, string name) =>
        values.TryGetValue(name, out var value) && value is not null
            ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static int GetInt(Dictionary<string, string?> values, string name, int defaultValue) =>
        values.TryGetValue(name, out var value) && value is not null
            ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : defaultValue;
}

internal sealed class CliMetrics
{
    public string Mode { get; set; } = string.Empty;
    public double BestReturnLossDb { get; set; }
    [JsonPropertyName("frequency_of_best_match_mhz")]
    public double FrequencyOfBestMatchMHz { get; set; }
    public double DeliveredPowerDb { get; set; }
    public double RadiatedPowerDb { get; set; }
    public double RadiationEfficiencyPercent { get; set; }
    public double TotalPhaseShiftDeg { get; set; }
    public double TotalGroupDelayNs { get; set; }
    public double AverageSlowWaveFactor { get; set; }
    public double MaxSlowWaveFactor { get; set; }
    public int SlowWaveFactorPeakCell { get; set; }
    public double EnergyConcentrationRatio { get; set; }
    public double PhaseSmoothnessError { get; set; }
    public double LcSmoothnessError { get; set; }
    public double CompressionRatio { get; set; }
    public double OptimizerScore { get; set; }
    public SmallAntennaScoreBreakdown ScoreBreakdown { get; set; }
    public Dictionary<string, bool> PassFailFlags { get; set; } = new(StringComparer.Ordinal);
    public string? OutputPath { get; set; }
    public List<RfElement>? Structure { get; set; }

    public bool AllFinite()
    {
        return new[]
        {
            BestReturnLossDb, FrequencyOfBestMatchMHz, DeliveredPowerDb, RadiatedPowerDb,
            RadiationEfficiencyPercent, TotalPhaseShiftDeg, TotalGroupDelayNs, AverageSlowWaveFactor,
            MaxSlowWaveFactor, EnergyConcentrationRatio, PhaseSmoothnessError, LcSmoothnessError,
            CompressionRatio, OptimizerScore
        }.All(double.IsFinite);
    }

    public Dictionary<string, double> ToMetricDictionary()
    {
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["best_return_loss_db"] = BestReturnLossDb,
            ["frequency_of_best_match_mhz"] = FrequencyOfBestMatchMHz,
            ["delivered_power_db"] = DeliveredPowerDb,
            ["radiated_power_db"] = RadiatedPowerDb,
            ["radiation_efficiency_percent"] = RadiationEfficiencyPercent,
            ["total_phase_shift_deg"] = TotalPhaseShiftDeg,
            ["total_group_delay_ns"] = TotalGroupDelayNs,
            ["average_slow_wave_factor"] = AverageSlowWaveFactor,
            ["max_slow_wave_factor"] = MaxSlowWaveFactor,
            ["slow_wave_factor_peak_cell"] = SlowWaveFactorPeakCell,
            ["energy_concentration_ratio"] = EnergyConcentrationRatio,
            ["phase_smoothness_error"] = PhaseSmoothnessError,
            ["lc_smoothness_error"] = LcSmoothnessError,
            ["compression_ratio"] = CompressionRatio,
            ["optimizer_score"] = OptimizerScore
        };
    }
}

internal sealed class RegressionCase
{
    public string? Name { get; set; }
    public string Mode { get; set; } = "antenna";
    public List<RfElement>? Structure { get; set; }
    public SmallAntennaConfig? Antenna { get; set; }
    public double? FrequencyStartMHz { get; set; }
    public double? FrequencyStopMHz { get; set; }
    public double? FrequencyMHz { get; set; }
    public int? Points { get; set; }
    public double? SpacingMm { get; set; }
    public double? TargetReturnLossDb { get; set; }
    public double? MaxVoltage { get; set; }
    public double? MaxSlowWaveFactor { get; set; }
    public double? MaxEnergyConcentrationRatio { get; set; }
    public bool? ExpectOverallPass { get; set; }
    public Dictionary<string, MetricRange>? ExpectedMetricRanges { get; set; }
}

internal sealed class MetricRange
{
    public double? Min { get; set; }
    public double? Max { get; set; }
}

internal sealed class RegressionResult
{
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public bool AnalysisPassed { get; set; }
    public string AnalysisStatus { get; set; } = string.Empty;
    public bool? ExpectedOverallPass { get; set; }
    public bool Passed { get; set; }
    public List<string> Failures { get; set; } = new();
    public CliMetrics? Metrics { get; set; }
}

internal sealed class RegressionSuiteResult
{
    public string TestDirectory { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public int Total { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> SummaryLines { get; set; } = new();
    public List<RegressionResult> Results { get; set; } = new();
}

internal sealed class CliError
{
    public string Error { get; set; } = string.Empty;
}
