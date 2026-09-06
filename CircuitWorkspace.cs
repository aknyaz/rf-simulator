using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RfSimulator;

public sealed record FrequencyConfiguration(double StartMHz, double StopMHz, int Points, double Z0)
{
    public double StartHz => StartMHz * 1e6;
    public double StopHz => StopMHz * 1e6;
    public double MidpointMHz => (StartMHz + StopMHz) / 2;

    public void Validate()
    {
        if (!double.IsFinite(StartMHz) || !double.IsFinite(StopMHz) || StartMHz <= 0 || StopMHz <= StartMHz)
            throw new InvalidOperationException("Frequency must be positive, with Start < Stop.");
        if (Points < 2 || Points > 1000000)
            throw new InvalidOperationException("Sweep points must be between 2 and 1000000.");
        if (!double.IsFinite(Z0) || Z0 <= 0)
            throw new InvalidOperationException("Reference impedance must be positive and finite.");
    }

    public double InspectionMHz(double? manualMHz)
    {
        Validate();
        var requested = manualMHz ?? MidpointMHz;
        if (!double.IsFinite(requested) || requested < StartMHz || requested > StopMHz)
            throw new InvalidOperationException("Inspection frequency must be within the sweep range.");
        return requested;
    }
}

// Snapshots own their data. UI adapters, candidates and workers receive copies, never the live list.
public sealed class CircuitSnapshot
{
    private readonly List<RfElement> _structure;
    private readonly int[] _lockedIndices;
    public long Revision { get; }
    public FrequencyConfiguration Frequency { get; }
    public string CircuitId { get; }
    public List<RfElement> Structure => OptimizerEngine.CloneStructure(_structure);
    public int[] LockedIndices => (int[])_lockedIndices.Clone();

    public CircuitSnapshot(long revision, FrequencyConfiguration frequency, IReadOnlyList<RfElement> structure,
        IEnumerable<int>? lockedIndices = null)
    {
        frequency.Validate();
        SimulationEngine.ValidateStructure(structure.ToList());
        Revision = revision;
        Frequency = frequency;
        _structure = OptimizerEngine.CloneStructure(structure);
        _lockedIndices = (lockedIndices ?? Array.Empty<int>()).Distinct().Order().ToArray();
        CircuitId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_structure))));
    }

    public List<SweepPoint> Evaluate() => SimulationEngine.Simulate(
        Frequency.StartHz, Frequency.StopHz, Frequency.Points, Frequency.Z0, _structure);

    public SimulationConfig ToConfig(double? markerMHz = null) => new()
    {
        StartMHz = Frequency.StartMHz, StopMHz = Frequency.StopMHz, Points = Frequency.Points,
        Z0 = Frequency.Z0, Structure = Structure, CircuitId = CircuitId, Revision = Revision,
        LockedIndices = LockedIndices, ManualMarkerMHz = markerMHz
    };
}

public sealed class CircuitWorkspace
{
    public CircuitSnapshot? Current { get; private set; }
    public long Revision { get; private set; }

    public CircuitSnapshot Set(FrequencyConfiguration frequency, IReadOnlyList<RfElement> structure,
        IEnumerable<int>? lockedIndices = null)
    {
        var next = new CircuitSnapshot(Revision + 1, frequency, structure, lockedIndices);
        if (Current is not null && Current.Frequency == frequency && Current.CircuitId == next.CircuitId
            && Current.LockedIndices.SequenceEqual(next.LockedIndices))
            return Current;
        Current = next;
        Revision = next.Revision;
        return next;
    }
}
