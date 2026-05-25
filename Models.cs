using System.Numerics;

namespace RfSimulator;

public sealed class RfElement
{
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
}

public sealed class SimulationConfig
{
    public double StartMHz { get; set; }
    public double StopMHz { get; set; }
    public int Points { get; set; }
    public double Z0 { get; set; }
    public List<RfElement> Structure { get; set; } = new();
}

public readonly record struct SweepPoint(double FrequencyHz, Complex S11, Complex S21);

public readonly record struct PhaseMetrics(
    double TotalPhaseRotationDeg,
    double AverageGroupDelayNs,
    double MaxGroupDelayNs);
