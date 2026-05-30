using System.Numerics;
using System.Text;

namespace RfSimulator;

public static class SmallAntennaMode
{
    public const double SourceResistanceOhms = 50.0;
    public const double AvailableSourcePowerWatts = 1.0;

    public static List<SmallAntennaPoint> Simulate(
        double startHz,
        double stopHz,
        int points,
        IReadOnlyList<RfElement> structure,
        SmallAntennaConfig config)
    {
        if (points < 2)
        {
            throw new InvalidOperationException("Sweep points must be at least 2.");
        }

        if (stopHz <= startHz)
        {
            throw new InvalidOperationException("Stop frequency must be greater than start frequency.");
        }

        ValidateConfig(config);

        var result = new List<SmallAntennaPoint>(points);
        var step = (stopHz - startHz) / (points - 1);
        for (var i = 0; i < points; i++)
        {
            result.Add(AnalyzeAtFrequency(startHz + i * step, structure, config));
        }

        return result;
    }

    public static SmallAntennaPoint AnalyzeAtFrequency(
        double frequencyHz,
        IReadOnlyList<RfElement> structure,
        SmallAntennaConfig config)
    {
        if (frequencyHz <= 0)
        {
            throw new InvalidOperationException("Frequency must be positive.");
        }

        ValidateConfig(config);

        var load = config.AntennaImpedance;
        if (load.Magnitude <= 0)
        {
            throw new InvalidOperationException("Antenna impedance must be non-zero.");
        }

        var total = BuildAbcd(structure, 2.0 * Math.PI * frequencyHz);
        var zin = (total.A * load + total.B) / (total.C * load + total.D);
        var gamma = (zin - SourceResistanceOhms) / (zin + SourceResistanceOhms);
        var returnLossDb = RfMath.ToDbMagnitude(gamma);

        // RMS convention: source voltage is the Thevenin RMS voltage that gives
        // 1 W available power into a matched 50 ohm load.
        var sourceVoltageRms = Math.Sqrt(4.0 * SourceResistanceOhms * AvailableSourcePowerWatts);
        var inputVoltage = sourceVoltageRms * zin / (SourceResistanceOhms + zin);
        var antennaVoltage = inputVoltage / (total.A + total.B / load);
        var antennaCurrent = antennaVoltage / load;

        var currentSquared = antennaCurrent.Magnitude * antennaCurrent.Magnitude;
        var radiatedPower = currentSquared * config.RadiationResistanceOhms;
        var lossPower = currentSquared * config.LossResistanceOhms;
        var deliveredPower = radiatedPower + lossPower;
        var radiatedPowerRatio = radiatedPower / AvailableSourcePowerWatts;
        var deliveredPowerRatio = deliveredPower / AvailableSourcePowerWatts;
        var radiationEfficiency = deliveredPower > 0 ? radiatedPower / deliveredPower : 0;

        return new SmallAntennaPoint(
            frequencyHz,
            zin,
            gamma,
            returnLossDb,
            antennaVoltage,
            antennaCurrent,
            radiatedPower,
            lossPower,
            deliveredPower,
            radiationEfficiency,
            ToPowerDb(radiatedPowerRatio),
            ToPowerDb(deliveredPowerRatio));
    }

    public static SmallAntennaSummary Summarize(
        IReadOnlyList<SmallAntennaPoint> sweep,
        PropagationResult? propagation,
        double voltageWarningThreshold)
    {
        if (sweep.Count == 0)
        {
            return new SmallAntennaSummary();
        }

        var bestMatch = sweep.OrderBy(p => p.InputReturnLossDb).First();
        var bestRadiated = sweep.OrderByDescending(p => p.RadiatedPowerWatts).First();
        var peakCurrent = sweep.Max(p => p.AntennaCurrent.Magnitude);
        var peakVoltage = sweep.Max(p => p.AntennaVoltage.Magnitude);

        return new SmallAntennaSummary
        {
            BestInputReturnLossDb = bestMatch.InputReturnLossDb,
            BestMatchFrequencyHz = bestMatch.FrequencyHz,
            RadiatedPowerRatio = bestRadiated.RadiatedPowerWatts / AvailableSourcePowerWatts,
            DeliveredPowerRatio = bestRadiated.DeliveredPowerWatts / AvailableSourcePowerWatts,
            RadiationEfficiency = bestRadiated.RadiationEfficiency,
            PeakAntennaCurrentAmps = peakCurrent,
            PeakAntennaVoltageVolts = peakVoltage,
            TotalPhaseAccumulationDeg = propagation?.Summary.TotalPhaseShiftDeg ?? 0,
            AverageSlowWaveFactor = propagation?.Summary.AverageSlowWaveFactor ?? 0,
            VoltageWarning = voltageWarningThreshold > 0 && peakVoltage > voltageWarningThreshold
                ? $"Warning: peak antenna voltage {peakVoltage:F2} V exceeds threshold {voltageWarningThreshold:F2} V."
                : string.Empty
        };
    }

