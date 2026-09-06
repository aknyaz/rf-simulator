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
    public string? CircuitId { get; set; }
    public long Revision { get; set; }
    public int[] LockedIndices { get; set; } = Array.Empty<int>();
    public double? ManualMarkerMHz { get; set; }
    public double CellSpacingMm { get; set; } = 5;
}

public readonly record struct SweepPoint(double FrequencyHz, Complex S11, Complex S21);

public readonly record struct SParameterPoint(
    double FrequencyHz,
    Complex S11,
    Complex S21,
    Complex S12,
    Complex S22);

public readonly record struct PhaseMetrics(
    double TotalPhaseRotationDeg,
    double AverageGroupDelayNs,
    double MaxGroupDelayNs);
