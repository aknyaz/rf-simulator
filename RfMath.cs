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

    public static List<double> GetUnwrappedS21PhaseRadians(IReadOnlyList<SweepPoint> sweep)
    {
        return UnwrapPhase(sweep.Select(p => Math.Atan2(p.S21.Imaginary, p.S21.Real)).ToList());
    }

    public static List<double> CalculateGroupDelayNs(IReadOnlyList<SweepPoint> sweep)
    {
        var groupDelayNs = new List<double>(sweep.Count);
        if (sweep.Count == 0)
        {
            return groupDelayNs;
        }

        if (sweep.Count == 1)
        {
            groupDelayNs.Add(0);
            return groupDelayNs;
        }

        var phase = GetUnwrappedS21PhaseRadians(sweep);

        for (int i = 0; i < sweep.Count; i++)
        {
            int leftIndex;
            int rightIndex;

            if (i == 0)
            {
                leftIndex = 0;
                rightIndex = 1;
            }
            else if (i == sweep.Count - 1)
            {
                leftIndex = sweep.Count - 2;
                rightIndex = sweep.Count - 1;
            }
            else
            {
                leftIndex = i - 1;
                rightIndex = i + 1;
            }

            var deltaPhase = phase[rightIndex] - phase[leftIndex];
            var deltaOmega = 2.0 * Math.PI * (sweep[rightIndex].FrequencyHz - sweep[leftIndex].FrequencyHz);

            if (deltaOmega <= 0)
            {
                groupDelayNs.Add(0);
                continue;
            }

            var groupDelaySeconds = -deltaPhase / deltaOmega;
            groupDelayNs.Add(double.IsFinite(groupDelaySeconds) ? groupDelaySeconds * 1e9 : 0);
        }

        return groupDelayNs;
    }

    public static PhaseMetrics CalculatePhaseMetrics(IReadOnlyList<SweepPoint> sweep)
    {
        if (sweep.Count < 2)
        {
            return new PhaseMetrics(0, 0, 0);
        }

        var unwrappedPhase = GetUnwrappedS21PhaseRadians(sweep);
        var totalPhaseRotationDeg = Math.Abs(unwrappedPhase[^1] - unwrappedPhase[0]) * 180.0 / Math.PI;
        var groupDelaysNs = CalculateGroupDelayNs(sweep)
            .Where(delay => double.IsFinite(delay) && delay > 0)
            .ToList();

        if (groupDelaysNs.Count == 0)
        {
            return new PhaseMetrics(totalPhaseRotationDeg, 0, 0);
        }

        return new PhaseMetrics(totalPhaseRotationDeg, groupDelaysNs.Average(), groupDelaysNs.Max());
    }
}
