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

    private static GrowingCompressionResult RunGrowingCompressionSynthesis(CliOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var optimizerConfig = options.CreateOptimizerConfig(SmallAntennaObjectiveMode.SmoothCompression);
        var random = new Random(options.RandomSeed);
        var steps = new List<GrowingCompressionStep>();
        var growthReport = new List<string>();
        var rejectedSteps = 0;
        var seed = FindGrowingSeed(options, optimizerConfig, stopwatch, ref rejectedSteps, out var rejectedSeedDiagnostics);
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
        steps.Add(CreateGrowingStep(1, current, 0, "Coupled seed accepted."));
        growthReport.Add(FormatGrowingReportLine(1, current.Metrics, "PASS", "coupled seed accepted"));
        if (PassesGrowingFinalValidation(current.Metrics, options))
        {
            if (PassesGrowingFinalValidation(current.Metrics, options))
            {
                SaveGrowingStructure(options.OutputPath, current.Candidate.Structure);
            }
        }

        var stopReason = $"Maximum cell count {options.MaxCells} reached.";
        for (var cellCount = 2; cellCount <= options.MaxCells; cellCount++)
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
                    : FormatGrowingReportLine(cellCount, bestRejected.Metrics, "REJECT", rejectionReason));
                break;
            }

            current = bestStepCandidate;
            steps.Add(CreateGrowingStep(cellCount, current, rejectedAttempts, "Adiabatic cell extension accepted."));
            growthReport.Add(FormatGrowingReportLine(cellCount, current.Metrics, "PASS", "adiabatic cell extension accepted"));
            SaveGrowingStructure(options.OutputPath, current.Candidate.Structure);
        }

        growthReport.Add($"Final accepted structure: {steps.Count} {(steps.Count == 1 ? "cell" : "cells")}.");
        return new GrowingCompressionResult
        {
            Mode = "grow-compression",
            Passed = PassesGrowingFinalValidation(current.Metrics, options),
            AcceptedCells = steps.Count,
            RejectedStepCount = rejectedSteps,
            StopReason = stopReason,
            OutputPath = options.OutputPath,
            ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
            FinalStructure = current.Candidate.Structure,
            FinalMetrics = current.Metrics,
            FinalUsableRfDevice = PassesGrowingFinalValidation(current.Metrics, options),
            FinalValidationReasons = GetGrowingFinalValidationReasons(current.Metrics, options),
            GrowthReport = growthReport,
            BestRejectedSeeds = rejectedSeedDiagnostics,
            Steps = steps
        };
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
                    var rejectionReasons = GetGrowingSeedRejectionReasons(evaluated.Metrics, options);
                    if (rejectionReasons.Count > 0)
                    {
                        rejectedSteps++;
                        rejectedSeeds.Add(new GrowingRejectedSeed
                        {
                            BestReturnLossDb = evaluated.Metrics.BestReturnLossDb,
                            DeliveredPowerDb = evaluated.Metrics.DeliveredPowerDb,
                            BestMatchReflectionCoefficientMagnitude = evaluated.Metrics.BestMatchReflectionCoefficientMagnitude,
                            BestMatchReflectedPowerPercent = evaluated.Metrics.BestMatchReflectedPowerPercent,
                            BestMatchAcceptedPowerPercent = evaluated.Metrics.BestMatchAcceptedPowerPercent,
                            BestMatchDeliveredPowerPercent = evaluated.Metrics.BestMatchDeliveredPowerPercent,
                            AverageInputReflectionPercent = evaluated.Metrics.AverageInputReflectionPercent,
                            ReflectionMetricsConsistent =
                                evaluated.Metrics.PassFailFlags.GetValueOrDefault("reflection_metrics_consistent"),
                            PowerBalanceConsistent =
                                evaluated.Metrics.PassFailFlags.GetValueOrDefault("power_balance_consistent"),
                            Reasons = rejectionReasons,
                            Structure = evaluated.Candidate.Structure
                        });
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

    private static BatchCandidateEvaluation? FindNextGrowingCell(
        BatchCandidateEvaluation current,
        CliOptions options,
        SmallAntennaOptimizerConfig optimizerConfig,
        Random random,
        Stopwatch stopwatch,
        out int rejectedAttempts,
        out BatchCandidateEvaluation? bestRejected,
        out string rejectionReason)
    {
        rejectedAttempts = 0;
        bestRejected = null;
        rejectionReason = "no candidate was evaluated";
        BatchCandidateEvaluation? best = null;
        var appended = OptimizerEngine.CloneStructure(current.Candidate.Structure);
        var startsWithSeries = appended[0].Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase);
        appended.Add(new RfElement
        {
            Type = startsWithSeries ? "SeriesL" : "ShuntC",
            Value = appended[^2].Value
        });
        appended.Add(new RfElement
        {
            Type = startsWithSeries ? "ShuntC" : "SeriesL",
            Value = appended[^2].Value
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
            var rejectionReasons = GetGrowingExtensionRejectionReasons(current.Metrics, evaluated, options);
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
        CliOptions options)
    {
        var metrics = evaluated.Metrics;
        var reasons = new List<string>();
        if (!metrics.PassFailFlags.GetValueOrDefault("target_return_loss"))
            reasons.Add("input return loss missed target");
        if (!metrics.PassFailFlags.GetValueOrDefault("slow_wave_bounded"))
            reasons.Add("slow-wave factor exceeded bound");
        if (!metrics.PassFailFlags.GetValueOrDefault("propagation_supported"))
            reasons.Add("propagation analysis failed");
        if (metrics.DeliveredPowerAtBestMatchDb < previous.DeliveredPowerAtBestMatchDb - options.MaxDeliveredPowerCollapseDb)
            reasons.Add("delivered power collapsed");
        if (Math.Abs(metrics.TotalPhaseShiftDeg) < Math.Abs(previous.TotalPhaseShiftDeg) + options.MinimumPhaseIncreaseDeg)
            reasons.Add("phase delay did not increase");
        if (metrics.TotalGroupDelayNs < previous.TotalGroupDelayNs + options.MinimumGroupDelayIncreaseNs)
            reasons.Add("group delay did not increase");
        if (metrics.ScoreBreakdown.ResonancePenalty > options.MaxResonancePenalty)
            reasons.Add("resonance penalty exceeded limit");
        return reasons;
    }

    private static bool PassesGrowingFinalValidation(CliMetrics metrics, CliOptions options)
    {
        return GetGrowingFinalValidationReasons(metrics, options).Count == 0;
    }

    private static List<string> GetGrowingFinalValidationReasons(CliMetrics metrics, CliOptions options)
    {
        var reasons = new List<string>();
        if (!metrics.PassFailFlags.GetValueOrDefault("internal_medium_quality"))
            reasons.Add("internal medium quality failed");
        if (!metrics.PassFailFlags.GetValueOrDefault("target_return_loss"))
            reasons.Add("input return loss missed target");
        if (!metrics.PassFailFlags.GetValueOrDefault("input_coupling_acceptable"))
            reasons.Add("excessive reflected input power");
        if (metrics.ScoreBreakdown.ResonancePenalty > options.MaxResonancePenalty)
            reasons.Add("resonance penalty exceeded limit");
        if (metrics.EnergyConcentrationRatio > options.MaxEnergyConcentrationRatio)
            reasons.Add("energy localization exceeded limit");
        return reasons;
    }

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

    private static string FormatGrowingReportLine(int cellCount, CliMetrics metrics, string status, string reason)
    {
        return $"{cellCount} {(cellCount == 1 ? "cell" : "cells")}: "
               + $"RL = {metrics.BestReturnLossDb:F2} dB, "
               + $"reflected at best match = {metrics.BestMatchReflectedPowerPercent:F4} %, "
               + $"sweep-average reflected = {metrics.AverageInputReflectionPercent:F2} %, "
               + $"delivered power at best match = {metrics.DeliveredPowerAtBestMatchDb:F2} dB, "
               + $"phase = {metrics.TotalPhaseShiftDeg:F2} deg, "
               + $"group delay = {metrics.TotalGroupDelayNs:F2} ns, "
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
        metrics.PassFailFlags["reflection_metrics_consistent"] =
            Math.Abs(metrics.BestMatchReflectedPowerPercent
                     - Math.Pow(10.0, metrics.BestReturnLossDb / 10.0) * 100.0) <= 1e-9;
        metrics.PassFailFlags["power_balance_consistent"] =
            Math.Abs(metrics.BestMatchAcceptedPowerPercent - metrics.BestMatchDeliveredPowerPercent) <= 1e-8;
        metrics.PassFailFlags["target_return_loss"] = metrics.BestReturnLossDb <= options.TargetReturnLossDb;
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

        var output = new RegressionSuiteResult
        {
            TestDirectory = directory,
            Passed = results.Count > 0 && results.All(result => result.Passed),
            Total = results.Count,
            PassedCount = results.Count(result => result.Passed),
            FailedCount = results.Count(result => !result.Passed),
            SummaryLines = results
                .Select(result =>
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
    public string? BatchOptimizeObjective { get; private set; }
    public string? AutoTestDirectory { get; private set; }
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
    public double RuntimeLimitSeconds { get; private set; }
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
        rf_sim_cli --mode grow-compression --max-cells 12 --target-rl-db -10 --iters-per-step 2000 --output grown_medium.json
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
          --rejected-seed-diagnostics 5
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
            FrequencyMHz = GetNullableDouble(values, "--freq"),
            Points = GetInt(values, "--points", 401),
            SpacingMm = GetDouble(values, "--spacing-mm", 5),
            Iterations = GetInt(values, "--iters", 100),
            IterationsPerStep = GetInt(values, "--iters-per-step", 2000),
            ElementCount = GetInt(values, "--elements", 9),
            MaxCells = GetInt(values, "--max-cells", 12),
            OptimizePreviousCells = GetInt(values, "--optimize-previous-cells", 2),
            RejectedSeedDiagnosticCount = GetInt(values, "--rejected-seed-diagnostics", 5),
            LauncherCellCount = GetInt(values, "--launcher-cells", 2),
            RandomSeed = GetInt(values, "--seed", 12345),
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
            RuntimeLimitSeconds = GetDouble(values, "--runtime-limit-seconds", 0),
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
            BestReturnLossDb, BestS11Db, BestReturnLossPositiveDb,
            BestMatchReflectionCoefficientMagnitude, BestMatchReflectedPowerPercent,
            BestMatchAcceptedPowerPercent, BestMatchDeliveredPowerPercent,
            AverageInputReflectionPercent, FrequencyOfBestMatchMHz, DeliveredPowerDb,
            DeliveredPowerAtBestMatchDb, PeakDeliveredPowerDb, RadiatedPowerDb,
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
    public double ElapsedSeconds { get; set; }
    public List<RfElement>? FinalStructure { get; set; }
    public CliMetrics? FinalMetrics { get; set; }
    public bool FinalUsableRfDevice { get; set; }
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
    public bool ReflectionMetricsConsistent { get; set; }
    public bool PowerBalanceConsistent { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<RfElement> Structure { get; set; } = new();
}
