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
        var nodeCurrents = new List<Complex> { inputCurrent };
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
                nodeCurrents.Add(stateCurrent);
            }
            else if (IsShuntC(element))
            {
                var y = Complex.ImaginaryOne * omega * element.Value;
                capacitorVoltages.Add(stateVoltage);
                capacitorValues.Add(element.Value);
                stateCurrent -= y * stateVoltage;
                nodeCurrents[^1] = stateCurrent;
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
            var effectiveImpedance = nodeVoltages[i] / inductorCurrents[i];

            cells.Add(new PropagationCell(
                i,
                deltaPhaseRad,
                deltaPhaseRad * 180.0 / Math.PI,
                beta,
                effectiveWavelength,
                phaseVelocity,
                slowWaveFactor,
                inductorCurrents[i],
                inductorEnergy,
                effectiveImpedance));
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

        var inputImpedance = inputVoltage / inputCurrent;
        var outputImpedanceBeforeLoad = outputVoltage / outputCurrent;
        var inputReflection = ReflectionCoefficient(inputImpedance, referenceImpedanceOhms);
        var outputReflection = ReflectionCoefficient(outputImpedanceBeforeLoad, referenceImpedanceOhms);
        var powerEnteringStructure = RealPower(inputVoltage, inputCurrent);
        var powerDeliveredToLoad = RealPower(outputVoltage, outputCurrent);
        var diagnostics = new PropagationDiagnostics(
            inputImpedance,
            outputImpedanceBeforeLoad,
            loadImpedance,
            inputReflection,
            outputReflection,
            powerEnteringStructure,
            powerDeliveredToLoad,
            ReflectedPowerFromNetPower(powerEnteringStructure, inputReflection),
            ReflectedPowerFromNetPower(powerDeliveredToLoad, outputReflection),
            BuildCellDiagnostics(cells, nodeVoltages, nodeCurrents, loadImpedance));
        var classification = ClassifySections(cells, diagnostics.Cells, loadImpedance);

        return new PropagationResult(nodes, cells, capacitors, summary, diagnostics, classification, BuildWarning(cells));
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
        sb.AppendLine($"Input impedance: {FormatComplex(result.Diagnostics.InputImpedanceOhms)} ohm");
        sb.AppendLine($"Output impedance before load: {FormatComplex(result.Diagnostics.OutputImpedanceBeforeLoadOhms)} ohm");
        sb.AppendLine($"Load impedance: {FormatComplex(result.Diagnostics.LoadImpedanceOhms)} ohm");
        sb.AppendLine($"Input reflected power: {result.Diagnostics.InputReflectionCoefficient.Magnitude * result.Diagnostics.InputReflectionCoefficient.Magnitude * 100.0:F3} %");
        sb.AppendLine($"Output/load reflected power: {result.Diagnostics.OutputReflectionCoefficient.Magnitude * result.Diagnostics.OutputReflectionCoefficient.Magnitude * 100.0:F3} %");
        sb.AppendLine($"Section classification: {result.Classification.Interpretation}");

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

    private static Complex ReflectionCoefficient(Complex impedance, double referenceImpedanceOhms) =>
        (impedance - referenceImpedanceOhms) / (impedance + referenceImpedanceOhms);

    private static double RealPower(Complex voltage, Complex current) =>
        Math.Max(0, (voltage * Complex.Conjugate(current)).Real);

    private static double ReflectedPowerFromNetPower(double netPower, Complex reflectionCoefficient)
    {
        var reflectedFraction = reflectionCoefficient.Magnitude * reflectionCoefficient.Magnitude;
        return reflectedFraction < 1.0
            ? netPower / Math.Max(1.0 - reflectedFraction, 1e-30) * reflectedFraction
            : double.PositiveInfinity;
    }

    private static string FormatComplex(Complex value) => $"{value.Real:F3} {(value.Imaginary < 0 ? "-" : "+")} j{Math.Abs(value.Imaginary):F3}";

    private static IReadOnlyList<PropagationCellDiagnostic> BuildCellDiagnostics(
        IReadOnlyList<PropagationCell> cells,
        IReadOnlyList<Complex> nodeVoltages,
        IReadOnlyList<Complex> nodeCurrents,
        Complex loadImpedance)
    {
        var diagnostics = new List<PropagationCellDiagnostic>(cells.Count);
        for (var index = 0; index < cells.Count; index++)
        {
            var impedance = cells[index].EffectiveImpedanceOhms;
            diagnostics.Add(new PropagationCellDiagnostic(
                index,
                impedance,
                (impedance - loadImpedance).Magnitude,
                cells[index].SlowWaveFactor,
                cells[index].DeltaPhaseDeg,
                nodeVoltages[index].Magnitude,
                nodeCurrents[index].Magnitude));
        }
        return diagnostics;
    }

    private static PropagationSectionClassification ClassifySections(
        IReadOnlyList<PropagationCell> cells,
        IReadOnlyList<PropagationCellDiagnostic> diagnostics,
        Complex loadImpedance)
    {
        if (cells.Count == 0)
            return new PropagationSectionClassification(Array.Empty<PropagationSection>(), false, "No propagation cells available.");

        var slowWave = cells.Select(cell => double.IsFinite(cell.SlowWaveFactor) ? cell.SlowWaveFactor : 0).ToList();
        var smoothed = new List<double>(slowWave.Count);
        for (var index = 0; index < slowWave.Count; index++)
        {
            var left = Math.Max(0, index - 1);
            var right = Math.Min(slowWave.Count - 1, index + 1);
            smoothed.Add(slowWave.Skip(left).Take(right - left + 1).Average());
        }

        var peakIndex = smoothed.IndexOf(smoothed.Max());
        var launcherEnd = Math.Max(0, peakIndex / 2);
        var tailStart = Math.Min(cells.Count, peakIndex + 1);
        var tailExists = tailStart < cells.Count;
        var tailSlowWaveDrops = tailExists && smoothed[^1] < smoothed[peakIndex] * 0.8;
        var tailImpedanceImproves = tailExists
                                   && diagnostics[^1].DistanceToLoadImpedanceOhms
                                   < diagnostics[Math.Min(peakIndex, diagnostics.Count - 1)].DistanceToLoadImpedanceOhms;
        var recoveryLikely = tailSlowWaveDrops && tailImpedanceImproves;
        var sections = new List<PropagationSection>
        {
            new("launcher", 0, launcherEnd),
            new("compression", Math.Min(launcherEnd + 1, peakIndex), peakIndex)
        };
        if (tailExists)
            sections.Add(new PropagationSection("recovery_output_matching", tailStart, cells.Count - 1));

        var interpretation = !tailExists
            ? "No distinct output recovery section detected."
            : recoveryLikely
                ? "Output recovery section likely: tail slow-wave factor falls and local impedance moves toward the load."
                : "Tail cells exist after peak compression, but recovery intent is unconfirmed; treat the tail as a possible optimizer artifact.";
        return new PropagationSectionClassification(sections, recoveryLikely, interpretation);
    }

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
    PropagationDiagnostics Diagnostics,
    PropagationSectionClassification Classification,
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
    double InductorEnergyJoules,
    Complex EffectiveImpedanceOhms);

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

public sealed record PropagationDiagnostics(
    Complex InputImpedanceOhms,
    Complex OutputImpedanceBeforeLoadOhms,
    Complex LoadImpedanceOhms,
    Complex InputReflectionCoefficient,
    Complex OutputReflectionCoefficient,
    double PowerEnteringStructureWatts,
    double PowerDeliveredToLoadWatts,
    double ReflectedPowerFromInputWatts,
    double ReflectedPowerFromOutputLoadWatts,
    IReadOnlyList<PropagationCellDiagnostic> Cells);

public readonly record struct PropagationCellDiagnostic(
    int CellIndex,
    Complex EffectiveImpedanceOhms,
    double DistanceToLoadImpedanceOhms,
    double SlowWaveFactor,
    double LocalPhaseShiftDeg,
    double NodeVoltageMagnitude,
    double NodeCurrentMagnitude);

public sealed record PropagationSectionClassification(
    IReadOnlyList<PropagationSection> Sections,
    bool RecoverySectionLikely,
    string Interpretation);

public readonly record struct PropagationSection(
    string Name,
    int StartCell,
    int EndCell);
