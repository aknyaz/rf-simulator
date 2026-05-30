using System.Numerics;
using System.Text;

namespace RfSimulator;

public static class PropagationAnalysis
{
    private const double SpeedOfLight = 299_792_458.0;

    public static PropagationResult Analyze(
        IReadOnlyList<RfElement> structure,
        double frequencyHz,
        double z0,
        double cellSpacingMeters,
        IReadOnlyList<SweepPoint> sweep)
    {
        return AnalyzeTerminated(
            structure,
            frequencyHz,
            z0,
            new Complex(z0, 0),
            cellSpacingMeters,
            sweep);
    }

    public static PropagationResult AnalyzeTerminated(
        IReadOnlyList<RfElement> structure,
        double frequencyHz,
        double referenceImpedanceOhms,
        Complex loadImpedance,
        double cellSpacingMeters,
        IReadOnlyList<SweepPoint> sweep)
    {
        if (frequencyHz <= 0)
        {
            throw new InvalidOperationException("Analysis frequency must be positive.");
        }

        if (referenceImpedanceOhms <= 0)
        {
            throw new InvalidOperationException("Z0 must be positive.");
        }

        if (loadImpedance.Magnitude <= 0)
        {
            throw new InvalidOperationException("Load impedance must be non-zero.");
        }

        if (cellSpacingMeters <= 0)
        {
            throw new InvalidOperationException("Cell spacing must be positive.");
        }

        ValidateSupportedLadder(structure);

        var omega = 2.0 * Math.PI * frequencyHz;
        var matrices = structure.Select(e => SectionMatrix(e, omega)).ToList();
        var total = AbcdMatrix.Identity;
        foreach (var matrix in matrices)
        {
            total *= matrix;
        }

        var outputVoltage = Complex.One;
        var outputCurrent = outputVoltage / loadImpedance;
        var inputVoltage = total.A * outputVoltage + total.B * outputCurrent;
        var inputCurrent = total.C * outputVoltage + total.D * outputCurrent;

        var nodeVoltages = new List<Complex> { inputVoltage };
        var inductorCurrents = new List<Complex>();
        var inductorValues = new List<double>();
        var capacitorVoltages = new List<Complex>();
        var capacitorValues = new List<double>();

        var stateVoltage = inputVoltage;
        var stateCurrent = inputCurrent;

        foreach (var element in structure)
        {
            if (IsSeriesL(element))
            {
                var z = Complex.ImaginaryOne * omega * element.Value;
                inductorCurrents.Add(stateCurrent);
                inductorValues.Add(element.Value);
                stateVoltage -= z * stateCurrent;
                nodeVoltages.Add(stateVoltage);
            }
            else if (IsShuntC(element))
            {
                var y = Complex.ImaginaryOne * omega * element.Value;
                capacitorVoltages.Add(stateVoltage);
                capacitorValues.Add(element.Value);
                stateCurrent -= y * stateVoltage;
            }
        }

        var unwrappedPhase = RfMath.UnwrapPhase(nodeVoltages.Select(Phase).ToList());
        var nodes = new List<PropagationNode>(nodeVoltages.Count);
        for (var i = 0; i < nodeVoltages.Count; i++)
        {
            nodes.Add(new PropagationNode(
                i,
                nodeVoltages[i],
                nodeVoltages[i].Magnitude,
                unwrappedPhase[i],
                unwrappedPhase[i] * 180.0 / Math.PI));
        }

        var cells = new List<PropagationCell>(Math.Max(0, nodeVoltages.Count - 1));
        for (var i = 0; i < nodeVoltages.Count - 1; i++)
        {
            var deltaPhaseRad = unwrappedPhase[i + 1] - unwrappedPhase[i];
            var beta = -deltaPhaseRad / cellSpacingMeters;
            var effectiveWavelength = beta > 0 ? 2.0 * Math.PI / beta : double.NaN;
            var phaseVelocity = beta > 0 ? omega / beta : double.NaN;
            var slowWaveFactor = phaseVelocity > 0 ? SpeedOfLight / phaseVelocity : double.NaN;
            var inductorEnergy = 0.5 * inductorValues[i] * Math.Pow(inductorCurrents[i].Magnitude, 2);

            cells.Add(new PropagationCell(
                i,
                deltaPhaseRad,
                deltaPhaseRad * 180.0 / Math.PI,
                beta,
                effectiveWavelength,
                phaseVelocity,
                slowWaveFactor,
                inductorCurrents[i],
                inductorEnergy));
        }

        var capacitors = new List<PropagationCapacitor>(capacitorValues.Count);
        for (var i = 0; i < capacitorValues.Count; i++)
        {
            var energy = 0.5 * capacitorValues[i] * Math.Pow(capacitorVoltages[i].Magnitude, 2);
            capacitors.Add(new PropagationCapacitor(i, capacitorVoltages[i], energy));
        }

        var groupDelayNs = FindNearestGroupDelayNs(sweep, frequencyHz);
        var totalPhaseShiftRad = unwrappedPhase.Count >= 2 ? unwrappedPhase[^1] - unwrappedPhase[0] : 0;
        var validSlowWave = cells.Select(c => c.SlowWaveFactor).Where(double.IsFinite).ToList();
        var physicalLengthMeters = cells.Count * cellSpacingMeters;
        var equivalentFreeSpaceLengthMeters = groupDelayNs > 0 ? SpeedOfLight * groupDelayNs * 1e-9 : 0;
        var compressionRatio = physicalLengthMeters > 0 && equivalentFreeSpaceLengthMeters > 0
            ? equivalentFreeSpaceLengthMeters / physicalLengthMeters
            : 0;

        var summary = new PropagationSummary(
            frequencyHz,
            totalPhaseShiftRad,
            totalPhaseShiftRad * 180.0 / Math.PI,
            groupDelayNs,
            validSlowWave.Count > 0 ? validSlowWave.Average() : 0,
            validSlowWave.Count > 0 ? validSlowWave.Min() : 0,
            validSlowWave.Count > 0 ? validSlowWave.Max() : 0,
            equivalentFreeSpaceLengthMeters,
            physicalLengthMeters,
            compressionRatio);

        return new PropagationResult(nodes, cells, capacitors, summary, BuildWarning(cells));
    }

