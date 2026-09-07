using System.Numerics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
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
            if (args.Length > 0 && args[0] == "--inspect-s2p")
            {
                if (args.Length != 2 && args.Length != 3) throw new InvalidOperationException("Usage: --inspect-s2p FILE [SAVER_CAL_FILE]");
                var measurement = TouchstoneImporter.Read(args[1]);
                if (args.Length == 3) measurement = new SaverCalibration(args[2]).Apply(measurement);
                var phase = RfMath.GetUnwrappedS21PhaseRadians(measurement.Points);
                var delay = RfMath.CalculateGroupDelayNs(measurement.Points);
                WriteJson(new
                {
                    measurement.Source,
                    measurement.ReferenceOhms,
                    Count = measurement.Points.Count,
                    Calibration = "Unknown",
                    ReverseParameters = "Not used or certified as measured",
                    BandMeanDelayNs = -(phase[^1] - phase[0]) / (2 * Math.PI * (measurement.Points[^1].FrequencyHz - measurement.Points[0].FrequencyHz)) * 1e9,
                    Samples = measurement.Points.Select((point, index) => new
                    {
                        point.FrequencyHz,
                        S11Db = RfMath.ToDbMagnitude(point.S11),
                        S21Db = RfMath.ToDbMagnitude(point.S21),
                        PhaseDegrees = phase[index] * 180 / Math.PI,
                        DelayNs = delay[index],
                        TransmissionNullCaution = point.S21.Magnitude < 1e-6
                    })
                });
                return 0;
            }
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

            if (!string.IsNullOrWhiteSpace(options.BatchOptimizeObjective))
            {
                var batchResult = RunBatchOptimization(options);
                WriteJson(batchResult);
                return batchResult.Passed ? 0 : 4;
            }

            if (options.Mode.Equals("grow-compression", StringComparison.OrdinalIgnoreCase))
            {
                var growthResult = RunGrowingCompressionSynthesis(options);
                WriteJson(growthResult);
                return growthResult.Passed ? 0 : 5;
            }

            if (options.Mode.Equals("refine-adiabatic", StringComparison.OrdinalIgnoreCase))
            {
                var refineResult = RunAdiabaticRefinement(options);
                WriteJson(refineResult);
                return refineResult.Passed ? 0 : 6;
            }

            if (options.Mode.Equals("refine-linear-slowwave", StringComparison.OrdinalIgnoreCase))
            {
                var refineResult = RunLinearSlowWaveRefinement(options);
                WriteJson(refineResult);
                return refineResult.Passed ? 0 : 7;
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

    private static AdiabaticRefinementResult RunAdiabaticRefinement(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath))
            throw new InvalidOperationException("refine-adiabatic requires --input structure.json.");

        var optimizerConfig = options.CreateOptimizerConfig(SmallAntennaObjectiveMode.SmoothCompression);
        var random = new Random(options.RandomSeed);
        var initial = EvaluateBatchCandidate(
            LoadStructure(options.InputPath),
            options,
            optimizerConfig,
            SmallAntennaObjectiveMode.SmoothCompression);
        ApplyGrowingValidation(initial.Metrics, options);

        var working = initial;
        var accepted = 0;
        var rejected = 0;
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            var structure = MutateAdiabaticStructure(working.Candidate.Structure, options, random);
            var evaluated = EvaluateBatchCandidate(
                structure,
                options,
                optimizerConfig,
                SmallAntennaObjectiveMode.SmoothCompression);
            ApplyGrowingValidation(evaluated.Metrics, options);
            if (!PassesAdiabaticRefinementConstraints(initial.Metrics, evaluated.Metrics, options)
                || CalculateAdiabaticRefinementScore(evaluated.Metrics) >= CalculateAdiabaticRefinementScore(working.Metrics))
            {
                rejected++;
                continue;
            }

            working = evaluated;
            accepted++;
        }

        var passed = PassesAdiabaticRefinementConstraints(initial.Metrics, working.Metrics, options);
        if (passed && !string.IsNullOrWhiteSpace(options.OutputPath))
            SaveStructure(options.OutputPath, working.Candidate.Structure);

        return new AdiabaticRefinementResult
        {
            Mode = "refine-adiabatic",
            Passed = passed,
            InputPath = options.InputPath,
            OutputPath = options.OutputPath,
            Iterations = options.Iterations,
            RandomSeed = options.RandomSeed,
            AcceptedCandidates = accepted,
            RejectedCandidates = rejected,
            InitialAdiabaticScore = CalculateAdiabaticRefinementScore(initial.Metrics),
            FinalAdiabaticScore = CalculateAdiabaticRefinementScore(working.Metrics),
            InitialMetrics = initial.Metrics,
            FinalMetrics = working.Metrics,
            FinalStructure = working.Candidate.Structure
        };
    }

    private static List<RfElement> MutateAdiabaticStructure(
        IReadOnlyList<RfElement> structure,
        CliOptions options,
        Random random)
    {
        var mutated = OptimizerEngine.CloneStructure(structure);
        for (var mutation = 0; mutation < options.MutationsPerCandidate; mutation++)
        {
            var element = mutated[random.Next(mutated.Count)];
            var scale = Math.Exp((random.NextDouble() * 2.0 - 1.0) * options.MutationLogStep);
            element.Value = ClampMutationValue(element, element.Value * scale, options);
        }
        return mutated;
    }

    private static bool PassesAdiabaticRefinementConstraints(
        CliMetrics baseline,
        CliMetrics candidate,
        CliOptions options)
    {
        return candidate.PassFailFlags.GetValueOrDefault("finite_metrics")
               && candidate.PassFailFlags.GetValueOrDefault("propagation_supported")
               && candidate.TargetFrequencyS11Db <= options.TargetReturnLossDb
               && candidate.TargetFrequencyS21Db >= options.RefineMinimumS21Db
               && candidate.TargetFrequencyGroupDelayNs >= baseline.TargetFrequencyGroupDelayNs * options.RefineMinimumRetentionFraction
               && Math.Abs(candidate.TargetFrequencyPhaseDeg) >= Math.Abs(baseline.TargetFrequencyPhaseDeg) * options.RefineMinimumRetentionFraction
               && candidate.ScoreBreakdown.ResonancePenalty <= options.RefineMaxResonancePenalty;
    }

    private static double CalculateAdiabaticRefinementScore(CliMetrics metrics)
    {
        return metrics.NormalizedSlowWaveJumpPenalty * 20.0
               + metrics.SlowWaveCurvaturePenalty * 0.05
               + metrics.LocalPhaseJumpPenalty * 5.0
               + Math.Max(0, metrics.VoltageRippleRatio - 1.0) * 20.0
               + metrics.EnergyRipplePenalty * 10.0
               + metrics.MaxEnergyFraction * 100.0;
    }

    private static AdiabaticRefinementResult RunLinearSlowWaveRefinement(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath))
            throw new InvalidOperationException("refine-linear-slowwave requires --input structure.json.");

        var optimizerConfig = options.CreateOptimizerConfig(SmallAntennaObjectiveMode.SmoothCompression);
        var random = new Random(options.RandomSeed);
        var initial = EvaluateBatchCandidate(
            LoadStructure(options.InputPath),
            options,
            optimizerConfig,
            SmallAntennaObjectiveMode.SmoothCompression);
        ApplyGrowingValidation(initial.Metrics, options);

        var working = initial;
        var accepted = 0;
        var rejected = 0;
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            var structure = MutateAdiabaticStructure(working.Candidate.Structure, options, random);
            var evaluated = EvaluateBatchCandidate(
                structure,
                options,
                optimizerConfig,
                SmallAntennaObjectiveMode.SmoothCompression);
            ApplyGrowingValidation(evaluated.Metrics, options);
            if (!PassesLinearSlowWaveConstraints(initial.Metrics, working.Metrics, evaluated.Metrics, options)
                || CalculateLinearSlowWaveRefinementScore(evaluated.Metrics)
                >= CalculateLinearSlowWaveRefinementScore(working.Metrics))
            {
                rejected++;
                continue;
            }

            working = evaluated;
            accepted++;
        }

        var passed = PassesLinearSlowWaveFinalConstraints(initial.Metrics, working.Metrics, options);
        if (passed && !string.IsNullOrWhiteSpace(options.OutputPath))
            SaveStructure(options.OutputPath, working.Candidate.Structure);

        return new AdiabaticRefinementResult
        {
            Mode = "refine-linear-slowwave",
            Passed = passed,
            InputPath = options.InputPath,
            OutputPath = options.OutputPath,
            Iterations = options.Iterations,
            RandomSeed = options.RandomSeed,
            AcceptedCandidates = accepted,
            RejectedCandidates = rejected,
            InitialAdiabaticScore = CalculateLinearSlowWaveRefinementScore(initial.Metrics),
            FinalAdiabaticScore = CalculateLinearSlowWaveRefinementScore(working.Metrics),
            InitialMetrics = initial.Metrics,
            FinalMetrics = working.Metrics,
            FinalStructure = working.Candidate.Structure
        };
    }

    private static bool PassesLinearSlowWaveConstraints(
        CliMetrics baseline,
        CliMetrics current,
        CliMetrics candidate,
        CliOptions options)
    {
        return PassesLinearSlowWaveFinalConstraints(baseline, candidate, options)
               && candidate.SlowWaveJumpPenalty < current.SlowWaveJumpPenalty
               && candidate.MaxSlowWaveJump < current.MaxSlowWaveJump
               && candidate.VoltageRippleRatio <= current.VoltageRippleRatio
               && candidate.MaxEnergyFraction <= Math.Max(options.LinearSlowWavePreferredMaxEnergyFraction, baseline.MaxEnergyFraction);
    }

    private static bool PassesLinearSlowWaveFinalConstraints(
        CliMetrics baseline,
        CliMetrics candidate,
        CliOptions options)
    {
        return candidate.PassFailFlags.GetValueOrDefault("finite_metrics")
               && candidate.PassFailFlags.GetValueOrDefault("propagation_supported")
               && candidate.TargetFrequencyS11Db <= options.TargetReturnLossDb
               && candidate.TargetFrequencyS21Db >= options.RefineMinimumS21Db
               && candidate.TargetFrequencyGroupDelayNs >= baseline.TargetFrequencyGroupDelayNs * options.LinearSlowWaveMinimumRetentionFraction
               && Math.Abs(candidate.TargetFrequencyPhaseDeg) >= Math.Abs(baseline.TargetFrequencyPhaseDeg) * options.LinearSlowWaveMinimumRetentionFraction
               && candidate.ScoreBreakdown.ResonancePenalty <= options.RefineMaxResonancePenalty
               && candidate.SectionClassification?.RecoverySectionLikely == true;
    }

    private static double CalculateLinearSlowWaveRefinementScore(CliMetrics metrics)
    {
        return metrics.SlowWaveLinearityError * 100.0
               + metrics.CompressionRegionMonotonicityError * 50.0
               + metrics.RecoveryRegionSmoothnessError * 50.0
               + metrics.NormalizedSlowWaveJumpPenalty * 10.0
               + metrics.MaxSlowWaveJump * 0.1
               + Math.Max(0, 3 - metrics.SlowWavePeakWidthCells) * 25.0
               + Math.Max(0, metrics.VoltageRippleRatio - 1.0) * 10.0
               + metrics.MaxEnergyFraction * 100.0;
    }

    private static GrowingCompressionResult RunGrowingCompressionSynthesis(CliOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var optimizerConfig = options.CreateOptimizerConfig(SmallAntennaObjectiveMode.SmoothCompression);
        var random = new Random(options.RandomSeed);
        var steps = new List<GrowingCompressionStep>();
        var growthReport = new List<string>();
        var rejectedSteps = 0;
        var usesKnownSeed = !string.IsNullOrWhiteSpace(options.SeedStructurePath);
        var seed = usesKnownSeed
            ? LoadKnownGrowingSeed(options, optimizerConfig, out var rejectedSeedDiagnostics)
            : FindGrowingSeed(options, optimizerConfig, stopwatch, ref rejectedSteps, out rejectedSeedDiagnostics);
        if (seed is null)
        {
            return new GrowingCompressionResult
            {
                Mode = "grow-compression",
                Passed = false,
                AcceptedCells = 0,
                RejectedStepCount = rejectedSteps,
                StopReason = HasRuntimeExpired(options, stopwatch)
                    ? "Runtime limit reached before a coupled seed was found."
                    : "No SeriesL + ShuntC or ShuntC + SeriesL seed met the return-loss, coupling, and power-delivery constraints.",
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                GrowthReport = new List<string>
                {
                    "1 cell: REJECT, reason = no seed met the complete launcher validation constraints.",
                    "Final accepted structure: 0 cells."
                },
                BestRejectedSeeds = rejectedSeedDiagnostics,
                Steps = steps
            };
        }

        var current = seed;
        ApplyGrowingValidation(current.Metrics, options);
        var initialCellCount = CountGrowingCells(current.Candidate.Structure);
        steps.Add(CreateGrowingStep(initialCellCount, current, 0, usesKnownSeed ? "Known launcher seed accepted." : "Coupled seed accepted."));
        var initialFinalPass = PassesGrowingFinalValidation(current.Metrics);
        growthReport.Add(FormatGrowingReportLine(
            initialCellCount,
            current.Metrics,
            initialFinalPass ? "PASS" : "LAUNCHER ONLY",
            usesKnownSeed
                ? initialFinalPass ? "known launcher seed accepted" : "known launcher accepted; final medium validation not yet satisfied"
                : initialFinalPass ? "coupled seed accepted" : "coupled seed accepted; final medium validation not yet satisfied",
            usesKnownSeed));
        if (initialFinalPass)
        {
            SaveGrowingStructure(options.OutputPath, current.Candidate.Structure);
        }

        var stopReason = $"Maximum cell count {options.MaxCells} reached.";
        for (var cellCount = initialCellCount + 1; cellCount <= options.MaxCells; cellCount++)
        {
            if (HasRuntimeExpired(options, stopwatch))
            {
                stopReason = "Runtime limit reached.";
                break;
            }

            var bestStepCandidate = FindNextGrowingCell(
                current,
                options,
                optimizerConfig,
                random,
                stopwatch,
                usesKnownSeed,
                out var rejectedAttempts,
                out var bestRejected,
                out var rejectionReason);
            rejectedSteps += rejectedAttempts;
            if (bestStepCandidate is null)
            {
                stopReason = HasRuntimeExpired(options, stopwatch)
                    ? "Runtime limit reached while optimizing the next cell."
                    : $"No acceptable cell {cellCount} extension preserved coupling and adiabatic growth.";
                growthReport.Add(bestRejected is null
                    ? $"{cellCount} cells: REJECT, reason = {rejectionReason}"
                    : FormatGrowingReportLine(cellCount, bestRejected.Metrics, "REJECT", rejectionReason, usesKnownSeed));
                break;
            }

            current = bestStepCandidate;
            steps.Add(CreateGrowingStep(cellCount, current, rejectedAttempts, "Adiabatic cell extension accepted."));
            growthReport.Add(FormatGrowingReportLine(
                cellCount,
                current.Metrics,
                "PASS",
                "adiabatic cell extension accepted",
                usesKnownSeed));
            ApplyGrowingValidation(current.Metrics, options);
            if (PassesGrowingFinalValidation(current.Metrics))
            {
                SaveGrowingStructure(options.OutputPath, current.Candidate.Structure);
            }
        }

        var finalCellCount = CountGrowingCells(current.Candidate.Structure);
        growthReport.Add($"Final accepted structure: {finalCellCount} {(finalCellCount == 1 ? "cell" : "cells")}.");
        ApplyGrowingValidation(current.Metrics, options);
        var finalStatus = CreateGrowingFinalStatus(current.Metrics);
        return new GrowingCompressionResult
        {
            Mode = "grow-compression",
            Passed = current.Metrics.PassFailFlags.GetValueOrDefault("overall"),
            AcceptedCells = CountGrowingCells(current.Candidate.Structure),
            RejectedStepCount = rejectedSteps,
            StopReason = stopReason,
            OutputPath = options.OutputPath,
            SeedStructurePath = options.SeedStructurePath,
            ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
            FinalStructure = current.Candidate.Structure,
            FinalMetrics = current.Metrics,
            FinalUsableRfDevice = current.Metrics.PassFailFlags.GetValueOrDefault("usable_rf_device"),
            FinalStatus = finalStatus,
            FinalVerdict = BuildGrowingFinalVerdict(finalStatus),
            FinalValidationReasons = GetGrowingFinalValidationReasons(current.Metrics, options),
            GrowthReport = growthReport,
            BestRejectedSeeds = rejectedSeedDiagnostics,
            Steps = steps
        };
    }

    private static BatchCandidateEvaluation? LoadKnownGrowingSeed(
        CliOptions options,
        SmallAntennaOptimizerConfig optimizerConfig,
        out List<GrowingRejectedSeed> rejectedSeedDiagnostics)
    {
        var structure = LoadStructure(options.SeedStructurePath!);
        PropagationAnalysis.ValidateSupportedLadder(structure);
        var evaluated = EvaluateBatchCandidate(
            structure,
            options,
            optimizerConfig,
            SmallAntennaObjectiveMode.SmoothCompression);
        ApplyGrowingValidation(evaluated.Metrics, options);
        var reasons = GetKnownGrowingSeedRejectionReasons(evaluated.Metrics, options);
        rejectedSeedDiagnostics = reasons.Count == 0
            ? new List<GrowingRejectedSeed>()
            : new List<GrowingRejectedSeed> { CreateRejectedSeed(evaluated, reasons) };
        return reasons.Count == 0 ? evaluated : null;
    }

    private static BatchCandidateEvaluation? FindGrowingSeed(
        CliOptions options,
        SmallAntennaOptimizerConfig optimizerConfig,
        Stopwatch stopwatch,
        ref int rejectedSteps,
        out List<GrowingRejectedSeed> rejectedSeedDiagnostics)
    {
        BatchCandidateEvaluation? best = null;
        var rejectedSeeds = new List<GrowingRejectedSeed>();
        foreach (var inductance in BuildGrowingSearchValues(
                     OptimizerEngine.AvailableInductors,
                     options.LMinNh * 1e-9,
                     options.LMaxNh * 1e-9))
        {
            foreach (var capacitance in BuildGrowingSearchValues(
                         OptimizerEngine.AvailableCapacitors,
                         options.CMinPf * 1e-12,
                         options.CMaxPf * 1e-12))
            {
                if (HasRuntimeExpired(options, stopwatch))
                {
                    rejectedSeedDiagnostics = rejectedSeeds
                        .OrderBy(seed => seed.BestReturnLossDb)
                        .ThenByDescending(seed => seed.DeliveredPowerDb)
                        .Take(options.RejectedSeedDiagnosticCount)
                        .ToList();
                    return best;
                }

                foreach (var structure in new[]
                {
                    new List<RfElement>
                    {
                        new() { Type = "SeriesL", Value = inductance },
                        new() { Type = "ShuntC", Value = capacitance }
                    },
                    new List<RfElement>
                    {
                        new() { Type = "ShuntC", Value = capacitance },
                        new() { Type = "SeriesL", Value = inductance }
                    }
                })
                {
                    var evaluated = EvaluateBatchCandidate(
                        structure,
                        options,
                        optimizerConfig,
                        SmallAntennaObjectiveMode.SmoothCompression);
                    ApplyGrowingValidation(evaluated.Metrics, options);
                    var rejectionReasons = GetGrowingSeedRejectionReasons(evaluated.Metrics, options);
                    if (rejectionReasons.Count > 0)
                    {
                        rejectedSteps++;
                        rejectedSeeds.Add(CreateRejectedSeed(evaluated, rejectionReasons));
                        continue;
                    }

                    if (best is null || IsBetterGrowingSeed(evaluated.Metrics, best.Metrics))
                    {
                        best = evaluated;
                    }
                }
            }
        }

        rejectedSeedDiagnostics = rejectedSeeds
            .OrderBy(seed => seed.BestReturnLossDb)
            .ThenByDescending(seed => seed.DeliveredPowerDb)
            .Take(options.RejectedSeedDiagnosticCount)
            .ToList();
        return best;
    }

    private static GrowingRejectedSeed CreateRejectedSeed(
        BatchCandidateEvaluation evaluated,
        List<string> reasons)
    {
        return new GrowingRejectedSeed
        {
            BestReturnLossDb = evaluated.Metrics.BestReturnLossDb,
            DeliveredPowerDb = evaluated.Metrics.DeliveredPowerDb,
            BestMatchReflectionCoefficientMagnitude = evaluated.Metrics.BestMatchReflectionCoefficientMagnitude,
            BestMatchReflectedPowerPercent = evaluated.Metrics.BestMatchReflectedPowerPercent,
            BestMatchAcceptedPowerPercent = evaluated.Metrics.BestMatchAcceptedPowerPercent,
            BestMatchDeliveredPowerPercent = evaluated.Metrics.BestMatchDeliveredPowerPercent,
            AverageInputReflectionPercent = evaluated.Metrics.AverageInputReflectionPercent,
            ReferenceBestS11Db = evaluated.Metrics.ReferenceBestS11Db,
            ReferenceS21AtBestS11Db = evaluated.Metrics.ReferenceS21AtBestS11Db,
            TargetFrequencyS11Db = evaluated.Metrics.TargetFrequencyS11Db,
            TargetFrequencyS21Db = evaluated.Metrics.TargetFrequencyS21Db,
            ReflectionMetricsConsistent =
                evaluated.Metrics.PassFailFlags.GetValueOrDefault("reflection_metrics_consistent"),
            PowerBalanceConsistent =
                evaluated.Metrics.PassFailFlags.GetValueOrDefault("power_balance_consistent"),
            Reasons = reasons,
            Structure = evaluated.Candidate.Structure
        };
    }

    private static BatchCandidateEvaluation? FindNextGrowingCell(
        BatchCandidateEvaluation current,
        CliOptions options,
        SmallAntennaOptimizerConfig optimizerConfig,
        Random random,
        Stopwatch stopwatch,
        bool useReferenceNetworkMetrics,
        out int rejectedAttempts,
        out BatchCandidateEvaluation? bestRejected,
        out string rejectionReason)
    {
        rejectedAttempts = 0;
        bestRejected = null;
        rejectionReason = "no candidate was evaluated";
        BatchCandidateEvaluation? best = null;
        var appended = OptimizerEngine.CloneStructure(current.Candidate.Structure);
        var firstNewType = appended[^1].Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
            ? "ShuntC"
            : "SeriesL";
        var secondNewType = firstNewType.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
            ? "ShuntC"
            : "SeriesL";
        appended.Add(new RfElement
        {
            Type = firstNewType,
            Value = FindLastValue(appended, firstNewType)
        });
        appended.Add(new RfElement
        {
            Type = secondNewType,
            Value = FindLastValue(appended, secondNewType)
        });

        for (var iteration = 0; iteration < options.IterationsPerStep; iteration++)
        {
            if (HasRuntimeExpired(options, stopwatch))
            {
                break;
            }

            var structure = OptimizerEngine.CloneStructure(appended);
            MutateGrowingTail(structure, options, random);
            var evaluated = EvaluateBatchCandidate(
                structure,
                options,
                optimizerConfig,
                SmallAntennaObjectiveMode.SmoothCompression);
            ApplyGrowingValidation(evaluated.Metrics, options);
            var rejectionReasons = GetGrowingExtensionRejectionReasons(
                current.Metrics,
                evaluated,
                options,
                useReferenceNetworkMetrics);
            if (rejectionReasons.Count > 0)
            {
                rejectedAttempts++;
                if (bestRejected is null || evaluated.Candidate.Score < bestRejected.Candidate.Score)
                {
                    bestRejected = evaluated;
                    rejectionReason = string.Join("; ", rejectionReasons);
                }
                continue;
            }

            if (best is null || evaluated.Candidate.Score < best.Candidate.Score)
            {
                best = evaluated;
            }
        }

        return best;
    }

    private static double FindLastValue(IReadOnlyList<RfElement> structure, string type)
    {
        return structure
            .Where(element => element.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value)
            .LastOrDefault(type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase) ? 100e-9 : 51e-12);
    }

    private static void MutateGrowingTail(List<RfElement> structure, CliOptions options, Random random)
    {
        var tailElementCount = Math.Min(structure.Count, 2 * (1 + options.OptimizePreviousCells));
        var tailStart = structure.Count - tailElementCount;
        var mutationCount = Math.Max(2, options.MutationsPerCandidate);
        for (var mutation = 0; mutation < mutationCount; mutation++)
        {
            var element = structure[random.Next(tailStart, structure.Count)];
            var availableValues = element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
                ? BuildGrowingSearchValues(OptimizerEngine.AvailableInductors, options.LMinNh * 1e-9, options.LMaxNh * 1e-9)
                : BuildGrowingSearchValues(OptimizerEngine.AvailableCapacitors, options.CMinPf * 1e-12, options.CMaxPf * 1e-12);
            element.Value = availableValues[random.Next(availableValues.Length)];
        }
    }

    private static double[] BuildGrowingSearchValues(
        IReadOnlyList<double> discreteValues,
        double minimum,
        double maximum)
    {
        const int logarithmicSamples = 32;
        var values = discreteValues
            .Where(value => value >= minimum && value <= maximum)
            .ToList();
        var logMinimum = Math.Log(minimum);
        var logMaximum = Math.Log(maximum);
        for (var index = 0; index < logarithmicSamples; index++)
        {
            var fraction = (double)index / (logarithmicSamples - 1);
            values.Add(Math.Exp(logMinimum + fraction * (logMaximum - logMinimum)));
        }

        return values.Distinct().OrderBy(value => value).ToArray();
    }

    private static List<string> GetGrowingSeedRejectionReasons(CliMetrics metrics, CliOptions options)
    {
        var reasons = new List<string>();
        if (!metrics.PassFailFlags.GetValueOrDefault("finite_metrics"))
            reasons.Add("non-finite metrics");
        if (!metrics.PassFailFlags.GetValueOrDefault("reflection_metrics_consistent"))
            reasons.Add("reflection metrics are inconsistent");
        if (!metrics.PassFailFlags.GetValueOrDefault("power_balance_consistent"))
            reasons.Add("accepted and delivered power are inconsistent");
        if (!metrics.PassFailFlags.GetValueOrDefault("target_return_loss"))
            reasons.Add("input return loss missed target");
        if (metrics.DeliveredPowerAtBestMatchDb < options.MinimumSeedDeliveredPowerDb)
            reasons.Add("delivered power below seed minimum");
        if (!metrics.PassFailFlags.GetValueOrDefault("slow_wave_bounded"))
            reasons.Add("slow-wave factor exceeded bound");
        if (!metrics.PassFailFlags.GetValueOrDefault("energy_distributed"))
            reasons.Add("energy localization");
        if (!metrics.PassFailFlags.GetValueOrDefault("input_coupling_acceptable"))
            reasons.Add("excessive sweep-average reflected input power");
        if (!metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality"))
            reasons.Add("internal medium quality failed");
        if (!metrics.PassFailFlags.GetValueOrDefault("usable_rf_device"))
            reasons.Add("usable RF-device validation failed");
        if (metrics.ScoreBreakdown.ResonancePenalty > options.MaxResonancePenalty)
            reasons.Add("resonance penalty exceeded limit");
        if (metrics.MaxSlowWaveJump > options.MaxSlowWaveJump)
            reasons.Add("slow-wave jump exceeded limit");
        if (metrics.MaxLocalPhaseJumpDeg > options.MaxLocalPhaseJumpDeg)
            reasons.Add("local phase jump exceeded limit");
        if (metrics.VoltageRippleRatio > options.MaxVoltageRippleRatio)
            reasons.Add("node-voltage ripple exceeded limit");
        if (metrics.MaxEnergyFraction > options.MaxEnergyConcentrationRatio)
            reasons.Add("localized energy spike");
        return reasons;
    }

    private static List<string> GetKnownGrowingSeedRejectionReasons(CliMetrics metrics, CliOptions options)
    {
        var reasons = new List<string>();
        if (!metrics.PassFailFlags.GetValueOrDefault("finite_metrics"))
            reasons.Add("non-finite metrics");
        if (!metrics.PassFailFlags.GetValueOrDefault("reflection_metrics_consistent"))
            reasons.Add("reflection metrics are inconsistent");
        if (!metrics.PassFailFlags.GetValueOrDefault("target_frequency_match"))
            reasons.Add("target-frequency S11 missed target");
        if (!metrics.PassFailFlags.GetValueOrDefault("target_frequency_transmission_acceptable"))
            reasons.Add("target-frequency S21 below minimum");
        if (!metrics.PassFailFlags.GetValueOrDefault("propagation_supported"))
            reasons.Add("propagation analysis failed");
        return reasons;
    }

    private static bool IsBetterGrowingSeed(CliMetrics candidate, CliMetrics currentBest)
    {
        var deliveredPowerComparison = candidate.DeliveredPowerAtBestMatchDb.CompareTo(currentBest.DeliveredPowerAtBestMatchDb);
        if (deliveredPowerComparison != 0)
            return deliveredPowerComparison > 0;

        var returnLossComparison = candidate.BestReturnLossDb.CompareTo(currentBest.BestReturnLossDb);
        if (returnLossComparison != 0)
            return returnLossComparison < 0;

        return candidate.AverageInputReflectionPercent < currentBest.AverageInputReflectionPercent;
    }

    private static List<string> GetGrowingExtensionRejectionReasons(
        CliMetrics previous,
        BatchCandidateEvaluation evaluated,
        CliOptions options,
        bool useReferenceNetworkMetrics)
    {
        var metrics = evaluated.Metrics;
        var reasons = new List<string>();
        if (!metrics.PassFailFlags.GetValueOrDefault("target_frequency_match"))
            reasons.Add("target-frequency S11 missed target");
        if (!metrics.PassFailFlags.GetValueOrDefault("target_frequency_transmission_acceptable"))
            reasons.Add("target-frequency S21 below minimum");
        if (metrics.TargetFrequencyS21Db < previous.TargetFrequencyS21Db - options.MaxDeliveredPowerCollapseDb)
            reasons.Add("target-frequency S21 collapsed");
        if (!metrics.PassFailFlags.GetValueOrDefault("slow_wave_bounded"))
            reasons.Add("slow-wave factor exceeded bound");
        if (!metrics.PassFailFlags.GetValueOrDefault("propagation_supported"))
            reasons.Add("propagation analysis failed");
        if (Math.Abs(metrics.TargetFrequencyPhaseDeg) < Math.Abs(previous.TargetFrequencyPhaseDeg) + options.MinimumPhaseIncreaseDeg)
            reasons.Add("phase delay did not increase");
        if (metrics.TargetFrequencyGroupDelayNs < previous.TargetFrequencyGroupDelayNs + options.MinimumGroupDelayIncreaseNs)
            reasons.Add("group delay did not increase");
        if (metrics.ScoreBreakdown.ResonancePenalty > options.MaxResonancePenalty)
            reasons.Add("resonance penalty exceeded limit");
        if (metrics.MaxSlowWaveJump > options.MaxSlowWaveJump)
            reasons.Add("slow-wave jump exceeded limit");
        if (metrics.MaxLocalPhaseJumpDeg > options.MaxLocalPhaseJumpDeg)
            reasons.Add("local phase jump exceeded limit");
        if (metrics.VoltageRippleRatio > options.MaxVoltageRippleRatio)
            reasons.Add("node-voltage ripple exceeded limit");
        if (metrics.MaxEnergyFraction > options.MaxEnergyConcentrationRatio)
            reasons.Add("localized energy spike");
        if (metrics.AdiabaticQualityScore0To100 + options.MaxAdiabaticQualityDrop < previous.AdiabaticQualityScore0To100)
            reasons.Add("adiabatic quality collapsed");
        return reasons;
    }

    private static void ApplyGrowingValidation(CliMetrics metrics, CliOptions options)
    {
        metrics.PassFailFlags["target_frequency_match"] =
            metrics.TargetFrequencyS11Db <= options.TargetReturnLossDb;
        metrics.PassFailFlags["target_frequency_transmission_acceptable"] =
            metrics.TargetFrequencyS21Db >= options.MinimumReferenceS21Db;
        metrics.PassFailFlags["target_frequency_device_quality"] =
            metrics.PassFailFlags["finite_metrics"]
            && metrics.PassFailFlags["target_frequency_match"]
            && metrics.PassFailFlags["target_frequency_transmission_acceptable"];
        metrics.PassFailFlags["broadband_sweep_quality"] =
            metrics.BroadbandReferenceReflectionPercent <= options.MaxAverageInputReflectionPercent;
        metrics.PassFailFlags["usable_rf_device"] =
            metrics.PassFailFlags["internal_medium_quality"]
            && metrics.PassFailFlags["target_frequency_device_quality"]
            && metrics.ScoreBreakdown.ResonancePenalty <= options.MaxResonancePenalty
            && metrics.EnergyConcentrationRatio <= options.MaxEnergyConcentrationRatio;
        metrics.PassFailFlags["overall"] = metrics.PassFailFlags["usable_rf_device"];
    }

    private static bool PassesGrowingFinalValidation(CliMetrics metrics) =>
        metrics.PassFailFlags.GetValueOrDefault("overall");

    private static List<string> GetGrowingFinalValidationReasons(
        CliMetrics metrics,
        CliOptions options)
    {
        var reasons = new List<string>();
        if (!metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality"))
            reasons.Add("internal medium quality failed");
        if (!metrics.PassFailFlags.GetValueOrDefault("target_frequency_match"))
            reasons.Add("target-frequency S11 missed target");
        if (!metrics.PassFailFlags.GetValueOrDefault("target_frequency_transmission_acceptable"))
            reasons.Add("target-frequency S21 below minimum");
        if (metrics.ScoreBreakdown.ResonancePenalty > options.MaxResonancePenalty)
            reasons.Add("resonance penalty exceeded limit");
        if (metrics.EnergyConcentrationRatio > options.MaxEnergyConcentrationRatio)
            reasons.Add("energy localization exceeded limit");
        return reasons;
    }

    private static GrowingFinalStatus CreateGrowingFinalStatus(CliMetrics metrics) =>
        new()
        {
            InternalMediumQuality = metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality"),
            TargetFrequencyDeviceQuality = metrics.PassFailFlags.GetValueOrDefault("target_frequency_device_quality"),
            BroadbandSweepQuality = metrics.PassFailFlags.GetValueOrDefault("broadband_sweep_quality"),
            FinalOverall = metrics.PassFailFlags.GetValueOrDefault("overall")
        };

    private static string BuildGrowingFinalVerdict(GrowingFinalStatus status)
    {
        if (status.FinalOverall)
            return "PASS: Narrowband target-frequency RF device";
        if (status.InternalMediumQuality && !status.TargetFrequencyDeviceQuality)
            return "FAIL: Internal medium OK, but target-frequency input coupling failed";
        if (!status.InternalMediumQuality && status.TargetFrequencyDeviceQuality)
            return "FAIL: Target-frequency match OK, but energy is localized / resonant";
        return "FAIL: Internal medium quality and target-frequency device quality failed";
    }

    private static int CountGrowingCells(IReadOnlyList<RfElement> structure) =>
        structure.Count(element => element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase));

    private static GrowingCompressionStep CreateGrowingStep(
        int cellCount,
        BatchCandidateEvaluation evaluated,
        int rejectedAttempts,
        string reason)
    {
        return new GrowingCompressionStep
        {
            CellCount = cellCount,
            RejectedAttemptsBeforeAcceptance = rejectedAttempts,
            Reason = reason,
            Metrics = evaluated.Metrics,
            BestStructureAtLength = evaluated.Candidate.Structure
        };
    }

    private static void SaveGrowingStructure(string? path, IReadOnlyList<RfElement> structure)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            SaveStructure(path, structure);
        }
    }

    private static bool HasRuntimeExpired(CliOptions options, Stopwatch stopwatch) =>
        options.RuntimeLimitSeconds > 0 && stopwatch.Elapsed.TotalSeconds >= options.RuntimeLimitSeconds;

    private static string FormatGrowingReportLine(
        int cellCount,
        CliMetrics metrics,
        string status,
        string reason,
        bool useReferenceNetworkMetrics = false)
    {
        return $"{cellCount} {(cellCount == 1 ? "cell" : "cells")}: "
               + $"target S11 = {metrics.TargetFrequencyS11Db:F2} dB, "
               + $"target S21 = {metrics.TargetFrequencyS21Db:F2} dB, "
               + $"target phase = {metrics.TargetFrequencyPhaseDeg:F2} deg, "
               + $"target group delay = {metrics.TargetFrequencyGroupDelayNs:F2} ns, "
               + $"broadband reflected = {metrics.BroadbandReferenceReflectionPercent:F2} %, "
               + $"slow-wave max = {metrics.MaxSlowWaveFactor:F2}, "
               + $"{status}, reason = {reason}";
    }

    private static BatchOptimizationResult RunBatchOptimization(CliOptions options)
    {
        var objectiveMode = options.BatchOptimizeObjective switch
        {
            "antenna" => SmallAntennaObjectiveMode.AntennaMatch,
            "compression" => SmallAntennaObjectiveMode.SmoothCompression,
            _ => throw new InvalidOperationException(
                $"Unsupported batch optimization objective '{options.BatchOptimizeObjective}'.")
        };

        RegressionSuiteResult? regressionBefore = null;
        if (!string.IsNullOrWhiteSpace(options.AutoTestDirectory))
        {
            regressionBefore = ExecuteRegressionTests(options.AutoTestDirectory);
            if (!regressionBefore.Passed)
            {
                return new BatchOptimizationResult
                {
                    Mode = $"optimize-{options.BatchOptimizeObjective}",
                    Passed = false,
                    FailureReason = "Regression tests failed before optimization.",
                    RegressionBefore = regressionBefore
                };
            }
        }

        var structure = !string.IsNullOrWhiteSpace(options.InputPath)
            ? LoadStructure(options.InputPath)
            : CreateDefaultStructure(options.ElementCount);
        var optimizerConfig = options.CreateOptimizerConfig(objectiveMode);
        var random = new Random(options.RandomSeed);
        var history = new List<BatchHistoryEntry>();
        var seed = EvaluateBatchCandidate(structure, options, optimizerConfig, objectiveMode);
        var working = seed.Metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality") ? seed : null;
        var best = seed.Metrics.PassFailFlags.GetValueOrDefault("usable_rf_device") ? seed : null;
        history.Add(CreateHistoryEntry(
            0,
            seed,
            working is not null,
            best is not null ? "usable seed accepted" : working is not null ? "internal-only seed retained" : "seed failed internal validation"));

        if (best is not null)
        {
            SaveStructure(options.SaveBestPath, best.Candidate.Structure);
        }

        var mutationBase = OptimizerEngine.CloneStructure(structure);
        for (var iteration = 1; iteration <= options.Iterations; iteration++)
        {
            var candidateStructure = MutateStructure(mutationBase, options, random, objectiveMode);
            var evaluated = EvaluateBatchCandidate(candidateStructure, options, optimizerConfig, objectiveMode);
            var internallyValid = evaluated.Metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality");
            var usable = evaluated.Metrics.PassFailFlags.GetValueOrDefault("usable_rf_device");
            var retained = internallyValid && (working is null || evaluated.Candidate.Score < working.Candidate.Score);
            var improvesUsableBest = usable && (best is null || evaluated.Candidate.Score < best.Candidate.Score);
            var reason = !internallyValid
                ? "failed internal-medium validation"
                : improvesUsableBest
                    ? "usable-device improvement accepted"
                    : retained
                        ? "internal-only exploration improvement retained"
                        : "score did not improve";

            if (retained)
            {
                working = evaluated;
                mutationBase = OptimizerEngine.CloneStructure(evaluated.Candidate.Structure);
            }

            if (improvesUsableBest)
            {
                best = evaluated;
                SaveStructure(options.SaveBestPath, best.Candidate.Structure);
            }

            history.Add(CreateHistoryEntry(iteration, evaluated, retained, reason));
        }

        ExportHistory(options.HistoryJsonPath, options.HistoryCsvPath, history);
        var regressionAfter = !string.IsNullOrWhiteSpace(options.AutoTestDirectory)
            ? ExecuteRegressionTests(options.AutoTestDirectory)
            : null;
        return new BatchOptimizationResult
        {
            Mode = $"optimize-{options.BatchOptimizeObjective}",
            Passed = best is not null && (regressionAfter?.Passed ?? true),
            FailureReason = best is null
                ? "No physically valid candidate was found."
                : regressionAfter is { Passed: false }
                    ? "Regression tests failed after optimization."
                    : null,
            Iterations = options.Iterations,
            RandomSeed = options.RandomSeed,
            AcceptedCandidates = history.Count(entry => entry.Accepted),
            RejectedCandidates = history.Count(entry => !entry.Accepted),
            SaveBestPath = options.SaveBestPath,
            HistoryJsonPath = options.HistoryJsonPath,
            HistoryCsvPath = options.HistoryCsvPath,
            BestMetrics = best?.Metrics,
            BestInternalMetrics = working?.Metrics,
            RegressionBefore = regressionBefore,
            RegressionAfter = regressionAfter
        };
    }

    private static BatchCandidateEvaluation EvaluateBatchCandidate(
        IReadOnlyList<RfElement> structure,
        CliOptions options,
        SmallAntennaOptimizerConfig optimizerConfig,
        SmallAntennaObjectiveMode objectiveMode)
    {
        var candidate = SmallAntennaMode.Evaluate(OptimizerEngine.CloneStructure(structure), optimizerConfig);
        var metrics = BuildMetrics(candidate.Structure, candidate.Sweep, options, objectiveMode);
        metrics.OptimizerScore = candidate.Score;
        metrics.ScoreBreakdown = candidate.ScoreBreakdown;
        metrics.Structure = candidate.Structure;
        return new BatchCandidateEvaluation(candidate, metrics);
    }

    private static List<RfElement> MutateStructure(
        IReadOnlyList<RfElement> structure,
        CliOptions options,
        Random random,
        SmallAntennaObjectiveMode objectiveMode)
    {
        var mutated = OptimizerEngine.CloneStructure(structure);
        if (mutated.Count == 0)
        {
            return mutated;
        }

        for (var mutation = 0; mutation < options.MutationsPerCandidate; mutation++)
        {
            var launcherElementCount = Math.Clamp(options.LauncherCellCount * 2, 0, mutated.Count);
            var mutateLauncher = objectiveMode == SmallAntennaObjectiveMode.SmoothCompression
                                 && launcherElementCount > 0
                                 && random.NextDouble() < options.LauncherMutationFraction;
            var element = mutated[mutateLauncher ? random.Next(launcherElementCount) : random.Next(mutated.Count)];
            if (options.UseContinuousValues)
            {
                var scale = Math.Exp((random.NextDouble() * 2.0 - 1.0) * options.MutationLogStep);
                element.Value = ClampMutationValue(element, element.Value * scale, options);
                continue;
            }

            var availableValues = element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
                ? OptimizerEngine.AvailableInductors
                : OptimizerEngine.AvailableCapacitors;
            var nearestIndex = Enumerable.Range(0, availableValues.Length)
                .OrderBy(index => Math.Abs(Math.Log(availableValues[index] / element.Value)))
                .First();
            var direction = random.Next(2) == 0 ? -1 : 1;
            var distance = random.Next(1, Math.Min(3, availableValues.Length) + 1);
            var nextIndex = Math.Clamp(nearestIndex + direction * distance, 0, availableValues.Length - 1);
            element.Value = availableValues[nextIndex];
        }

        return mutated;
    }

    private static double ClampMutationValue(RfElement element, double value, CliOptions options)
    {
        return element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(value, options.LMinNh * 1e-9, options.LMaxNh * 1e-9)
            : Math.Clamp(value, options.CMinPf * 1e-12, options.CMaxPf * 1e-12);
    }

    private static BatchHistoryEntry CreateHistoryEntry(
        int iteration,
        BatchCandidateEvaluation evaluated,
        bool accepted,
        string reason)
    {
        return new BatchHistoryEntry
        {
            Iteration = iteration,
            Accepted = accepted,
            Reason = reason,
            OptimizerScore = evaluated.Candidate.Score,
            OverallPassed = evaluated.Metrics.PassFailFlags.GetValueOrDefault("overall"),
            BestReturnLossDb = evaluated.Metrics.BestReturnLossDb,
            AverageSlowWaveFactor = evaluated.Metrics.AverageSlowWaveFactor,
            MaxSlowWaveFactor = evaluated.Metrics.MaxSlowWaveFactor,
            EnergyConcentrationRatio = evaluated.Metrics.EnergyConcentrationRatio,
            PhaseSmoothnessError = evaluated.Metrics.PhaseSmoothnessError,
            LcSmoothnessError = evaluated.Metrics.LcSmoothnessError,
            CompressionRatio = evaluated.Metrics.CompressionRatio,
            AverageInputReflectionPercent = evaluated.Metrics.AverageInputReflectionPercent,
            InputCouplingScore = evaluated.Metrics.ScoreBreakdown.InputCouplingScore,
            UsableRfDevice = evaluated.Metrics.PassFailFlags.GetValueOrDefault("usable_rf_device"),
            Structure = evaluated.Candidate.Structure
        };
    }

    private static void SaveStructure(string path, IReadOnlyList<RfElement> structure)
    {
        EnsureOutputDirectory(path);
        File.WriteAllText(path, JsonSerializer.Serialize(structure, JsonOptions));
    }

    private static void ExportHistory(
        string jsonPath,
        string csvPath,
        IReadOnlyList<BatchHistoryEntry> history)
    {
        EnsureOutputDirectory(jsonPath);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(history, JsonOptions));

        EnsureOutputDirectory(csvPath);
        var csv = new StringBuilder();
        csv.AppendLine(
            "iteration,accepted,reason,optimizer_score,overall_passed,usable_rf_device,best_return_loss_db,average_input_reflection_percent,input_coupling_score,average_slow_wave_factor,max_slow_wave_factor,energy_concentration_ratio,phase_smoothness_error,lc_smoothness_error,compression_ratio");
        foreach (var entry in history)
        {
            csv.AppendLine(string.Join(",",
                entry.Iteration.ToString(CultureInfo.InvariantCulture),
                entry.Accepted.ToString().ToLowerInvariant(),
                CsvEscape(entry.Reason),
                entry.OptimizerScore.ToString("G17", CultureInfo.InvariantCulture),
                entry.OverallPassed.ToString().ToLowerInvariant(),
                entry.UsableRfDevice.ToString().ToLowerInvariant(),
                entry.BestReturnLossDb.ToString("G17", CultureInfo.InvariantCulture),
                entry.AverageInputReflectionPercent.ToString("G17", CultureInfo.InvariantCulture),
                entry.InputCouplingScore.ToString("G17", CultureInfo.InvariantCulture),
                entry.AverageSlowWaveFactor.ToString("G17", CultureInfo.InvariantCulture),
                entry.MaxSlowWaveFactor.ToString("G17", CultureInfo.InvariantCulture),
                entry.EnergyConcentrationRatio.ToString("G17", CultureInfo.InvariantCulture),
                entry.PhaseSmoothnessError.ToString("G17", CultureInfo.InvariantCulture),
                entry.LcSmoothnessError.ToString("G17", CultureInfo.InvariantCulture),
                entry.CompressionRatio.ToString("G17", CultureInfo.InvariantCulture)));
        }
        File.WriteAllText(csvPath, csv.ToString());
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static void EnsureOutputDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static CliMetrics BuildMetrics(
        IReadOnlyList<RfElement> structure,
        IReadOnlyList<SmallAntennaPoint> sweep,
        CliOptions options,
        SmallAntennaObjectiveMode objectiveMode)
    {
        var bestMatch = sweep.OrderBy(point => point.InputReturnLossDb).First();
        var strongestRadiation = sweep.OrderByDescending(point => point.RadiatedPowerWatts).First();
        var referenceSweep = SimulationEngine.Simulate(
            options.FrequencyStartMHz * 1e6,
            options.FrequencyStopMHz * 1e6,
            options.Points,
            SmallAntennaMode.SourceResistanceOhms,
            structure);
        var referenceBestS11 = referenceSweep.OrderBy(point => RfMath.ToDbMagnitude(point.S11)).First();
        var referencePhase = RfMath.CalculatePhaseMetrics(referenceSweep);
        var targetFrequencyHz = (options.FrequencyMHz
                                 ?? (options.FrequencyStartMHz + options.FrequencyStopMHz) / 2.0) * 1e6;
        var targetFrequencyIndex = FindNearestIndex(referenceSweep, targetFrequencyHz);
        var targetFrequencyPoint = referenceSweep[targetFrequencyIndex];
        var referenceUnwrappedPhase = RfMath.GetUnwrappedS21PhaseRadians(referenceSweep);
        var referenceGroupDelayNs = RfMath.CalculateGroupDelayNs(referenceSweep);
        var selectedFrequencyHz = objectiveMode == SmallAntennaObjectiveMode.SmoothCompression
            ? targetFrequencyHz
            : options.FrequencyMHz.HasValue
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
                propagation = objectiveMode == SmallAntennaObjectiveMode.SmoothCompression
                    ? PropagationAnalysis.Analyze(
                        structure,
                        selectedFrequencyHz,
                        SmallAntennaMode.SourceResistanceOhms,
                        options.SpacingMm * 1e-3,
                        referenceSweep)
                    : PropagationAnalysis.AnalyzeTerminated(
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
        var adiabaticMetrics = CalculateAdiabaticMetrics(propagation, energies);
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
            BestS11Db = bestMatch.InputReturnLossDb,
            BestReturnLossPositiveDb = -bestMatch.InputReturnLossDb,
            BestMatchReflectionCoefficientMagnitude = bestMatch.InputReflectionCoefficient.Magnitude,
            BestMatchReflectedPowerPercent =
                bestMatch.InputReflectionCoefficient.Magnitude * bestMatch.InputReflectionCoefficient.Magnitude * 100.0,
            BestMatchAcceptedPowerPercent =
                (1.0 - bestMatch.InputReflectionCoefficient.Magnitude * bestMatch.InputReflectionCoefficient.Magnitude) * 100.0,
            BestMatchDeliveredPowerPercent = bestMatch.DeliveredPowerWatts / SmallAntennaMode.AvailableSourcePowerWatts * 100.0,
            AverageInputReflectionPercent = sweep.Average(point =>
                point.InputReflectionCoefficient.Magnitude * point.InputReflectionCoefficient.Magnitude) * 100.0,
            FrequencyOfBestMatchMHz = bestMatch.FrequencyHz / 1e6,
            DeliveredPowerDb = bestMatch.DeliveredPowerDb,
            DeliveredPowerAtBestMatchDb = bestMatch.DeliveredPowerDb,
            PeakDeliveredPowerDb = strongestRadiation.DeliveredPowerDb,
            ReferenceBestS11Db = RfMath.ToDbMagnitude(referenceBestS11.S11),
            ReferenceS21AtBestS11Db = RfMath.ToDbMagnitude(referenceBestS11.S21),
            ReferenceTotalPhaseRotationDeg = referencePhase.TotalPhaseRotationDeg,
            ReferenceAverageGroupDelayNs = referencePhase.AverageGroupDelayNs,
            TargetFrequencyMHz = targetFrequencyPoint.FrequencyHz / 1e6,
            TargetFrequencyS11Db = RfMath.ToDbMagnitude(targetFrequencyPoint.S11),
            TargetFrequencyS21Db = RfMath.ToDbMagnitude(targetFrequencyPoint.S21),
            TargetFrequencyPhaseDeg = Math.Abs(referenceUnwrappedPhase[targetFrequencyIndex]) * 180.0 / Math.PI,
            TargetFrequencyGroupDelayNs = referenceGroupDelayNs[targetFrequencyIndex],
            BroadbandReferenceReflectionPercent = referenceSweep.Average(point =>
                point.S11.Magnitude * point.S11.Magnitude) * 100.0,
            RadiatedPowerDb = strongestRadiation.RadiatedPowerDb,
            RadiationEfficiencyPercent = strongestRadiation.RadiationEfficiency * 100.0,
            TotalPhaseShiftDeg = propagation?.Summary.TotalPhaseShiftDeg ?? 0,
            TotalGroupDelayNs = groupDelayNs,
            AverageSlowWaveFactor = slowWave.Count > 0 ? slowWave.Average(cell => cell.SlowWaveFactor) : 0,
            MaxSlowWaveFactor = slowWave.Count > 0 ? slowWave.Max(cell => cell.SlowWaveFactor) : 0,
            SlowWaveFactorPeakCell = slowWave.Count > 0 ? slowWavePeak.Index : -1,
            EnergyConcentrationRatio = totalEnergy > 0 ? energies.Max() / totalEnergy : 0,
            SlowWaveJumpPenalty = adiabaticMetrics.SlowWaveJumpPenalty,
            NormalizedSlowWaveJumpPenalty = adiabaticMetrics.NormalizedSlowWaveJumpPenalty,
            SlowWaveCurvaturePenalty = adiabaticMetrics.SlowWaveCurvaturePenalty,
            MaxSlowWaveJump = adiabaticMetrics.MaxSlowWaveJump,
            LocalPhaseJumpPenalty = adiabaticMetrics.LocalPhaseJumpPenalty,
            MaxLocalPhaseJumpDeg = adiabaticMetrics.MaxLocalPhaseJumpDeg,
            VoltageRippleRatio = adiabaticMetrics.VoltageRippleRatio,
            MaxEnergyFraction = adiabaticMetrics.MaxEnergyFraction,
            EnergyEntropy = adiabaticMetrics.EnergyEntropy,
            EnergyRipplePenalty = adiabaticMetrics.EnergyRipplePenalty,
            AdiabaticQualityScore0To100 = adiabaticMetrics.AdiabaticQualityScore0To100,
            SlowWaveLinearityError = adiabaticMetrics.SlowWaveLinearityError,
            CompressionRegionMonotonicityError = adiabaticMetrics.CompressionRegionMonotonicityError,
            RecoveryRegionSmoothnessError = adiabaticMetrics.RecoveryRegionSmoothnessError,
            SlowWavePeakWidthCells = adiabaticMetrics.SlowWavePeakWidthCells,
            SlowWavePeakCell = adiabaticMetrics.SlowWavePeakCell,
            SlowWaveProfileCorrelation = adiabaticMetrics.SlowWaveProfileCorrelation,
            TargetProfileType = adiabaticMetrics.TargetProfileType,
            TargetFrequencyNetworkDiagnostics = propagation is null
                ? null
                : BuildCliPropagationDiagnostics(propagation),
            SectionClassification = propagation is null
                ? null
                : BuildCliSectionClassification(propagation),
            PhaseSmoothnessError = propagation is null ? 0 : CalculatePhaseSmoothnessError(propagation),
            LcSmoothnessError = CalculateLcSmoothnessError(structure),
            CompressionRatio = compressionRatio,
            OptimizerScore = score.TotalScore,
            ScoreBreakdown = score,
            Structure = structure.ToList()
        };

        metrics.PassFailFlags["finite_metrics"] = metrics.AllFinite();
        metrics.PassFailFlags["reflection_metrics_consistent"] =
            Math.Abs(metrics.BestMatchReflectedPowerPercent
                     - Math.Pow(10.0, metrics.BestReturnLossDb / 10.0) * 100.0) <= 1e-9;
        metrics.PassFailFlags["power_balance_consistent"] =
            Math.Abs(metrics.BestMatchAcceptedPowerPercent - metrics.BestMatchDeliveredPowerPercent) <= 1e-8;
        metrics.PassFailFlags["target_return_loss"] = metrics.BestReturnLossDb <= options.TargetReturnLossDb;
        metrics.PassFailFlags["broadband_sweep_has_matching_point"] =
            metrics.ReferenceBestS11Db <= options.TargetReturnLossDb;
        metrics.PassFailFlags["broadband_sweep_s21_at_best_match_acceptable"] =
            metrics.ReferenceS21AtBestS11Db >= options.MinimumReferenceS21Db;
        metrics.PassFailFlags["target_frequency_match"] = metrics.TargetFrequencyS11Db <= options.TargetReturnLossDb;
        metrics.PassFailFlags["target_frequency_transmission_acceptable"] =
            metrics.TargetFrequencyS21Db >= options.MinimumReferenceS21Db;
        metrics.PassFailFlags["target_frequency_device_quality"] =
            metrics.PassFailFlags["finite_metrics"]
            && metrics.PassFailFlags["target_frequency_match"]
            && metrics.PassFailFlags["target_frequency_transmission_acceptable"];
        metrics.PassFailFlags["broadband_sweep_quality"] =
            metrics.BroadbandReferenceReflectionPercent <= options.MaxAverageInputReflectionPercent;
        metrics.PassFailFlags["propagation_supported"] = propagationSupported;
        metrics.PassFailFlags["slow_wave_bounded"] = metrics.MaxSlowWaveFactor <= options.MaxSlowWaveFactor;
        metrics.PassFailFlags["energy_distributed"] = metrics.EnergyConcentrationRatio <= options.MaxEnergyConcentrationRatio;
        metrics.PassFailFlags["input_coupling_acceptable"] =
            metrics.AverageInputReflectionPercent <= options.MaxAverageInputReflectionPercent;
        metrics.PassFailFlags["internal_medium_quality"] = metrics.PassFailFlags["finite_metrics"]
            && metrics.PassFailFlags["reflection_metrics_consistent"]
            && metrics.PassFailFlags["power_balance_consistent"]
            && metrics.PassFailFlags["propagation_supported"]
            && metrics.PassFailFlags["slow_wave_bounded"]
            && metrics.PassFailFlags["energy_distributed"];
        metrics.PassFailFlags["usable_rf_device"] = metrics.PassFailFlags["internal_medium_quality"]
            && metrics.PassFailFlags["target_return_loss"]
            && metrics.PassFailFlags["input_coupling_acceptable"];
        metrics.PassFailFlags["overall"] = objectiveMode == SmallAntennaObjectiveMode.AntennaMatch
            ? metrics.PassFailFlags["finite_metrics"] && metrics.PassFailFlags["target_return_loss"]
            : metrics.PassFailFlags["usable_rf_device"];
        return metrics;
    }

    private static int RunRegressionTests(string directory)
    {
        var output = ExecuteRegressionTests(directory);
        WriteJson(output);
        return output.Passed ? 0 : 3;
    }

    private static RegressionSuiteResult ExecuteRegressionTests(string directory)
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
            if (testCase.ExpectInternalMediumPass.HasValue
                && metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality") != testCase.ExpectInternalMediumPass.Value)
            {
                failures.Add(
                    $"internal medium expected {testCase.ExpectInternalMediumPass.Value} but was {metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality")}");
            }

            results.Add(new RegressionResult
            {
                Name = testCase.Name ?? Path.GetFileNameWithoutExtension(path),
                File = Path.GetRelativePath(directory, path),
                AnalysisPassed = metrics.PassFailFlags.GetValueOrDefault("overall"),
                AnalysisStatus = metrics.PassFailFlags.GetValueOrDefault("overall") ? "PASS" : "FAIL",
                InternalMediumPassed = metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality"),
                UsableRfDevicePassed = metrics.PassFailFlags.GetValueOrDefault("usable_rf_device"),
                ExpectedOverallPass = testCase.ExpectOverallPass,
                Passed = failures.Count == 0,
                Failures = failures,
                Metrics = metrics
            });
        }

        results.AddRange(WorkflowRegressionTests.Run());
        var output = new RegressionSuiteResult
        {
            TestDirectory = directory,
            Passed = results.Count > 0 && results.All(result => result.Passed),
            Total = results.Count,
            PassedCount = results.Count(result => result.Passed),
            FailedCount = results.Count(result => !result.Passed),
            SummaryLines = results
                .Select(result =>
                    result.Metrics is null ? $"{result.Name}: {(result.Passed ? "PASS" : "FAIL")}" :
                    $"{Path.GetFileNameWithoutExtension(result.File)}: INTERNAL {(result.InternalMediumPassed ? "PASS" : "FAIL")} / DEVICE {(result.UsableRfDevicePassed ? "PASS" : "FAIL")}")
                .ToList(),
            Results = results
        };
        return output;
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

    private static int FindNearestIndex(IReadOnlyList<SweepPoint> sweep, double frequencyHz)
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

    private static AdiabaticMetrics CalculateAdiabaticMetrics(
        PropagationResult? propagation,
        IReadOnlyList<double> energies)
    {
        if (propagation is null)
            return new AdiabaticMetrics();

        var slowWave = propagation.Cells
            .Select(cell => cell.SlowWaveFactor)
            .Where(value => double.IsFinite(value) && value > 0)
            .ToList();
        var localPhase = propagation.Cells
            .Select(cell => Math.Abs(cell.DeltaPhaseDeg))
            .Where(double.IsFinite)
            .ToList();
        var voltages = propagation.Nodes
            .Select(node => node.VoltageMagnitude)
            .Where(value => double.IsFinite(value) && value > 0)
            .ToList();
        var slowWaveJumps = AdjacentAbsoluteDifferences(slowWave);
        var phaseJumps = AdjacentAbsoluteDifferences(localPhase);
        var normalizedSlowWaveJump = 0.0;
        for (var index = 0; index < slowWave.Count - 1; index++)
            normalizedSlowWaveJump += Math.Abs(slowWave[index + 1] - slowWave[index]) / Math.Max(slowWave[index], 1.0);

        var slowWaveCurvature = 0.0;
        for (var index = 1; index < slowWave.Count - 1; index++)
            slowWaveCurvature += Math.Abs(slowWave[index + 1] - 2.0 * slowWave[index] + slowWave[index - 1]);

        var phaseAlternationPenalty = 0.0;
        for (var index = 1; index < localPhase.Count - 1; index++)
        {
            var left = localPhase[index] - localPhase[index - 1];
            var right = localPhase[index + 1] - localPhase[index];
            if (left * right < 0)
                phaseAlternationPenalty += Math.Min(Math.Abs(left), Math.Abs(right));
        }

        var totalEnergy = energies.Sum();
        var fractions = totalEnergy > 0
            ? energies.Select(energy => energy / totalEnergy).ToList()
            : new List<double>();
        var energyEntropy = fractions.Count > 1
            ? -fractions.Where(fraction => fraction > 0).Sum(fraction => fraction * Math.Log(fraction))
              / Math.Log(fractions.Count)
            : 0;
        var energyMean = energies.Count > 0 ? energies.Average() : 0;
        var energyRipple = energyMean > 0
            ? Math.Sqrt(energies.Average(energy => Math.Pow((energy - energyMean) / energyMean, 2)))
            : 0;
        var voltageRipple = voltages.Count > 0 ? voltages.Max() / Math.Max(voltages.Min(), 1e-30) : 0;
        var profileMetrics = CalculateLinearSlowWaveProfileMetrics(propagation);
        var normalizedPenalty = normalizedSlowWaveJump
                                + slowWaveCurvature / Math.Max(slowWave.Sum(), 1.0)
                                + (phaseJumps.Sum() + phaseAlternationPenalty) / 180.0
                                + Math.Max(0, voltageRipple - 1.0)
                                + energyRipple
                                + (1.0 - energyEntropy);

        return new AdiabaticMetrics
        {
            SlowWaveJumpPenalty = slowWaveJumps.Sum(),
            NormalizedSlowWaveJumpPenalty = normalizedSlowWaveJump,
            SlowWaveCurvaturePenalty = slowWaveCurvature,
            MaxSlowWaveJump = slowWaveJumps.Count > 0 ? slowWaveJumps.Max() : 0,
            LocalPhaseJumpPenalty = phaseJumps.Sum() + phaseAlternationPenalty,
            MaxLocalPhaseJumpDeg = phaseJumps.Count > 0 ? phaseJumps.Max() : 0,
            VoltageRippleRatio = voltageRipple,
            MaxEnergyFraction = fractions.Count > 0 ? fractions.Max() : 0,
            EnergyEntropy = energyEntropy,
            EnergyRipplePenalty = energyRipple,
            AdiabaticQualityScore0To100 = 100.0 / (1.0 + normalizedPenalty),
            SlowWaveLinearityError = profileMetrics.SlowWaveLinearityError,
            CompressionRegionMonotonicityError = profileMetrics.CompressionRegionMonotonicityError,
            RecoveryRegionSmoothnessError = profileMetrics.RecoveryRegionSmoothnessError,
            SlowWavePeakWidthCells = profileMetrics.SlowWavePeakWidthCells,
            SlowWavePeakCell = profileMetrics.SlowWavePeakCell,
            SlowWaveProfileCorrelation = profileMetrics.SlowWaveProfileCorrelation,
            TargetProfileType = profileMetrics.TargetProfileType
        };
    }

    private static LinearSlowWaveProfileMetrics CalculateLinearSlowWaveProfileMetrics(PropagationResult propagation)
    {
        const string profileType = "launcher_linear_ramp_broad_peak_linear_recovery";
        var slowWave = propagation.Cells
            .Select(cell => double.IsFinite(cell.SlowWaveFactor) && cell.SlowWaveFactor > 0 ? cell.SlowWaveFactor : 0)
            .ToList();
        if (slowWave.Count == 0)
            return new LinearSlowWaveProfileMetrics { TargetProfileType = profileType };

        var peak = slowWave.Max();
        var peakIndex = slowWave.IndexOf(peak);
        var target = new List<double>(slowWave.Count);
        for (var index = 0; index < slowWave.Count; index++)
        {
            var value = index <= peakIndex
                ? Interpolate(slowWave[0], peak, peakIndex == 0 ? 1.0 : (double)index / peakIndex)
                : Interpolate(peak, slowWave[^1], peakIndex == slowWave.Count - 1
                    ? 1.0
                    : (double)(index - peakIndex) / (slowWave.Count - 1 - peakIndex));
            target.Add(value);
        }

        var scale = Math.Max(peak, 1.0);
        var linearityError = Math.Sqrt(slowWave
            .Zip(target, (actual, ideal) => Math.Pow((actual - ideal) / scale, 2))
            .Average());
        var monotonicityError = 0.0;
        for (var index = 0; index < peakIndex; index++)
            monotonicityError += Math.Max(0, slowWave[index] - slowWave[index + 1]) / scale;

        var recoveryError = 0.0;
        for (var index = peakIndex; index < slowWave.Count - 1; index++)
        {
            var actualDrop = slowWave[index] - slowWave[index + 1];
            var idealDrop = target[index] - target[index + 1];
            recoveryError += Math.Abs(actualDrop - idealDrop) / scale;
        }

        return new LinearSlowWaveProfileMetrics
        {
            SlowWaveLinearityError = linearityError,
            CompressionRegionMonotonicityError = monotonicityError,
            RecoveryRegionSmoothnessError = recoveryError,
            SlowWavePeakWidthCells = slowWave.Count(value => value >= peak * 0.8),
            SlowWavePeakCell = peakIndex,
            SlowWaveProfileCorrelation = CalculateCorrelation(slowWave, target),
            TargetProfileType = profileType
        };
    }

    private static double Interpolate(double start, double stop, double fraction) =>
        start + (stop - start) * Math.Clamp(fraction, 0, 1);

    private static double CalculateCorrelation(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count || left.Count == 0)
            return 0;
        var leftMean = left.Average();
        var rightMean = right.Average();
        var covariance = 0.0;
        var leftVariance = 0.0;
        var rightVariance = 0.0;
        for (var index = 0; index < left.Count; index++)
        {
            var leftDelta = left[index] - leftMean;
            var rightDelta = right[index] - rightMean;
            covariance += leftDelta * rightDelta;
            leftVariance += leftDelta * leftDelta;
            rightVariance += rightDelta * rightDelta;
        }
        var denominator = Math.Sqrt(leftVariance * rightVariance);
        return denominator > 0 ? covariance / denominator : 0;
    }

    private static CliPropagationDiagnostics BuildCliPropagationDiagnostics(PropagationResult propagation)
    {
        var diagnostics = propagation.Diagnostics;
        return new CliPropagationDiagnostics
        {
            InputImpedanceOhms = BuildCliComplex(diagnostics.InputImpedanceOhms),
            OutputImpedanceBeforeLoadOhms = BuildCliComplex(diagnostics.OutputImpedanceBeforeLoadOhms),
            LoadImpedanceOhms = BuildCliComplex(diagnostics.LoadImpedanceOhms),
            InputReflectionCoefficient = BuildCliComplex(diagnostics.InputReflectionCoefficient),
            OutputReflectionCoefficient = BuildCliComplex(diagnostics.OutputReflectionCoefficient),
            InputReflectedPowerPercent = Math.Pow(diagnostics.InputReflectionCoefficient.Magnitude, 2) * 100.0,
            OutputReflectedPowerPercent = Math.Pow(diagnostics.OutputReflectionCoefficient.Magnitude, 2) * 100.0,
            PowerEnteringStructureWatts = diagnostics.PowerEnteringStructureWatts,
            PowerDeliveredToLoadWatts = diagnostics.PowerDeliveredToLoadWatts,
            ReflectedPowerFromInputWatts = diagnostics.ReflectedPowerFromInputWatts,
            ReflectedPowerFromOutputLoadWatts = diagnostics.ReflectedPowerFromOutputLoadWatts,
            Cells = diagnostics.Cells.Select(cell => new CliPropagationCellDiagnostic
            {
                CellIndex = cell.CellIndex,
                EffectiveImpedanceOhms = BuildCliComplex(cell.EffectiveImpedanceOhms),
                DistanceToLoadImpedanceOhms = cell.DistanceToLoadImpedanceOhms,
                SlowWaveFactor = cell.SlowWaveFactor,
                LocalPhaseShiftDeg = cell.LocalPhaseShiftDeg,
                NodeVoltageMagnitude = cell.NodeVoltageMagnitude,
                NodeCurrentMagnitude = cell.NodeCurrentMagnitude
            }).ToList()
        };
    }

    private static CliSectionClassification BuildCliSectionClassification(PropagationResult propagation) =>
        new()
        {
            RecoverySectionLikely = propagation.Classification.RecoverySectionLikely,
            Interpretation = propagation.Classification.Interpretation,
            Sections = propagation.Classification.Sections.Select(section => new CliPropagationSection
            {
                Name = section.Name,
                StartCell = section.StartCell,
                EndCell = section.EndCell
            }).ToList()
        };

    private static CliComplex BuildCliComplex(Complex value) =>
        new()
        {
            Real = value.Real,
            Imaginary = value.Imaginary,
            Magnitude = value.Magnitude,
            PhaseDeg = Math.Atan2(value.Imaginary, value.Real) * 180.0 / Math.PI
        };

    private static List<double> AdjacentAbsoluteDifferences(IReadOnlyList<double> values)
    {
        var differences = new List<double>(Math.Max(0, values.Count - 1));
        for (var index = 0; index < values.Count - 1; index++)
            differences.Add(Math.Abs(values[index + 1] - values[index]));
        return differences;
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
    public string? BatchOptimizeObjective { get; private set; }
    public string? AutoTestDirectory { get; private set; }
    public string? SeedStructurePath { get; private set; }
    public bool ShowHelp { get; private set; }
    public double FrequencyStartMHz { get; private set; } = 5;
    public double FrequencyStopMHz { get; private set; } = 50;
    public double? FrequencyMHz { get; private set; }
    public int Points { get; private set; } = 401;
    public double SpacingMm { get; private set; } = 5;
    public int Iterations { get; private set; } = 100;
    public int IterationsPerStep { get; private set; } = 2000;
    public int ElementCount { get; private set; } = 9;
    public int MaxCells { get; private set; } = 12;
    public int OptimizePreviousCells { get; private set; } = 2;
    public int RejectedSeedDiagnosticCount { get; private set; } = 5;
    public int LauncherCellCount { get; private set; } = 2;
    public int RandomSeed { get; private set; } = 12345;
    public int MutationsPerCandidate { get; private set; } = 1;
    public double MutationLogStep { get; private set; } = 0.35;
    public double LauncherMutationFraction { get; private set; } = 0.65;
    public string SaveBestPath { get; private set; } = "best_candidate.json";
    public string HistoryJsonPath { get; private set; } = "optimizer_history.json";
    public string HistoryCsvPath { get; private set; } = "optimizer_history.csv";
    public double TargetReturnLossDb { get; private set; } = -10;
    public double MaxVoltage { get; private set; } = 500;
    public double MaxSlowWaveFactor { get; private set; } = 100;
    public double MaxEnergyConcentrationRatio { get; private set; } = 0.5;
    public double MaxAverageInputReflectionPercent { get; private set; } = 50;
    public double MinimumSeedDeliveredPowerDb { get; private set; } = -6;
    public double MaxDeliveredPowerCollapseDb { get; private set; } = 1;
    public double MinimumPhaseIncreaseDeg { get; private set; } = 0.01;
    public double MinimumGroupDelayIncreaseNs { get; private set; } = 0.01;
    public double MaxResonancePenalty { get; private set; } = 500;
    public double MaxSlowWaveJump { get; private set; } = 250;
    public double MaxLocalPhaseJumpDeg { get; private set; } = 45;
    public double MaxVoltageRippleRatio { get; private set; } = 10;
    public double MaxAdiabaticQualityDrop { get; private set; } = 10;
    public double RefineMinimumS21Db { get; private set; } = -1;
    public double RefineMinimumRetentionFraction { get; private set; } = 0.9;
    public double RefineMaxResonancePenalty { get; private set; }
    public double LinearSlowWaveMinimumRetentionFraction { get; private set; } = 0.85;
    public double LinearSlowWavePreferredMaxEnergyFraction { get; private set; } = 0.2;
    public double RuntimeLimitSeconds { get; private set; }
    public double MinimumReferenceS21Db { get; private set; } = -3;
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
        rf_sim_cli --mode grow-compression --seed known_good_seed.json --max-cells 12 --target-rl-db -10 --iters-per-step 2000 --output grown_medium.json
        rf_sim_cli --mode refine-adiabatic --input grown_medium.json --target-mhz 27.5 --iters 10000 --output refined_medium.json
        rf_sim_cli --mode refine-linear-slowwave --input refined_medium.json --target-mhz 27.5 --iters 20000 --output refined_linear_medium.json
        rf_sim_cli --optimize compression --iters 10000 --auto-test tests/cli --save-best best.json
        rf_sim_cli --run-tests tests/cli

        Optional batch optimization arguments:
          --seed 12345 --mutations-per-candidate 1 --mutation-log-step 0.35
          --launcher-mutation-fraction 0.65
          --history-json optimizer_history.json --history-csv optimizer_history.csv
          --launcher-cells 2

        Optional antenna arguments:
          --rrad 2 --rloss 1 --xant -300 --target-rl -10 --max-voltage 500
          --continuous --smooth-gradient --l-min-nh 10 --l-max-nh 2000 --c-min-pf 0.5 --c-max-pf 470
          --elements 9 --max-slow-wave 100 --max-energy-concentration 0.5
          --max-average-reflection-percent 50

        Optional grow-compression arguments:
          --max-cells 12 --iters-per-step 2000 --optimize-previous-cells 2
          --min-seed-delivered-power-db -6 --max-delivered-power-collapse-db 1
          --min-phase-increase-deg 0.01 --min-group-delay-increase-ns 0.01
          --max-resonance-penalty 500 --runtime-limit-seconds 0
          --max-slow-wave-jump 250 --max-local-phase-jump-deg 45
          --max-voltage-ripple-ratio 10 --max-adiabatic-quality-drop 10
          --min-reference-s21-db -3
          --rejected-seed-diagnostics 5

        Optional refine-adiabatic arguments:
          --target-mhz 27.5 --target-rl-db -10 --refine-min-s21-db -1
          --refine-min-retention 0.9 --refine-max-resonance-penalty 0

        Optional refine-linear-slowwave arguments:
          --linear-slowwave-min-retention 0.85 --linear-slowwave-preferred-max-energy 0.2
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
            BatchOptimizeObjective = GetNullable(values, "--optimize"),
            AutoTestDirectory = GetNullable(values, "--auto-test"),
            FrequencyStartMHz = GetDouble(values, "--freq-start", 5),
            FrequencyStopMHz = GetDouble(values, "--freq-stop", 50),
            FrequencyMHz = GetNullableDouble(values, "--target-mhz") ?? GetNullableDouble(values, "--freq"),
            Points = GetInt(values, "--points", 401),
            SpacingMm = GetDouble(values, "--spacing-mm", 5),
            Iterations = GetInt(values, "--iters", 100),
            IterationsPerStep = GetInt(values, "--iters-per-step", 2000),
            ElementCount = GetInt(values, "--elements", 9),
            MaxCells = GetInt(values, "--max-cells", 12),
            OptimizePreviousCells = GetInt(values, "--optimize-previous-cells", 2),
            RejectedSeedDiagnosticCount = GetInt(values, "--rejected-seed-diagnostics", 5),
            LauncherCellCount = GetInt(values, "--launcher-cells", 2),
            SeedStructurePath = GetSeedStructurePath(values),
            RandomSeed = GetRandomSeed(values),
            MutationsPerCandidate = GetInt(values, "--mutations-per-candidate", 1),
            MutationLogStep = GetDouble(values, "--mutation-log-step", 0.35),
            LauncherMutationFraction = GetDouble(values, "--launcher-mutation-fraction", 0.65),
            SaveBestPath = Get(values, "--save-best", "best_candidate.json"),
            HistoryJsonPath = Get(values, "--history-json", "optimizer_history.json"),
            HistoryCsvPath = Get(values, "--history-csv", "optimizer_history.csv"),
            TargetReturnLossDb = GetDouble(values, "--target-rl-db", GetDouble(values, "--target-rl", -10)),
            MaxVoltage = GetDouble(values, "--max-voltage", 500),
            MaxSlowWaveFactor = GetDouble(values, "--max-slow-wave", 100),
            MaxEnergyConcentrationRatio = GetDouble(values, "--max-energy-concentration", 0.5),
            MaxAverageInputReflectionPercent = GetDouble(values, "--max-average-reflection-percent", 50),
            MinimumSeedDeliveredPowerDb = GetDouble(values, "--min-seed-delivered-power-db", -6),
            MaxDeliveredPowerCollapseDb = GetDouble(values, "--max-delivered-power-collapse-db", 1),
            MinimumPhaseIncreaseDeg = GetDouble(values, "--min-phase-increase-deg", 0.01),
            MinimumGroupDelayIncreaseNs = GetDouble(values, "--min-group-delay-increase-ns", 0.01),
            MaxResonancePenalty = GetDouble(values, "--max-resonance-penalty", 500),
            MaxSlowWaveJump = GetDouble(values, "--max-slow-wave-jump", 250),
            MaxLocalPhaseJumpDeg = GetDouble(values, "--max-local-phase-jump-deg", 45),
            MaxVoltageRippleRatio = GetDouble(values, "--max-voltage-ripple-ratio", 10),
            MaxAdiabaticQualityDrop = GetDouble(values, "--max-adiabatic-quality-drop", 10),
            RefineMinimumS21Db = GetDouble(values, "--refine-min-s21-db", -1),
            RefineMinimumRetentionFraction = GetDouble(values, "--refine-min-retention", 0.9),
            RefineMaxResonancePenalty = GetDouble(values, "--refine-max-resonance-penalty", 0),
            LinearSlowWaveMinimumRetentionFraction = GetDouble(values, "--linear-slowwave-min-retention", 0.85),
            LinearSlowWavePreferredMaxEnergyFraction = GetDouble(values, "--linear-slowwave-preferred-max-energy", 0.2),
            RuntimeLimitSeconds = GetDouble(values, "--runtime-limit-seconds", 0),
            MinimumReferenceS21Db = GetDouble(values, "--min-reference-s21-db", -3),
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
            MaxAverageInputReflectionPercent = testCase.MaxAverageInputReflectionPercent ?? 50,
            LauncherCellCount = testCase.LauncherCellCount ?? 2,
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
            LauncherCellCount = LauncherCellCount,
            MaxAverageInputReflectionPercent = MaxAverageInputReflectionPercent,
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

    private static int GetRandomSeed(Dictionary<string, string?> values)
    {
        if (values.TryGetValue("--random-seed", out var randomSeed) && randomSeed is not null)
            return int.Parse(randomSeed, CultureInfo.InvariantCulture);
        if (values.TryGetValue("--seed", out var seed) && seed is not null
            && int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 12345;
    }

    private static string? GetSeedStructurePath(Dictionary<string, string?> values)
    {
        if (values.TryGetValue("--seed-structure", out var seedStructure) && seedStructure is not null)
            return seedStructure;
        if (values.TryGetValue("--seed", out var seed) && seed is not null
            && !int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return seed;
        return null;
    }
}

internal sealed class CliMetrics
{
    public string Mode { get; set; } = string.Empty;
    public double BestReturnLossDb { get; set; }
    public double BestS11Db { get; set; }
    public double BestReturnLossPositiveDb { get; set; }
    public double BestMatchReflectionCoefficientMagnitude { get; set; }
    public double BestMatchReflectedPowerPercent { get; set; }
    public double BestMatchAcceptedPowerPercent { get; set; }
    public double BestMatchDeliveredPowerPercent { get; set; }
    public double AverageInputReflectionPercent { get; set; }
    [JsonPropertyName("frequency_of_best_match_mhz")]
    public double FrequencyOfBestMatchMHz { get; set; }
    public double DeliveredPowerDb { get; set; }
    public double DeliveredPowerAtBestMatchDb { get; set; }
    public double PeakDeliveredPowerDb { get; set; }
    [JsonPropertyName("broadband_sweep_best_s11_db")]
    public double ReferenceBestS11Db { get; set; }
    [JsonPropertyName("broadband_sweep_s21_at_best_s11_db")]
    public double ReferenceS21AtBestS11Db { get; set; }
    [JsonPropertyName("broadband_sweep_total_phase_rotation_deg")]
    public double ReferenceTotalPhaseRotationDeg { get; set; }
    [JsonPropertyName("broadband_sweep_average_group_delay_ns")]
    public double ReferenceAverageGroupDelayNs { get; set; }
    [JsonPropertyName("target_frequency_mhz")]
    public double TargetFrequencyMHz { get; set; }
    public double TargetFrequencyS11Db { get; set; }
    public double TargetFrequencyS21Db { get; set; }
    public double TargetFrequencyPhaseDeg { get; set; }
    public double TargetFrequencyGroupDelayNs { get; set; }
    public double BroadbandReferenceReflectionPercent { get; set; }
    public double RadiatedPowerDb { get; set; }
    public double RadiationEfficiencyPercent { get; set; }
    public double TotalPhaseShiftDeg { get; set; }
    public double TotalGroupDelayNs { get; set; }
    public double AverageSlowWaveFactor { get; set; }
    public double MaxSlowWaveFactor { get; set; }
    public int SlowWaveFactorPeakCell { get; set; }
    public double EnergyConcentrationRatio { get; set; }
    public double SlowWaveJumpPenalty { get; set; }
    [JsonPropertyName("normalized_swf_jump_penalty")]
    public double NormalizedSlowWaveJumpPenalty { get; set; }
    public double SlowWaveCurvaturePenalty { get; set; }
    public double MaxSlowWaveJump { get; set; }
    public double LocalPhaseJumpPenalty { get; set; }
    public double MaxLocalPhaseJumpDeg { get; set; }
    public double VoltageRippleRatio { get; set; }
    public double MaxEnergyFraction { get; set; }
    public double EnergyEntropy { get; set; }
    public double EnergyRipplePenalty { get; set; }
    [JsonPropertyName("adiabatic_quality_score_0_to_100")]
    public double AdiabaticQualityScore0To100 { get; set; }
    public double SlowWaveLinearityError { get; set; }
    public double CompressionRegionMonotonicityError { get; set; }
    public double RecoveryRegionSmoothnessError { get; set; }
    public int SlowWavePeakWidthCells { get; set; }
    public int SlowWavePeakCell { get; set; }
    public double SlowWaveProfileCorrelation { get; set; }
    public string TargetProfileType { get; set; } = string.Empty;
    public CliPropagationDiagnostics? TargetFrequencyNetworkDiagnostics { get; set; }
    public CliSectionClassification? SectionClassification { get; set; }
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
            BestReturnLossDb, BestS11Db, BestReturnLossPositiveDb,
            BestMatchReflectionCoefficientMagnitude, BestMatchReflectedPowerPercent,
            BestMatchAcceptedPowerPercent, BestMatchDeliveredPowerPercent,
            AverageInputReflectionPercent, FrequencyOfBestMatchMHz, DeliveredPowerDb,
            DeliveredPowerAtBestMatchDb, PeakDeliveredPowerDb, RadiatedPowerDb,
            ReferenceBestS11Db, ReferenceS21AtBestS11Db, ReferenceTotalPhaseRotationDeg, ReferenceAverageGroupDelayNs,
            TargetFrequencyMHz, TargetFrequencyS11Db, TargetFrequencyS21Db, TargetFrequencyPhaseDeg,
            TargetFrequencyGroupDelayNs, BroadbandReferenceReflectionPercent,
            RadiationEfficiencyPercent, TotalPhaseShiftDeg, TotalGroupDelayNs, AverageSlowWaveFactor,
            MaxSlowWaveFactor, EnergyConcentrationRatio, PhaseSmoothnessError, LcSmoothnessError,
            SlowWaveJumpPenalty, NormalizedSlowWaveJumpPenalty, SlowWaveCurvaturePenalty, MaxSlowWaveJump,
            LocalPhaseJumpPenalty, MaxLocalPhaseJumpDeg, VoltageRippleRatio, MaxEnergyFraction,
            EnergyEntropy, EnergyRipplePenalty, AdiabaticQualityScore0To100,
            SlowWaveLinearityError, CompressionRegionMonotonicityError, RecoveryRegionSmoothnessError,
            SlowWavePeakWidthCells, SlowWavePeakCell, SlowWaveProfileCorrelation,
            CompressionRatio, OptimizerScore
        }.All(double.IsFinite);
    }

    public Dictionary<string, double> ToMetricDictionary()
    {
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["best_return_loss_db"] = BestReturnLossDb,
            ["best_s11_db"] = BestS11Db,
            ["best_return_loss_positive_db"] = BestReturnLossPositiveDb,
            ["best_match_reflection_coefficient_magnitude"] = BestMatchReflectionCoefficientMagnitude,
            ["best_match_reflected_power_percent"] = BestMatchReflectedPowerPercent,
            ["best_match_accepted_power_percent"] = BestMatchAcceptedPowerPercent,
            ["best_match_delivered_power_percent"] = BestMatchDeliveredPowerPercent,
            ["average_input_reflection_percent"] = AverageInputReflectionPercent,
            ["frequency_of_best_match_mhz"] = FrequencyOfBestMatchMHz,
            ["delivered_power_db"] = DeliveredPowerDb,
            ["delivered_power_at_best_match_db"] = DeliveredPowerAtBestMatchDb,
            ["peak_delivered_power_db"] = PeakDeliveredPowerDb,
            ["reference_best_s11_db"] = ReferenceBestS11Db,
            ["reference_s21_at_best_s11_db"] = ReferenceS21AtBestS11Db,
            ["reference_total_phase_rotation_deg"] = ReferenceTotalPhaseRotationDeg,
            ["reference_average_group_delay_ns"] = ReferenceAverageGroupDelayNs,
            ["broadband_sweep_best_s11_db"] = ReferenceBestS11Db,
            ["broadband_sweep_s21_at_best_s11_db"] = ReferenceS21AtBestS11Db,
            ["broadband_sweep_total_phase_rotation_deg"] = ReferenceTotalPhaseRotationDeg,
            ["broadband_sweep_average_group_delay_ns"] = ReferenceAverageGroupDelayNs,
            ["target_frequency_mhz"] = TargetFrequencyMHz,
            ["target_frequency_s11_db"] = TargetFrequencyS11Db,
            ["target_frequency_s21_db"] = TargetFrequencyS21Db,
            ["target_frequency_phase_deg"] = TargetFrequencyPhaseDeg,
            ["target_frequency_group_delay_ns"] = TargetFrequencyGroupDelayNs,
            ["broadband_reference_reflection_percent"] = BroadbandReferenceReflectionPercent,
            ["radiated_power_db"] = RadiatedPowerDb,
            ["radiation_efficiency_percent"] = RadiationEfficiencyPercent,
            ["total_phase_shift_deg"] = TotalPhaseShiftDeg,
            ["total_group_delay_ns"] = TotalGroupDelayNs,
            ["average_slow_wave_factor"] = AverageSlowWaveFactor,
            ["max_slow_wave_factor"] = MaxSlowWaveFactor,
            ["slow_wave_factor_peak_cell"] = SlowWaveFactorPeakCell,
            ["energy_concentration_ratio"] = EnergyConcentrationRatio,
            ["slow_wave_jump_penalty"] = SlowWaveJumpPenalty,
            ["normalized_swf_jump_penalty"] = NormalizedSlowWaveJumpPenalty,
            ["slow_wave_curvature_penalty"] = SlowWaveCurvaturePenalty,
            ["max_slow_wave_jump"] = MaxSlowWaveJump,
            ["local_phase_jump_penalty"] = LocalPhaseJumpPenalty,
            ["max_local_phase_jump_deg"] = MaxLocalPhaseJumpDeg,
            ["voltage_ripple_ratio"] = VoltageRippleRatio,
            ["max_energy_fraction"] = MaxEnergyFraction,
            ["energy_entropy"] = EnergyEntropy,
            ["energy_ripple_penalty"] = EnergyRipplePenalty,
            ["adiabatic_quality_score_0_to_100"] = AdiabaticQualityScore0To100,
            ["slow_wave_linearity_error"] = SlowWaveLinearityError,
            ["compression_region_monotonicity_error"] = CompressionRegionMonotonicityError,
            ["recovery_region_smoothness_error"] = RecoveryRegionSmoothnessError,
            ["slow_wave_peak_width_cells"] = SlowWavePeakWidthCells,
            ["slow_wave_peak_cell"] = SlowWavePeakCell,
            ["slow_wave_profile_correlation"] = SlowWaveProfileCorrelation,
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
    public double? MaxAverageInputReflectionPercent { get; set; }
    public int? LauncherCellCount { get; set; }
    public bool? ExpectOverallPass { get; set; }
    public bool? ExpectInternalMediumPass { get; set; }
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
    public bool InternalMediumPassed { get; set; }
    public bool UsableRfDevicePassed { get; set; }
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

internal sealed record BatchCandidateEvaluation(SmallAntennaCandidate Candidate, CliMetrics Metrics);

internal sealed class BatchHistoryEntry
{
    public int Iteration { get; set; }
    public bool Accepted { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double OptimizerScore { get; set; }
    public bool OverallPassed { get; set; }
    public double BestReturnLossDb { get; set; }
    public double AverageSlowWaveFactor { get; set; }
    public double MaxSlowWaveFactor { get; set; }
    public double EnergyConcentrationRatio { get; set; }
    public double PhaseSmoothnessError { get; set; }
    public double LcSmoothnessError { get; set; }
    public double CompressionRatio { get; set; }
    public double AverageInputReflectionPercent { get; set; }
    public double InputCouplingScore { get; set; }
    public bool UsableRfDevice { get; set; }
    public List<RfElement> Structure { get; set; } = new();
}

internal sealed class BatchOptimizationResult
{
    public string Mode { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? FailureReason { get; set; }
    public int Iterations { get; set; }
    public int RandomSeed { get; set; }
    public int AcceptedCandidates { get; set; }
    public int RejectedCandidates { get; set; }
    public string? SaveBestPath { get; set; }
    public string? HistoryJsonPath { get; set; }
    public string? HistoryCsvPath { get; set; }
    public CliMetrics? BestMetrics { get; set; }
    public CliMetrics? BestInternalMetrics { get; set; }
    public RegressionSuiteResult? RegressionBefore { get; set; }
    public RegressionSuiteResult? RegressionAfter { get; set; }
}

internal sealed class GrowingCompressionResult
{
    public string Mode { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public int AcceptedCells { get; set; }
    public int RejectedStepCount { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string? SeedStructurePath { get; set; }
    public double ElapsedSeconds { get; set; }
    public List<RfElement>? FinalStructure { get; set; }
    public CliMetrics? FinalMetrics { get; set; }
    public bool FinalUsableRfDevice { get; set; }
    public GrowingFinalStatus? FinalStatus { get; set; }
    public string? FinalVerdict { get; set; }
    public List<string> FinalValidationReasons { get; set; } = new();
    public List<string> GrowthReport { get; set; } = new();
    public List<GrowingRejectedSeed> BestRejectedSeeds { get; set; } = new();
    public List<GrowingCompressionStep> Steps { get; set; } = new();
}

internal sealed class GrowingCompressionStep
{
    public int CellCount { get; set; }
    public int RejectedAttemptsBeforeAcceptance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public CliMetrics? Metrics { get; set; }
    public List<RfElement> BestStructureAtLength { get; set; } = new();
}

internal sealed class GrowingRejectedSeed
{
    public double BestReturnLossDb { get; set; }
    public double DeliveredPowerDb { get; set; }
    public double BestMatchReflectionCoefficientMagnitude { get; set; }
    public double BestMatchReflectedPowerPercent { get; set; }
    public double BestMatchAcceptedPowerPercent { get; set; }
    public double BestMatchDeliveredPowerPercent { get; set; }
    public double AverageInputReflectionPercent { get; set; }
    public double ReferenceBestS11Db { get; set; }
    public double ReferenceS21AtBestS11Db { get; set; }
    public double TargetFrequencyS11Db { get; set; }
    public double TargetFrequencyS21Db { get; set; }
    public bool ReflectionMetricsConsistent { get; set; }
    public bool PowerBalanceConsistent { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<RfElement> Structure { get; set; } = new();
}

internal sealed class GrowingFinalStatus
{
    public bool InternalMediumQuality { get; set; }
    public bool TargetFrequencyDeviceQuality { get; set; }
    public bool BroadbandSweepQuality { get; set; }
    public bool FinalOverall { get; set; }
}

internal sealed class AdiabaticMetrics
{
    public double SlowWaveJumpPenalty { get; set; }
    public double NormalizedSlowWaveJumpPenalty { get; set; }
    public double SlowWaveCurvaturePenalty { get; set; }
    public double MaxSlowWaveJump { get; set; }
    public double LocalPhaseJumpPenalty { get; set; }
    public double MaxLocalPhaseJumpDeg { get; set; }
    public double VoltageRippleRatio { get; set; }
    public double MaxEnergyFraction { get; set; }
    public double EnergyEntropy { get; set; }
    public double EnergyRipplePenalty { get; set; }
    public double AdiabaticQualityScore0To100 { get; set; }
    public double SlowWaveLinearityError { get; set; }
    public double CompressionRegionMonotonicityError { get; set; }
    public double RecoveryRegionSmoothnessError { get; set; }
    public int SlowWavePeakWidthCells { get; set; }
    public int SlowWavePeakCell { get; set; }
    public double SlowWaveProfileCorrelation { get; set; }
    public string TargetProfileType { get; set; } = string.Empty;
}

internal sealed class LinearSlowWaveProfileMetrics
{
    public double SlowWaveLinearityError { get; set; }
    public double CompressionRegionMonotonicityError { get; set; }
    public double RecoveryRegionSmoothnessError { get; set; }
    public int SlowWavePeakWidthCells { get; set; }
    public int SlowWavePeakCell { get; set; }
    public double SlowWaveProfileCorrelation { get; set; }
    public string TargetProfileType { get; set; } = string.Empty;
}

internal sealed class CliPropagationDiagnostics
{
    public CliComplex InputImpedanceOhms { get; set; } = new();
    public CliComplex OutputImpedanceBeforeLoadOhms { get; set; } = new();
    public CliComplex LoadImpedanceOhms { get; set; } = new();
    public CliComplex InputReflectionCoefficient { get; set; } = new();
    public CliComplex OutputReflectionCoefficient { get; set; } = new();
    public double InputReflectedPowerPercent { get; set; }
    public double OutputReflectedPowerPercent { get; set; }
    public double PowerEnteringStructureWatts { get; set; }
    public double PowerDeliveredToLoadWatts { get; set; }
    public double ReflectedPowerFromInputWatts { get; set; }
    public double ReflectedPowerFromOutputLoadWatts { get; set; }
    public List<CliPropagationCellDiagnostic> Cells { get; set; } = new();
}

internal sealed class CliPropagationCellDiagnostic
{
    public int CellIndex { get; set; }
    public CliComplex EffectiveImpedanceOhms { get; set; } = new();
    public double DistanceToLoadImpedanceOhms { get; set; }
    public double SlowWaveFactor { get; set; }
    public double LocalPhaseShiftDeg { get; set; }
    public double NodeVoltageMagnitude { get; set; }
    public double NodeCurrentMagnitude { get; set; }
}

internal sealed class CliSectionClassification
{
    public bool RecoverySectionLikely { get; set; }
    public string Interpretation { get; set; } = string.Empty;
    public List<CliPropagationSection> Sections { get; set; } = new();
}

internal sealed class CliPropagationSection
{
    public string Name { get; set; } = string.Empty;
    public int StartCell { get; set; }
    public int EndCell { get; set; }
}

internal sealed class CliComplex
{
    public double Real { get; set; }
    public double Imaginary { get; set; }
    public double Magnitude { get; set; }
    public double PhaseDeg { get; set; }
}

internal sealed class AdiabaticRefinementResult
{
    public string Mode { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }
    public int Iterations { get; set; }
    public int RandomSeed { get; set; }
    public int AcceptedCandidates { get; set; }
    public int RejectedCandidates { get; set; }
    public double InitialAdiabaticScore { get; set; }
    public double FinalAdiabaticScore { get; set; }
    public CliMetrics? InitialMetrics { get; set; }
    public CliMetrics? FinalMetrics { get; set; }
    public List<RfElement> FinalStructure { get; set; } = new();
}