    public static double Score(
        IReadOnlyList<SmallAntennaPoint> sweep,
        IReadOnlyList<RfElement> structure,
        SmallAntennaOptimizerConfig config)
    {
        if (sweep.Count == 0)
        {
            return double.MaxValue;
        }

        var bestReturnLoss = sweep.Min(p => p.InputReturnLossDb);
        var averageRadiatedPowerRatio = sweep.Average(p => p.RadiatedPowerWatts / AvailableSourcePowerWatts);
        var bestRadiatedPowerRatio = sweep.Max(p => p.RadiatedPowerWatts / AvailableSourcePowerWatts);
        var averageLossPowerRatio = sweep.Average(p => p.LossPowerWatts / AvailableSourcePowerWatts);
        var peakVoltage = sweep.Max(p => p.AntennaVoltage.Magnitude);
        var radiatedDbValues = sweep.Select(p => p.RadiatedPowerDb).Where(double.IsFinite).ToList();
        var spikePenalty = radiatedDbValues.Count > 1
            ? Math.Max(0, radiatedDbValues.Max() - radiatedDbValues.Average() - 8.0)
            : 0;

        var score = 0.0;
        if (bestReturnLoss > config.TargetInputReturnLossDb)
        {
            var miss = bestReturnLoss - config.TargetInputReturnLossDb;
            score += 20.0 * miss * miss;
        }

        score -= 80.0 * Math.Log10(1.0 + 100.0 * bestRadiatedPowerRatio);
        score -= 25.0 * Math.Log10(1.0 + 100.0 * averageRadiatedPowerRatio);
        score += 35.0 * averageLossPowerRatio;

        if (config.MaxAntennaVoltage > 0 && peakVoltage > config.MaxAntennaVoltage)
        {
            var ratio = peakVoltage / config.MaxAntennaVoltage;
            score += 20.0 * ratio * ratio;
        }

        score += 3.0 * spikePenalty * spikePenalty;
        score += 0.35 * CalculateGradientPenalty(structure);
        return score;
    }

    public static SmallAntennaCandidate Optimize(
        IReadOnlyList<RfElement> initialStructure,
        SmallAntennaOptimizerConfig config,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        var current = OptimizerEngine.CloneStructure(initialStructure);
        var currentResult = Evaluate(current, config);

        for (var iter = 0; iter < config.MaxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statusCallback?.Invoke($"Antenna optimization iteration {iter + 1}/{config.MaxIterations}");

            var improved = false;
            for (var elementIndex = 0; elementIndex < current.Count; elementIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidateValues = current[elementIndex].Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase)
                    ? OptimizerEngine.AvailableInductors
                    : OptimizerEngine.AvailableCapacitors;

                foreach (var value in candidateValues)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var test = OptimizerEngine.CloneStructure(current);
                    test[elementIndex].Value = value;
                    var testResult = Evaluate(test, config);
                    if (testResult.Score < currentResult.Score)
                    {
                        current = test;
                        currentResult = testResult;
                        improved = true;
                        break;
                    }
                }

                if (improved)
                {
                    break;
                }
            }

