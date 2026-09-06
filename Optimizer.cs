using System.Text.Json.Serialization;

namespace RfSimulator;

public class OptimizerEngine
{
    public static readonly double[] AvailableInductors =
    {
        10e-9, 47e-9, 68e-9, 100e-9, 220e-9, 330e-9, 470e-9, 680e-9, 820e-9
    };

    public static readonly double[] AvailableCapacitors =
    {
        0.5e-12, 15e-12, 18e-12, 20e-12, 22e-12, 47e-12, 51e-12, 56e-12, 62e-12,
        68e-12, 75e-12, 82e-12, 91e-12, 100e-12, 110e-12, 120e-12, 130e-12, 150e-12,
        180e-12, 200e-12, 220e-12, 330e-12, 470e-12
    };

    public class OptimizerConfig
    {
        public double StartHz { get; set; } = 5e6;
        public double StopHz { get; set; } = 50e6;
        public int Points { get; set; } = 401;
        public double Z0 { get; set; } = 50;
        public double TargetS11Db { get; set; } = -15;
        public double MinAcceptableS21Db { get; set; } = -3;
        public int StructureLength { get; set; } = 3; // 3, 5, 7, or 9 inductors
        public int MaxIterations { get; set; } = 50;
        public int RandomRestarts { get; set; } = 100;
        public bool EnablePhaseObjective { get; set; } = true;
        public bool EnableGroupDelayObjective { get; set; } = true;
        public double TargetPhaseRotationDeg { get; set; } = 180;
        public double PhaseObjectiveWeight { get; set; } = 1;
        public double GroupDelayObjectiveWeight { get; set; } = 1;
        public int RandomSeed { get; set; } = 12345;
        public int[] LockedIndices { get; set; } = Array.Empty<int>();
        public List<RfElement>? InitialStructure { get; set; }
        public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);
    }

    public class CandidateResult
    {
        public double Score { get; set; }
        public double WorstS11Db { get; set; }
        public double AverageS11Db { get; set; }
        public double WorstS21Db { get; set; }
        public double RippleDb { get; set; }
        public double TotalPhaseRotationDeg { get; set; }
        public double AverageGroupDelayNs { get; set; }
        public double MaxGroupDelayNs { get; set; }
        public int RestartIndex { get; set; }
        public List<string> ConstraintViolations { get; set; } = new();
        public bool Feasible => Score < double.MaxValue && double.IsFinite(Score) && ConstraintViolations.Count == 0;
        public List<RfElement> Structure { get; set; } = new();

        [JsonIgnore]
        public List<SweepPoint> SweepCache { get; set; } = new();
    }

    public static double CalculateScore(
        List<SweepPoint> sweep,
        OptimizerConfig config,
        IReadOnlyList<RfElement> structure,
        out double worstS11Db,
        out double avgS11Db,
        out double worstS21Db,
        out double rippleDb,
        out double totalPhaseRotationDeg,
        out double averageGroupDelayNs,
        out double maxGroupDelayNs)
    {
        worstS11Db = double.NegativeInfinity;
        avgS11Db = 0;
        worstS21Db = double.PositiveInfinity;
        rippleDb = 0;
        totalPhaseRotationDeg = 0;
        averageGroupDelayNs = 0;
        maxGroupDelayNs = 0;

        if (sweep.Count == 0)
            return double.MaxValue;

        var s11Values = new List<double>();
        var s21Values = new List<double>();

        foreach (var p in sweep)
        {
            var s11Db = RfMath.ToDbMagnitude(p.S11);
            var s21Db = RfMath.ToDbMagnitude(p.S21);

            s11Values.Add(s11Db);
            s21Values.Add(s21Db);

            worstS11Db = Math.Max(worstS11Db, s11Db);
            worstS21Db = Math.Min(worstS21Db, s21Db);
        }

        avgS11Db = s11Values.Average();
        var minS21Db = s21Values.Min();
        var maxS21Db = s21Values.Max();
        rippleDb = maxS21Db - minS21Db;
        var phaseMetrics = RfMath.CalculatePhaseMetrics(sweep);
        totalPhaseRotationDeg = phaseMetrics.TotalPhaseRotationDeg;
        averageGroupDelayNs = phaseMetrics.AverageGroupDelayNs;
        maxGroupDelayNs = phaseMetrics.MaxGroupDelayNs;

        double score = 0;

        // Worst S11 penalty
        if (worstS11Db > config.TargetS11Db)
        {
            var penalty = worstS11Db - config.TargetS11Db;
            score += 10.0 * penalty * penalty;
        }

        // Average S11 penalty
        if (avgS11Db > config.TargetS11Db)
        {
            var penalty = avgS11Db - config.TargetS11Db;
            score += 5.0 * penalty * penalty;
        }

        // Worst S21 penalty (S21 lower than acceptable is worse)
        if (worstS21Db < config.MinAcceptableS21Db)
        {
            var penalty = config.MinAcceptableS21Db - worstS21Db;
            score += 5.0 * penalty * penalty;
        }

        // Ripple penalty
        score += 1.0 * rippleDb * rippleDb;

        if (config.EnablePhaseObjective && totalPhaseRotationDeg < config.TargetPhaseRotationDeg)
        {
            var phaseError = (config.TargetPhaseRotationDeg - totalPhaseRotationDeg) / 90.0;
            score += config.PhaseObjectiveWeight * 25.0 * phaseError * phaseError;
        }

        var withinSParameterLimits =
            worstS11Db <= config.TargetS11Db &&
            worstS21Db >= config.MinAcceptableS21Db;

        if (config.EnableGroupDelayObjective && withinSParameterLimits && averageGroupDelayNs > 0)
        {
            score -= config.GroupDelayObjectiveWeight * Math.Log(1.0 + averageGroupDelayNs);
        }

        // Non-smooth gradient penalty (prefer smooth component profiles)
        score += 0.5 * CalculateGradientPenalty(structure);

        return score;
    }

    private static double CalculateGradientPenalty(IReadOnlyList<RfElement> structure)
    {
        if (structure.Count < 2)
            return 0;

        double penalty = 0;
        var inductors = structure.Where(e => e.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)).ToList();
        var capacitors = structure.Where(e => e.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase)).ToList();

        for (int i = 0; i < inductors.Count - 1; i++)
        {
            var diff = Math.Abs(Math.Log(inductors[i + 1].Value / inductors[i].Value));
            penalty += diff * diff;
        }

        for (int i = 0; i < capacitors.Count - 1; i++)
        {
            var diff = Math.Abs(Math.Log(capacitors[i + 1].Value / capacitors[i].Value));
            penalty += diff * diff;
        }

        return penalty;
    }

    public static List<RfElement> GenerateRandomStructure(int numInductors)
    {
        return GenerateRandomStructure(numInductors, new Random());
    }

    private static List<RfElement> GenerateRandomStructure(int numInductors, Random random)
    {
        var structure = new List<RfElement>();

        for (int i = 0; i < numInductors; i++)
        {
            var inductorValue = AvailableInductors[random.Next(AvailableInductors.Length)];
            structure.Add(new RfElement { Type = "SeriesL", Value = inductorValue });

            if (i < numInductors - 1)
            {
                var capacitorValue = AvailableCapacitors[random.Next(AvailableCapacitors.Length)];
                structure.Add(new RfElement { Type = "ShuntC", Value = capacitorValue });
            }
        }

        return structure;
    }

    public static List<RfElement> CloneStructure(List<RfElement> structure)
    {
        return structure.Select(e => new RfElement { Type = e.Type, Value = e.Value }).ToList();
    }

    public static CandidateResult EvaluateStructure(
        List<RfElement> structure,
        Func<List<RfElement>, List<SweepPoint>> simulator,
        OptimizerConfig config)
    {
        try
        {
            var sweep = simulator(structure);
            var score = CalculateScore(
                sweep,
                config,
                structure,
                out var worstS11,
                out var avgS11,
                out var worstS21,
                out var ripple,
                out var totalPhaseRotation,
                out var averageGroupDelay,
                out var maxGroupDelay);

            var violations = new List<string>();
            if (!double.IsFinite(score) || sweep.Any(p => !double.IsFinite(p.S11.Magnitude) || !double.IsFinite(p.S21.Magnitude)))
            {
                violations.Add("Non-finite circuit response");
                score = double.MaxValue;
            }
            if (worstS11 > config.TargetS11Db) violations.Add($"Worst S11 {worstS11:F2} dB exceeds {config.TargetS11Db:F2} dB");
            if (worstS21 < config.MinAcceptableS21Db) violations.Add($"Worst S21 {worstS21:F2} dB is below {config.MinAcceptableS21Db:F2} dB");
            if (config.EnablePhaseObjective && totalPhaseRotation < config.TargetPhaseRotationDeg)
                violations.Add($"Phase rotation {totalPhaseRotation:F2} deg is below {config.TargetPhaseRotationDeg:F2} deg");
            return new CandidateResult
            {
                ConstraintViolations = violations,
                Score = score,
                WorstS11Db = worstS11,
                AverageS11Db = avgS11,
                WorstS21Db = worstS21,
                RippleDb = ripple,
                TotalPhaseRotationDeg = totalPhaseRotation,
                AverageGroupDelayNs = averageGroupDelay,
                MaxGroupDelayNs = maxGroupDelay,
                Structure = CloneStructure(structure),
                SweepCache = sweep
            };
        }
        catch
        {
            return new CandidateResult { Score = double.MaxValue, Structure = CloneStructure(structure), ConstraintViolations = new() { "Circuit evaluation failed" } };
        }
    }

    public static CandidateResult SequentialLocalSearch(
        List<RfElement> initialStructure,
        Func<List<RfElement>, List<SweepPoint>> simulator,
        OptimizerConfig config,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default,
        bool useParallelCandidateEvaluation = true)
    {
        var current = CloneStructure(initialStructure);
        var currentResult = EvaluateStructure(current, simulator, config);

        for (int iter = 0; iter < config.MaxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statusCallback?.Invoke($"Sequential search iteration {iter + 1}/{config.MaxIterations}");
            bool improved = false;

            for (int idx = 0; idx < current.Count; idx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var originalType = current[idx].Type;
                if (config.LockedIndices.Contains(idx)) continue;
                // Preserve unsupported types; never treat a resistor or series capacitor as a shunt capacitor.
                if (!originalType.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
                    && !originalType.Equals("ShuntC", StringComparison.OrdinalIgnoreCase)) continue;
                var candidateValues = originalType.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
                    ? AvailableInductors
                    : AvailableCapacitors;

                var improvement = useParallelCandidateEvaluation
                    ? FindFirstParallelImprovement(current, idx, candidateValues, currentResult.Score, simulator, config, cancellationToken)
                    : FindFirstSequentialImprovement(current, idx, candidateValues, currentResult.Score, simulator, config, cancellationToken);

                if (improvement.HasValue)
                {
                    current = improvement.Value.Structure;
                    currentResult = improvement.Value.Result;
                    improved = true;
                }

                if (improved)
                    break;
            }

            if (!improved)
            {
                statusCallback?.Invoke($"No improvement after iteration {iter + 1}. Stopping.");
                break;
            }
        }

        return currentResult;
    }

    private static (List<RfElement> Structure, CandidateResult Result)? FindFirstSequentialImprovement(
        IReadOnlyList<RfElement> current,
        int elementIndex,
        IReadOnlyList<double> candidateValues,
        double currentScore,
        Func<List<RfElement>, List<SweepPoint>> simulator,
        OptimizerConfig config,
        CancellationToken cancellationToken)
    {
        foreach (var candidateValue in candidateValues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var test = CloneStructure(current);
            test[elementIndex].Value = candidateValue;

            var testResult = EvaluateStructure(test, simulator, config);
            if (testResult.Score < currentScore)
            {
                return (test, testResult);
            }
        }

        return null;
    }

    private static (List<RfElement> Structure, CandidateResult Result)? FindFirstParallelImprovement(
        IReadOnlyList<RfElement> current,
        int elementIndex,
        IReadOnlyList<double> candidateValues,
        double currentScore,
        Func<List<RfElement>, List<SweepPoint>> simulator,
        OptimizerConfig config,
        CancellationToken cancellationToken)
    {
        var results = new (List<RfElement> Structure, CandidateResult Result)?[candidateValues.Count];
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, config.MaxDegreeOfParallelism)
        };

        Parallel.For(0, candidateValues.Count, parallelOptions, candidateIndex =>
        {
            var test = CloneStructure(current);
            test[elementIndex].Value = candidateValues[candidateIndex];

            var testResult = EvaluateStructure(test, simulator, config);
            if (testResult.Score < currentScore)
            {
                results[candidateIndex] = (test, testResult);
            }
        });

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].HasValue)
            {
                return results[i];
            }
        }

        return null;
    }

    public static List<RfElement> CloneStructure(IReadOnlyList<RfElement> structure)
    {
        return structure.Select(e => new RfElement { Type = e.Type, Value = e.Value }).ToList();
    }

    public static List<CandidateResult> RandomRestartLocalSearch(
        Func<List<RfElement>, List<SweepPoint>> simulator,
        OptimizerConfig config,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        int numInductors = config.StructureLength;
        var randomStructures = GenerateRestartStructures(numInductors, config.RandomRestarts, config.RandomSeed);
        if (config.InitialStructure is { } initial)
        {
            var random = new Random(config.RandomSeed);
            randomStructures = Enumerable.Range(0, config.RandomRestarts).Select(_ =>
            {
                var structure = CloneStructure(initial);
                for (var i = 0; i < structure.Count; i++)
                {
                    if (config.LockedIndices.Contains(i)) continue;
                    var values = structure[i].Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase) ? AvailableInductors
                        : structure[i].Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase) ? AvailableCapacitors : null;
                    if (values is not null) structure[i].Value = values[random.Next(values.Length)];
                }
                return structure;
            }).ToList();
        }
        var results = new CandidateResult?[config.RandomRestarts];
        var completed = 0;
        var lastStatusTick = 0L;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, config.MaxDegreeOfParallelism)
        };

        Parallel.For(0, config.RandomRestarts, parallelOptions, restart =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = SequentialLocalSearch(
                randomStructures[restart],
                simulator,
                config,
                statusCallback: null,
                cancellationToken,
                useParallelCandidateEvaluation: false);
            result.RestartIndex = restart;
            results[restart] = result;

            var done = Interlocked.Increment(ref completed);
            var now = Environment.TickCount64;
            var previous = Interlocked.Read(ref lastStatusTick);
            if (done == config.RandomRestarts || now - previous >= 500)
            {
                if (Interlocked.CompareExchange(ref lastStatusTick, now, previous) == previous)
                {
                    statusCallback?.Invoke($"Random restart progress: {done}/{config.RandomRestarts}");
                }
            }
        });

        var candidates = results
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        return candidates
            .OrderBy(c => c.Score)
            .ThenBy(c => c.RestartIndex)
            .Take(20)
            .ToList();
    }

    private static List<List<RfElement>> GenerateRestartStructures(int numInductors, int restarts, int randomSeed)
    {
        var random = new Random(randomSeed);
        var structures = new List<List<RfElement>>(restarts);

        for (int i = 0; i < restarts; i++)
        {
            structures.Add(GenerateRandomStructure(numInductors, random));
        }

        return structures;
    }
}

