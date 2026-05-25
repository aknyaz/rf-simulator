namespace RfSimulator;

public static class RfMath
{
    public static double ToDbMagnitude(System.Numerics.Complex value)
    {
        var mag = value.Magnitude;
        mag = Math.Max(mag, 1e-15);
        return 20.0 * Math.Log10(mag);
    }

    public static List<double> UnwrapPhase(IReadOnlyList<double> phaseRadians)
    {
        var unwrapped = new List<double>(phaseRadians.Count);
        if (phaseRadians.Count == 0)
        {
            return unwrapped;
        }

        unwrapped.Add(phaseRadians[0]);
        var offset = 0.0;

        for (int i = 1; i < phaseRadians.Count; i++)
        {
            var delta = phaseRadians[i] - phaseRadians[i - 1];
            if (delta > Math.PI)
            {
                offset -= 2.0 * Math.PI;
            }
            else if (delta < -Math.PI)
            {
                offset += 2.0 * Math.PI;
            }

            unwrapped.Add(phaseRadians[i] + offset);
        }

        return unwrapped;
    }

    public static PhaseMetrics CalculatePhaseMetrics(IReadOnlyList<SweepPoint> sweep)
    {
        if (sweep.Count < 2)
        {
            return new PhaseMetrics(0, 0, 0);
        }

        var unwrappedPhase = UnwrapPhase(sweep.Select(p => Math.Atan2(p.S21.Imaginary, p.S21.Real)).ToList());
        var totalPhaseRotationDeg = Math.Abs(unwrappedPhase[^1] - unwrappedPhase[0]) * 180.0 / Math.PI;
        var groupDelaysNs = new List<double>(sweep.Count - 1);

        for (int i = 1; i < sweep.Count; i++)
        {
            var deltaPhase = unwrappedPhase[i] - unwrappedPhase[i - 1];
            var deltaOmega = 2.0 * Math.PI * (sweep[i].FrequencyHz - sweep[i - 1].FrequencyHz);

            if (deltaOmega <= 0)
            {
                continue;
            }

            var groupDelaySeconds = -deltaPhase / deltaOmega;
            if (double.IsFinite(groupDelaySeconds) && groupDelaySeconds > 0)
            {
                groupDelaysNs.Add(groupDelaySeconds * 1e9);
            }
        }

        if (groupDelaysNs.Count == 0)
        {
            return new PhaseMetrics(totalPhaseRotationDeg, 0, 0);
        }

        return new PhaseMetrics(totalPhaseRotationDeg, groupDelaysNs.Average(), groupDelaysNs.Max());
    }
}