            if (!improved)
            {
                statusCallback?.Invoke("No antenna-mode improvement found. Stopping.");
                break;
            }
        }

        return currentResult;
    }

    public static SmallAntennaCandidate Evaluate(List<RfElement> structure, SmallAntennaOptimizerConfig config)
    {
        var sweep = Simulate(config.StartHz, config.StopHz, config.Points, structure, config.AntennaConfig);
        var score = Score(sweep, structure, config);
        var summary = Summarize(sweep, null, config.MaxAntennaVoltage);
        return new SmallAntennaCandidate
        {
            Score = score,
            Structure = OptimizerEngine.CloneStructure(structure),
            Sweep = sweep,
            Summary = summary
        };
    }

    public static string FormatSummary(SmallAntennaSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Best input return loss: {summary.BestInputReturnLossDb:F2} dB");
        sb.AppendLine($"Frequency of best match: {summary.BestMatchFrequencyHz / 1e6:F6} MHz");
        sb.AppendLine($"Radiated power ratio: {summary.RadiatedPowerRatio:F6} ({ToPowerDb(summary.RadiatedPowerRatio):F2} dB)");
        sb.AppendLine($"Delivered power ratio: {summary.DeliveredPowerRatio:F6} ({ToPowerDb(summary.DeliveredPowerRatio):F2} dB)");
        sb.AppendLine($"Radiation efficiency: {summary.RadiationEfficiency * 100.0:F2} %");
        sb.AppendLine($"Peak antenna current: {summary.PeakAntennaCurrentAmps:F6} A RMS");
        sb.AppendLine($"Peak antenna voltage: {summary.PeakAntennaVoltageVolts:F3} V RMS");
        sb.AppendLine($"Total phase accumulation: {summary.TotalPhaseAccumulationDeg:F2} deg");
        sb.AppendLine($"Average slow-wave factor: {summary.AverageSlowWaveFactor:F3}");
        sb.AppendLine("Power convention: RMS phasors, 1 W available from 50 ohm source.");

        if (!string.IsNullOrWhiteSpace(summary.VoltageWarning))
        {
            sb.AppendLine();
            sb.AppendLine(summary.VoltageWarning);
        }

        return sb.ToString();
    }

    public static Complex AntennaImpedance(double radiationResistanceOhms, double lossResistanceOhms, double reactanceOhms)
    {
        return new Complex(radiationResistanceOhms + lossResistanceOhms, reactanceOhms);
    }

    private static void ValidateConfig(SmallAntennaConfig config)
    {
        if (config.RadiationResistanceOhms < 0)
        {
            throw new InvalidOperationException("Rrad cannot be negative.");
        }

        if (config.LossResistanceOhms < 0)
        {
            throw new InvalidOperationException("Rloss cannot be negative.");
        }

        if (config.RadiationResistanceOhms + config.LossResistanceOhms <= 0)
        {
            throw new InvalidOperationException("Rrad + Rloss must be positive.");
        }
    }

    private static double ToPowerDb(double ratio)
    {
        return 10.0 * Math.Log10(Math.Max(ratio, 1e-30));
    }

    private static AbcdMatrix BuildAbcd(IReadOnlyList<RfElement> structure, double omega)
    {
        var total = AbcdMatrix.Identity;
        foreach (var element in structure)
        {
            var section = element.Type.Trim().ToLowerInvariant() switch
            {
                "seriesl" => AbcdMatrix.SeriesImpedance(Complex.ImaginaryOne * omega * element.Value),
                "shuntc" => AbcdMatrix.ShuntAdmittance(Complex.ImaginaryOne * omega * element.Value),
                _ => throw new InvalidOperationException($"Unsupported element type: {element.Type}")
            };

            total *= section;
        }

        return total;
    }

    private static double CalculateGradientPenalty(IReadOnlyList<RfElement> structure)
    {
        var values = structure.Select(e => e.Value).ToList();
        if (values.Count < 2)
        {
            return 0;
        }

        var penalty = 0.0;
        for (var i = 0; i < values.Count - 1; i++)
        {
            penalty += Math.Pow(Math.Log(values[i + 1] / values[i]), 2);
        }

        return penalty;
    }

    private readonly record struct AbcdMatrix(Complex A, Complex B, Complex C, Complex D)
    {
        public static AbcdMatrix Identity => new(1, 0, 0, 1);

        public static AbcdMatrix SeriesImpedance(Complex z) => new(1, z, 0, 1);

        public static AbcdMatrix ShuntAdmittance(Complex y) => new(1, 0, y, 1);

        public static AbcdMatrix operator *(AbcdMatrix left, AbcdMatrix right)
        {
            return new AbcdMatrix(
                left.A * right.A + left.B * right.C,
                left.A * right.B + left.B * right.D,
                left.C * right.A + left.D * right.C,
                left.C * right.B + left.D * right.D);
        }
    }
}

public sealed class SmallAntennaConfig
{
    public double RadiationResistanceOhms { get; set; } = 2;
    public double LossResistanceOhms { get; set; } = 1;
    public double ReactanceOhms { get; set; } = -300;

    public Complex AntennaImpedance =>
        SmallAntennaMode.AntennaImpedance(RadiationResistanceOhms, LossResistanceOhms, ReactanceOhms);
}

public sealed class SmallAntennaOptimizerConfig
{
    public double StartHz { get; set; } = 5e6;
    public double StopHz { get; set; } = 50e6;
    public int Points { get; set; } = 401;
    public double TargetInputReturnLossDb { get; set; } = -10;
    public double MaxAntennaVoltage { get; set; } = 500;
    public int MaxIterations { get; set; } = 35;
    public SmallAntennaConfig AntennaConfig { get; set; } = new();
}

public readonly record struct SmallAntennaPoint(
    double FrequencyHz,
    Complex InputImpedance,
    Complex InputReflectionCoefficient,
    double InputReturnLossDb,
    Complex AntennaVoltage,
    Complex AntennaCurrent,
    double RadiatedPowerWatts,
    double LossPowerWatts,
    double DeliveredPowerWatts,
    double RadiationEfficiency,
    double RadiatedPowerDb,
    double DeliveredPowerDb);

public sealed class SmallAntennaSummary
{
    public double BestInputReturnLossDb { get; set; }
    public double BestMatchFrequencyHz { get; set; }
    public double RadiatedPowerRatio { get; set; }
    public double DeliveredPowerRatio { get; set; }
    public double RadiationEfficiency { get; set; }
    public double PeakAntennaCurrentAmps { get; set; }
    public double PeakAntennaVoltageVolts { get; set; }
    public double TotalPhaseAccumulationDeg { get; set; }
    public double AverageSlowWaveFactor { get; set; }
    public string VoltageWarning { get; set; } = string.Empty;
}

public sealed class SmallAntennaCandidate
{
    public double Score { get; set; }
    public List<RfElement> Structure { get; set; } = new();
    public List<SmallAntennaPoint> Sweep { get; set; } = new();
    public SmallAntennaSummary Summary { get; set; } = new();
}
