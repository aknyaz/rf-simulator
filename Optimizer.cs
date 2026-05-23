using System.Numerics;
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
    }

    public class CandidateResult
    {
        public double Score { get; set; }
        public double WorstS11Db { get; set; }
        public double AverageS11Db { get; set; }
        public double WorstS21Db { get; set; }
        public double RippleDb { get; set; }
        public List<RfElement> Structure { get; set; } = new();

        [JsonIgnore]
        public List<SweepPoint> SweepCache { get; set; } = new();
    }

    public static double CalculateScore(
        List<SweepPoint> sweep,
        OptimizerConfig config,
        out double worstS11Db,
        out double avgS11Db,
        out double worstS21Db,
        out double rippleDb)
    {
        worstS11Db = double.NegativeInfinity;
        avgS11Db = 0;
        worstS21Db = double.NegativeInfinity;
        rippleDb = 0;

        if (sweep.Count == 0)
            return double.MaxValue;

        var s11Values = new List<double>();
        var s21Values = new List<double>();

        foreach (var p in sweep)
        {
            var s11Db = 20.0 * Math.Log10(Math.Max(p.S11.Magnitude, 1e-15));
            var s21Db = 20.0 * Math.Log10(Math.Max(p.S21.Magnitude, 1e-15));

            s11Values.Add(s11Db);
            s21Values.Add(s21Db);

            worstS11Db = Math.Max(worstS11Db, s11Db);
            worstS21Db = Math.Max(worstS21Db, s21Db);
        }

        avgS11Db = s11Values.Average();
        var minS21Db = s21Values.Min();
        var maxS21Db = s21Values.Max();
        rippleDb = maxS21Db - minS21Db;

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

        // Non-smooth gradient penalty (prefer smooth component profiles)
        score += 0.5 * CalculateGradientPenalty(new List<RfElement>());

        return score;
    }

    private static double CalculateGradientPenalty(List<RfElement> structure)
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
        var structure = new List<RfElement>();
        var random = new Random();

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
            var score = CalculateScore(sweep, config, out var worstS11, out var avgS11, out var worstS21, out var ripple);

            return new CandidateResult
            {
                Score = score,
                WorstS11Db = worstS11,
                AverageS11Db = avgS11,
                WorstS21Db = worstS21,
                RippleDb = ripple,
                Structure = CloneStructure(structure),
                SweepCache = sweep
            };
        }
        catch
        {
            return new CandidateResult { Score = double.MaxValue, Structure = CloneStructure(structure) };
        }
    }

    public static CandidateResult SequentialLocalSearch(
        List<RfElement> initialStructure,
        Func<List<RfElement>, List<SweepPoint>> simulator,
        OptimizerConfig config,
        Action<string> statusCallback = null)
    {
        var current = CloneStructure(initialStructure);
        var currentResult = EvaluateStructure(current, simulator, config);

        for (int iter = 0; iter < config.MaxIterations; iter++)
        {
            statusCallback?.Invoke($"Sequential search iteration {iter + 1}/{config.MaxIterations}");
            bool improved = false;

            for (int idx = 0; idx < current.Count; idx++)
            {
                var originalType = current[idx].Type;
                var candidates = originalType.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
                    ? AvailableInductors.Cast<object>().ToList()
                    : AvailableCapacitors.Cast<object>().ToList();

                foreach (var candidateValue in candidates)
                {
                    var test = CloneStructure(current);
                    test[idx].Value = (double)candidateValue;

                    var testResult = EvaluateStructure(test, simulator, config);

                    if (testResult.Score < currentResult.Score)
                    {
                        current = test;
                        currentResult = testResult;
                        improved = true;
                        break;
                    }
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

    public static List<CandidateResult> RandomRestartLocalSearch(
        Func<List<RfElement>, List<SweepPoint>> simulator,
        OptimizerConfig config,
        Action<string> statusCallback = null)
    {
        var candidates = new List<CandidateResult>();
        int numInductors = config.StructureLength;

        for (int restart = 0; restart < config.RandomRestarts; restart++)
        {
            statusCallback?.Invoke($"Random restart {restart + 1}/{config.RandomRestarts}");

            var randomStructure = GenerateRandomStructure(numInductors);
            var result = SequentialLocalSearch(randomStructure, simulator, config, statusCallback);
            candidates.Add(result);
        }

        return candidates.OrderBy(c => c.Score).Take(20).ToList();
    }
}

public class RfElement
{
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
}

public readonly record struct SweepPoint(double FrequencyHz, Complex S11, Complex S21);