    public static void ValidateSupportedLadder(IReadOnlyList<RfElement> structure)
    {
        if (structure.Count < 1)
        {
            throw new InvalidOperationException("Structure must contain at least one element.");
        }

        if (!IsSeriesL(structure[0]) && !IsShuntC(structure[0]))
        {
            throw new InvalidOperationException("Propagation analysis supports ladders that start with SeriesL or ShuntC.");
        }

        var startsWithSeries = IsSeriesL(structure[0]);
        for (var i = 0; i < structure.Count; i++)
        {
            var expectedSeries = startsWithSeries ? i % 2 == 0 : i % 2 != 0;
            if (expectedSeries && !IsSeriesL(structure[i]))
            {
                throw new InvalidOperationException("Propagation analysis supports alternating SeriesL / ShuntC ladder structures only.");
            }

            if (!expectedSeries && !IsShuntC(structure[i]))
            {
                throw new InvalidOperationException("Propagation analysis supports alternating SeriesL / ShuntC ladder structures only.");
            }
        }
    }

    public static string FormatSummary(PropagationResult result)
    {
        var s = result.Summary;
        var sb = new StringBuilder();
        sb.AppendLine($"Selected frequency: {s.FrequencyHz / 1e6:F6} MHz");
        sb.AppendLine($"Total phase shift: {s.TotalPhaseShiftDeg:F2} deg");
        sb.AppendLine($"Total group delay: {(s.GroupDelayNs > 0 ? $"{s.GroupDelayNs:F3} ns" : "n/a")}");
        sb.AppendLine($"Average slow-wave factor: {s.AverageSlowWaveFactor:F3}");
        sb.AppendLine($"Slow-wave factor min/max: {s.MinSlowWaveFactor:F3} / {s.MaxSlowWaveFactor:F3}");
        sb.AppendLine($"Equivalent free-space length: {s.EquivalentFreeSpaceLengthMeters:F4} m");
        sb.AppendLine($"Physical structure length: {s.PhysicalLengthMeters * 1e3:F3} mm");
        sb.AppendLine($"Compression ratio: {s.CompressionRatio:F3}");

        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            sb.AppendLine();
            sb.AppendLine(result.Warning);
        }

        return sb.ToString();
    }

    private static double FindNearestGroupDelayNs(IReadOnlyList<SweepPoint> sweep, double frequencyHz)
    {
        if (sweep.Count < 2)
        {
            return 0;
        }

        var delays = RfMath.CalculateGroupDelayNs(sweep);
        var nearestIndex = 0;
        var nearestDelta = double.PositiveInfinity;
        for (var i = 0; i < sweep.Count; i++)
        {
            var delta = Math.Abs(sweep[i].FrequencyHz - frequencyHz);
            if (delta < nearestDelta)
            {
                nearestDelta = delta;
                nearestIndex = i;
            }
        }

        return nearestIndex < delays.Count && double.IsFinite(delays[nearestIndex]) ? delays[nearestIndex] : 0;
    }

    private static string BuildWarning(IReadOnlyList<PropagationCell> cells)
    {
        if (cells.Any(c => !double.IsFinite(c.SlowWaveFactor)))
        {
            return "Warning: Some cells have non-positive phase progression at this frequency, so beta/slow-wave values are not finite.";
        }

        return string.Empty;
    }

    private static double Phase(Complex value) => Math.Atan2(value.Imaginary, value.Real);

    private static bool IsSeriesL(RfElement element) =>
        element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase);

    private static bool IsShuntC(RfElement element) =>
        element.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase);

    private static AbcdMatrix SectionMatrix(RfElement element, double omega)
    {
        if (IsSeriesL(element))
        {
            return AbcdMatrix.SeriesImpedance(Complex.ImaginaryOne * omega * element.Value);
        }

        if (IsShuntC(element))
        {
            return AbcdMatrix.ShuntAdmittance(Complex.ImaginaryOne * omega * element.Value);
        }

        throw new InvalidOperationException($"Unsupported element type: {element.Type}");
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

public sealed record PropagationResult(
    IReadOnlyList<PropagationNode> Nodes,
    IReadOnlyList<PropagationCell> Cells,
    IReadOnlyList<PropagationCapacitor> Capacitors,
    PropagationSummary Summary,
    string Warning);

public readonly record struct PropagationNode(
    int Index,
    Complex Voltage,
    double VoltageMagnitude,
    double PhaseRad,
    double PhaseDeg);

public readonly record struct PropagationCell(
    int Index,
    double DeltaPhaseRad,
    double DeltaPhaseDeg,
    double Beta,
    double EffectiveWavelengthMeters,
    double PhaseVelocityMetersPerSecond,
    double SlowWaveFactor,
    Complex InductorCurrent,
    double InductorEnergyJoules);

public readonly record struct PropagationCapacitor(
    int Index,
    Complex Voltage,
    double CapacitorEnergyJoules);

public readonly record struct PropagationSummary(
    double FrequencyHz,
    double TotalPhaseShiftRad,
    double TotalPhaseShiftDeg,
    double GroupDelayNs,
    double AverageSlowWaveFactor,
    double MinSlowWaveFactor,
    double MaxSlowWaveFactor,
    double EquivalentFreeSpaceLengthMeters,
    double PhysicalLengthMeters,
    double CompressionRatio);
