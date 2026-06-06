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
        return CalculateScoreBreakdown(sweep, structure, config).TotalScore;
    }

    public static SmallAntennaScoreBreakdown CalculateScoreBreakdown(
        IReadOnlyList<SmallAntennaPoint> sweep,
        IReadOnlyList<RfElement> structure,
        SmallAntennaOptimizerConfig config)
    {
        if (sweep.Count == 0)
        {
            return SmallAntennaScoreBreakdown.Invalid;
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

        var inputMatchScore = CalculateInputMatchScore(bestReturnLoss, config);
        var radiatedPowerScore = config.ObjectiveMode == SmallAntennaObjectiveMode.AntennaMatch
            ? -80.0 * Math.Log10(1.0 + 100.0 * bestRadiatedPowerRatio)
              -25.0 * Math.Log10(1.0 + 100.0 * averageRadiatedPowerRatio)
              +35.0 * averageLossPowerRatio
            : -12.0 * Math.Log10(1.0 + 100.0 * averageRadiatedPowerRatio)
              +10.0 * averageLossPowerRatio;

        var resonancePenalty = 3.0 * spikePenalty * spikePenalty;
        if (config.MaxAntennaVoltage > 0 && peakVoltage > config.MaxAntennaVoltage)
        {
            var ratio = peakVoltage / config.MaxAntennaVoltage;
            resonancePenalty += 20.0 * ratio * ratio;
        }

        var smoothGradientScore = 0.35 * CalculateGradientPenalty(structure);
        var phaseCompressionScore = 0.0;
        var energyLocalizationPenalty = 0.0;
        var inputCouplingScore = 0.0;

        if (config.ObjectiveMode == SmallAntennaObjectiveMode.SmoothCompression)
        {
            var compressionMedium = GetCompressionMediumStructure(structure, config.LauncherCellCount);
            var jumpPenalty =
                CalculateSameTypeJumpPenalty(compressionMedium, "SeriesL")
                + CalculateSameTypeJumpPenalty(compressionMedium, "ShuntC");
            var increasingPenalty = CalculateIncreasingGradientPenalty(compressionMedium);
            var derivativePenalty =
                CalculateSameTypeDerivativePenalty(compressionMedium, "SeriesL")
                + CalculateSameTypeDerivativePenalty(compressionMedium, "ShuntC");
            smoothGradientScore += 75.0 * jumpPenalty + 140.0 * increasingPenalty + 60.0 * derivativePenalty;

            inputCouplingScore = CalculateInputCouplingPenalty(sweep, structure, config);
            resonancePenalty += CalculateBroadbandResonancePenalty(sweep, config.MaxAntennaVoltage);
            var shapeScores = CalculateCompressionShapeScores(sweep, structure, config);
            phaseCompressionScore = shapeScores.PhaseCompressionScore;
            energyLocalizationPenalty = shapeScores.EnergyLocalizationPenalty;
        }
        else if (config.UseContinuousValues && config.UseSmoothIncreasingGradient)
        {
            smoothGradientScore += 4.0 * CalculateIncreasingGradientPenalty(structure);
        }

        return new SmallAntennaScoreBreakdown(
            inputMatchScore,
            radiatedPowerScore,
            smoothGradientScore,
            phaseCompressionScore,
            resonancePenalty,
            energyLocalizationPenalty,
            inputCouplingScore);
    }

    private static double CalculateInputMatchScore(double bestReturnLoss, SmallAntennaOptimizerConfig config)
    {
        if (bestReturnLoss <= config.TargetInputReturnLossDb)
        {
            return 0;
        }

        var miss = bestReturnLoss - config.TargetInputReturnLossDb;
        return config.ObjectiveMode == SmallAntennaObjectiveMode.AntennaMatch
            ? 1_000_000.0 + 100_000.0 * miss * miss
            : 500.0 * miss * miss;
    }

    public static SmallAntennaCandidate Optimize(
        IReadOnlyList<RfElement> initialStructure,
        SmallAntennaOptimizerConfig config,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        return config.UseContinuousValues
            ? OptimizeContinuous(initialStructure, config, statusCallback, cancellationToken)
            : OptimizeDiscrete(initialStructure, config, statusCallback, cancellationToken);
    }

    private static SmallAntennaCandidate OptimizeDiscrete(
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

    private static SmallAntennaCandidate OptimizeContinuous(
        IReadOnlyList<RfElement> initialStructure,
        SmallAntennaOptimizerConfig config,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        ValidateContinuousRanges(config);

        var current = ClampContinuousStructure(OptimizerEngine.CloneStructure(initialStructure), config);
        var currentResult = Evaluate(current, config);
        var logStep = Math.Log(1.8);
        const double minLogStep = 0.005;

        for (var iter = 0; iter < config.MaxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statusCallback?.Invoke($"Continuous antenna optimization iteration {iter + 1}/{config.MaxIterations}");

            var improved = false;
            for (var elementIndex = 0; elementIndex < current.Count; elementIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var baseValue = current[elementIndex].Value;
                foreach (var direction in new[] { 1.0, -1.0 })
                {
                    var test = OptimizerEngine.CloneStructure(current);
                    test[elementIndex].Value = ClampElementValue(
                        test[elementIndex],
                        baseValue * Math.Exp(direction * logStep),
                        config);

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
                logStep *= 0.5;
                if (logStep < minLogStep)
                {
                    statusCallback?.Invoke("Continuous antenna optimizer reached minimum step size.");
                    break;
                }
            }
        }

        return currentResult;
    }

    public static SmallAntennaCandidate Evaluate(List<RfElement> structure, SmallAntennaOptimizerConfig config)
    {
        var sweep = Simulate(config.StartHz, config.StopHz, config.Points, structure, config.AntennaConfig);
        var scoreBreakdown = CalculateScoreBreakdown(sweep, structure, config);
        var summary = Summarize(sweep, null, config.MaxAntennaVoltage);
        return new SmallAntennaCandidate
        {
            Score = scoreBreakdown.TotalScore,
            ScoreBreakdown = scoreBreakdown,
            Structure = OptimizerEngine.CloneStructure(structure),
            Sweep = sweep,
            Summary = summary
        };
    }

    public static string FormatScoreBreakdown(SmallAntennaScoreBreakdown score)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Total optimizer score: {score.TotalScore:F4}");
        sb.AppendLine($"Input match score: {score.InputMatchScore:F4}");
        sb.AppendLine($"Radiated power score: {score.RadiatedPowerScore:F4}");
        sb.AppendLine($"Smooth gradient score: {score.SmoothGradientScore:F4}");
        sb.AppendLine($"Phase-compression score: {score.PhaseCompressionScore:F4}");
        sb.AppendLine($"Resonance penalty: {score.ResonancePenalty:F4}");
        sb.AppendLine($"Energy-localization penalty: {score.EnergyLocalizationPenalty:F4}");
        sb.AppendLine($"Input-coupling score: {score.InputCouplingScore:F4}");
        return sb.ToString();
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
                "seriesc" => AbcdMatrix.SeriesImpedance(-Complex.ImaginaryOne / Math.Max(omega * element.Value, 1e-30)),
                "seriesr" => AbcdMatrix.SeriesImpedance(new Complex(Math.Max(element.Value, 0.0), 0.0)),
                "shuntc" => AbcdMatrix.ShuntAdmittance(Complex.ImaginaryOne * omega * element.Value),
                "shuntl" => AbcdMatrix.ShuntAdmittance(-Complex.ImaginaryOne / Math.Max(omega * element.Value, 1e-30)),
                "shuntr" => AbcdMatrix.ShuntAdmittance(new Complex(1.0 / Math.Max(element.Value, 1e-30), 0.0)),
                _ => throw new InvalidOperationException($"Unsupported element type: {element.Type}")
            };

            total *= section;
        }

        return total;
    }

    private static double CalculateBroadbandResonancePenalty(
        IReadOnlyList<SmallAntennaPoint> sweep,
        double maxAntennaVoltage)
    {
        var voltages = sweep.Select(p => p.AntennaVoltage.Magnitude).OrderBy(v => v).ToList();
        var currents = sweep.Select(p => p.AntennaCurrent.Magnitude).OrderBy(v => v).ToList();
        var radiatedDb = sweep.Select(p => p.RadiatedPowerDb).Where(double.IsFinite).ToList();
        if (voltages.Count == 0 || currents.Count == 0 || radiatedDb.Count == 0)
        {
            return 100_000;
        }

        var peakVoltage = voltages[^1];
        var peakCurrent = currents[^1];
        var medianVoltage = Math.Max(voltages[voltages.Count / 2], 1e-12);
        var medianCurrent = Math.Max(currents[currents.Count / 2], 1e-12);
        var voltagePeakRatio = peakVoltage / medianVoltage;
        var currentPeakRatio = peakCurrent / medianCurrent;
        var peakRadiatedDb = radiatedDb.Max();
        var threeDbPoints = radiatedDb.Count(value => value >= peakRadiatedDb - 3.0);
        var bandwidthFraction = (double)threeDbPoints / radiatedDb.Count;

        var penalty = 0.0;
        penalty += 12.0 * Math.Pow(Math.Max(0, voltagePeakRatio - 2.5), 2);
        penalty += 12.0 * Math.Pow(Math.Max(0, currentPeakRatio - 2.5), 2);
        penalty += 500.0 * Math.Pow(Math.Max(0, 0.12 - bandwidthFraction), 2);

        if (maxAntennaVoltage > 0 && peakVoltage > maxAntennaVoltage)
        {
            penalty += 120.0 * Math.Pow(peakVoltage / maxAntennaVoltage - 1.0, 2);
        }

        return penalty;
    }

    private static CompressionShapeScores CalculateCompressionShapeScores(
        IReadOnlyList<SmallAntennaPoint> sweep,
        IReadOnlyList<RfElement> structure,
        SmallAntennaOptimizerConfig config)
    {
        if (structure.Count == 0)
        {
            return CompressionShapeScores.Invalid;
        }

        try
        {
            var analysisPoint = sweep.OrderByDescending(p => p.RadiatedPowerWatts).First();
            var propagation = PropagationAnalysis.AnalyzeTerminated(
                structure,
                analysisPoint.FrequencyHz,
                SourceResistanceOhms,
                config.AntennaConfig.AntennaImpedance,
                config.CellSpacingMeters,
                Array.Empty<SweepPoint>());

            var phaseShift = propagation.Cells.Select(c => Math.Max(0, -c.DeltaPhaseRad)).ToList();
            var slowWave = propagation.Cells
                .Select(c => c.SlowWaveFactor)
                .Where(value => double.IsFinite(value) && value > 0)
                .ToList();
            var launcherCells = Math.Clamp(config.LauncherCellCount, 0, Math.Max(0, phaseShift.Count - 2));
            phaseShift = phaseShift.Skip(launcherCells).ToList();
            slowWave = slowWave.Skip(launcherCells).ToList();

            if (phaseShift.Count < 2 || slowWave.Count != phaseShift.Count)
            {
                return CompressionShapeScores.Invalid;
            }

            var phaseSmoothPenalty = CalculateRelativeSmoothnessPenalty(phaseShift);
            var phaseIncreasingPenalty = CalculateDecreasePenalty(phaseShift);
            var phaseJumpPenalty = CalculateAbruptGrowthPenalty(phaseShift, 2.0);
            var slowWaveSmoothPenalty = CalculateRelativeSmoothnessPenalty(slowWave);
            var slowWaveIncreasingPenalty = CalculateDecreasePenalty(slowWave);
            var slowWaveGrowthPenalty = CalculateAbruptGrowthPenalty(slowWave, 1.7);
            var extremeSlowWavePenalty = slowWave.Sum(value => Math.Pow(Math.Max(0, value - 80.0) / 20.0, 2));
            var endCellCompressionPenalty = CalculateEndCellCompressionPenalty(slowWave);
            var totalPhase = phaseShift.Sum();
            var averageSlowWave = slowWave.Average();
            var phaseCompressionScore =
                35.0 * phaseSmoothPenalty
                +90.0 * phaseIncreasingPenalty
                +160.0 * phaseJumpPenalty
                +30.0 * slowWaveSmoothPenalty
                +60.0 * slowWaveIncreasingPenalty
                +180.0 * slowWaveGrowthPenalty
                +220.0 * extremeSlowWavePenalty
                +300.0 * endCellCompressionPenalty
                -12.0 * Math.Log(1.0 + totalPhase)
                -8.0 * Math.Log(1.0 + averageSlowWave);

            var energies = BuildDistributedEnergyProfile(propagation).Skip(launcherCells).ToList();
            if (energies.Count == 0)
            {
                return new CompressionShapeScores(phaseCompressionScore, 100_000);
            }

            var totalEnergy = energies.Sum();
            if (!double.IsFinite(totalEnergy) || totalEnergy <= 0)
            {
                return new CompressionShapeScores(phaseCompressionScore, 100_000);
            }

            var largestShare = energies.Max() / totalEnergy;
            var concentrationPenalty = 1200.0 * Math.Pow(Math.Max(0, largestShare - 0.25), 2);
            var energyGrowthPenalty = 40.0 * CalculateDecreasePenalty(energies);
            var energyJumpPenalty = 25.0 * CalculateRelativeSmoothnessPenalty(energies);
            return new CompressionShapeScores(
                phaseCompressionScore,
                concentrationPenalty + energyGrowthPenalty + energyJumpPenalty);
        }
        catch
        {
            return CompressionShapeScores.Invalid;
        }
    }

    private static List<double> BuildDistributedEnergyProfile(PropagationResult propagation)
    {
        var values = new List<double>();
        for (var cellIndex = 0; cellIndex < propagation.Cells.Count; cellIndex++)
        {
            var energy = propagation.Cells[cellIndex].InductorEnergyJoules;
            if (cellIndex < propagation.Capacitors.Count)
            {
                energy += propagation.Capacitors[cellIndex].CapacitorEnergyJoules;
            }

            values.Add(Math.Max(energy, 1e-30));
        }

        return values;
    }

    private static double CalculateRelativeSmoothnessPenalty(IReadOnlyList<double> values)
    {
        var penalty = 0.0;
        for (var i = 0; i < values.Count - 1; i++)
        {
            var left = Math.Max(values[i], 1e-30);
            var right = Math.Max(values[i + 1], 1e-30);
            penalty += Math.Pow(Math.Log(right / left), 2);
        }

        return penalty;
    }

    private static double CalculateDecreasePenalty(IReadOnlyList<double> values)
    {
        var penalty = 0.0;
        for (var i = 0; i < values.Count - 1; i++)
        {
            if (values[i + 1] < values[i])
            {
                penalty += Math.Pow(Math.Log(Math.Max(values[i], 1e-30) / Math.Max(values[i + 1], 1e-30)), 2);
            }
        }

        return penalty;
    }

    private static double CalculateAbruptGrowthPenalty(IReadOnlyList<double> values, double allowedRatio)
    {
        var penalty = 0.0;
        for (var i = 0; i < values.Count - 1; i++)
        {
            var left = Math.Max(values[i], 1e-30);
            var ratio = Math.Max(values[i + 1], 1e-30) / left;
            if (ratio > allowedRatio)
            {
                penalty += Math.Pow(Math.Log(ratio / allowedRatio), 2);
            }
        }

        return penalty;
    }

    private static double CalculateEndCellCompressionPenalty(IReadOnlyList<double> slowWave)
    {
        if (slowWave.Count < 2)
        {
            return 0;
        }

        var finalValue = slowWave[^1];
        var previousValues = slowWave.Take(slowWave.Count - 1).OrderBy(value => value).ToList();
        var medianPrevious = Math.Max(previousValues[previousValues.Count / 2], 1e-30);
        var ratio = finalValue / medianPrevious;
        return Math.Pow(Math.Max(0, ratio - 2.0), 2);
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

    private static double CalculateInputCouplingPenalty(
        IReadOnlyList<SmallAntennaPoint> sweep,
        IReadOnlyList<RfElement> structure,
        SmallAntennaOptimizerConfig config)
    {
        var reflectionPower = sweep
            .Select(point => point.InputReflectionCoefficient.Magnitude)
            .Select(magnitude => magnitude * magnitude)
            .ToList();
        if (reflectionPower.Count == 0)
        {
            return 100_000;
        }

        var launcherElements = GetLauncherStructure(structure, config.LauncherCellCount);
        var taperPenalty =
            CalculateSameTypeIncreasingPenalty(launcherElements, "SeriesL")
            + CalculateSameTypeIncreasingPenalty(launcherElements, "ShuntC")
            + CalculateSameTypeAbruptRatioPenalty(launcherElements, "SeriesL", 2.5)
            + CalculateSameTypeAbruptRatioPenalty(launcherElements, "ShuntC", 2.5)
            + CalculateLauncherInterfacePenalty(structure, config.LauncherCellCount);
        var averageReflectionPercent = reflectionPower.Average() * 100.0;
        var couplingGateMiss = Math.Max(0, averageReflectionPercent - config.MaxAverageInputReflectionPercent);

        return 260.0 * reflectionPower.Average()
               + 120.0 * reflectionPower.Max()
               + 90.0 * taperPenalty
               + 40.0 * couplingGateMiss * couplingGateMiss;
    }

    private static IReadOnlyList<RfElement> GetLauncherStructure(
        IReadOnlyList<RfElement> structure,
        int requestedLauncherCells)
    {
        var launcherElementCount = Math.Clamp(requestedLauncherCells * 2, 0, Math.Max(0, structure.Count - 1));
        return structure.Take(launcherElementCount).ToList();
    }

    private static IReadOnlyList<RfElement> GetCompressionMediumStructure(
        IReadOnlyList<RfElement> structure,
        int requestedLauncherCells)
    {
        var launcherElementCount = Math.Clamp(requestedLauncherCells * 2, 0, Math.Max(0, structure.Count - 3));
        return structure.Skip(launcherElementCount).ToList();
    }

    private static double CalculateLauncherInterfacePenalty(
        IReadOnlyList<RfElement> structure,
        int requestedLauncherCells)
    {
        var launcherElementCount = Math.Clamp(requestedLauncherCells * 2, 0, Math.Max(0, structure.Count - 3));
        if (launcherElementCount == 0)
        {
            return 0;
        }

        var penalty = 0.0;
        foreach (var type in new[] { "SeriesL", "ShuntC" })
        {
            var launcherValue = structure.Take(launcherElementCount)
                .Where(element => element.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Value)
                .LastOrDefault();
            var mediumValue = structure.Skip(launcherElementCount)
                .Where(element => element.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Value)
                .FirstOrDefault();
            if (launcherValue > 0 && mediumValue > 0)
            {
                penalty += Math.Pow(Math.Max(0, Math.Abs(Math.Log(mediumValue / launcherValue)) - Math.Log(2.0)), 2);
            }
        }

        return penalty;
    }

    private static double CalculateSameTypeAbruptRatioPenalty(
        IReadOnlyList<RfElement> structure,
        string type,
        double allowedRatio)
    {
        var values = structure
            .Where(element => element.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value)
            .ToList();
        var penalty = 0.0;
        for (var index = 0; index < values.Count - 1; index++)
        {
            var ratio = Math.Max(values[index], values[index + 1]) / Math.Min(values[index], values[index + 1]);
            penalty += Math.Pow(Math.Max(0, Math.Log(ratio / allowedRatio)), 2);
        }

        return penalty;
    }

    private static double CalculateSameTypeJumpPenalty(IReadOnlyList<RfElement> structure, string type)
    {
        var values = structure
            .Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value)
            .ToList();

        var penalty = 0.0;
        for (var i = 0; i < values.Count - 1; i++)
        {
            penalty += Math.Pow(Math.Log(values[i + 1] / values[i]), 2);
        }

        return penalty;
    }

    private static double CalculateSameTypeDerivativePenalty(IReadOnlyList<RfElement> structure, string type)
    {
        var values = structure
            .Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value)
            .ToList();
        if (values.Count < 3)
        {
            return 0;
        }

        var logValues = values.Select(value => Math.Log(value)).ToList();
        var penalty = 0.0;
        for (var i = 0; i < logValues.Count - 2; i++)
        {
            var leftSlope = logValues[i + 1] - logValues[i];
            var rightSlope = logValues[i + 2] - logValues[i + 1];
            penalty += Math.Pow(rightSlope - leftSlope, 2);
        }

        return penalty;
    }

    private static double CalculateIncreasingGradientPenalty(IReadOnlyList<RfElement> structure)
    {
        return CalculateSameTypeIncreasingPenalty(structure, "SeriesL")
               + CalculateSameTypeIncreasingPenalty(structure, "ShuntC");
    }

    private static double CalculateSameTypeIncreasingPenalty(IReadOnlyList<RfElement> structure, string type)
    {
        var values = structure
            .Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value)
            .ToList();

        var penalty = 0.0;
        for (var i = 0; i < values.Count - 1; i++)
        {
            if (values[i + 1] < values[i])
            {
                penalty += Math.Pow(Math.Log(values[i] / values[i + 1]), 2);
            }
        }

        return penalty;
    }

    private static List<RfElement> ClampContinuousStructure(List<RfElement> structure, SmallAntennaOptimizerConfig config)
    {
        foreach (var element in structure)
        {
            element.Value = ClampElementValue(element, element.Value, config);
        }

        return structure;
    }

    private static double ClampElementValue(RfElement element, double value, SmallAntennaOptimizerConfig config)
    {
        if (element.Type.Equals("SeriesL", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Min(config.ContinuousLMaxHenries, Math.Max(config.ContinuousLMinHenries, value));
        }

        if (element.Type.Equals("ShuntC", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Min(config.ContinuousCMaxFarads, Math.Max(config.ContinuousCMinFarads, value));
        }

        return value;
    }

    private static void ValidateContinuousRanges(SmallAntennaOptimizerConfig config)
    {
        if (config.ContinuousLMinHenries <= 0 || config.ContinuousLMaxHenries <= config.ContinuousLMinHenries)
        {
            throw new InvalidOperationException("Continuous L range must be positive and max must be greater than min.");
        }

        if (config.ContinuousCMinFarads <= 0 || config.ContinuousCMaxFarads <= config.ContinuousCMinFarads)
        {
            throw new InvalidOperationException("Continuous C range must be positive and max must be greater than min.");
        }
    }

    private readonly record struct CompressionShapeScores(
        double PhaseCompressionScore,
        double EnergyLocalizationPenalty)
    {
        public static CompressionShapeScores Invalid => new(100_000, 100_000);
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
    public bool UseContinuousValues { get; set; }
    public double ContinuousLMinHenries { get; set; } = 10e-9;
    public double ContinuousLMaxHenries { get; set; } = 2000e-9;
    public double ContinuousCMinFarads { get; set; } = 0.5e-12;
    public double ContinuousCMaxFarads { get; set; } = 470e-12;
    public bool UseSmoothIncreasingGradient { get; set; }
    public int LauncherCellCount { get; set; } = 2;
    public double MaxAverageInputReflectionPercent { get; set; } = 50;
    public double CellSpacingMeters { get; set; } = 5e-3;
    public SmallAntennaObjectiveMode ObjectiveMode { get; set; } = SmallAntennaObjectiveMode.AntennaMatch;
    public SmallAntennaConfig AntennaConfig { get; set; } = new();
}

public enum SmallAntennaObjectiveMode
{
    AntennaMatch,
    SmoothCompression
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
    public SmallAntennaScoreBreakdown ScoreBreakdown { get; set; }
    public List<RfElement> Structure { get; set; } = new();
    public List<SmallAntennaPoint> Sweep { get; set; } = new();
    public SmallAntennaSummary Summary { get; set; } = new();
}

public readonly record struct SmallAntennaScoreBreakdown(
    double InputMatchScore,
    double RadiatedPowerScore,
    double SmoothGradientScore,
    double PhaseCompressionScore,
    double ResonancePenalty,
    double EnergyLocalizationPenalty,
    double InputCouplingScore)
{
    public double TotalScore =>
        InputMatchScore
        + RadiatedPowerScore
        + SmoothGradientScore
        + PhaseCompressionScore
        + ResonancePenalty
        + EnergyLocalizationPenalty
        + InputCouplingScore;

    public static SmallAntennaScoreBreakdown Invalid =>
        new(double.MaxValue, 0, 0, 0, 0, 0, 0);
}
