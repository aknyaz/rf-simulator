using System.Globalization;
using System.Numerics;

namespace RfSimulator;

public sealed class SaverCalibration
{
    private sealed record Terms(double Frequency, Complex Directivity, Complex SourceMatch, Complex Tracking, Complex Isolation, Complex Transmission);
    private readonly List<Terms> _terms = new();
    public string Source { get; }
    public double MinimumHz => _terms[0].Frequency;
    public double MaximumHz => _terms[^1].Frequency;

    public SaverCalibration(string path)
    {
        Source = Path.GetFullPath(path);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 13) throw new InvalidOperationException("Expected Saver 13-column SOL/through/isolation calibration.");
            var values = fields.Select(field =>
            {
                if (!double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                    throw new InvalidOperationException("Invalid calibration number.");
                return value;
            }).ToArray();
            if (values[0] <= 0 || (_terms.Count > 0 && values[0] <= MaximumHz)) throw new InvalidOperationException("Calibration frequencies must increase.");
            var shortValue = new Complex(values[1], values[2]);
            var open = new Complex(values[3], values[4]);
            var load = new Complex(values[5], values[6]);
            var through = new Complex(values[7], values[8]);
            var throughReflection = new Complex(values[9], values[10]);
            var isolation = new Complex(values[11], values[12]);
            var sourceMatch = Divide(open + shortValue - 2 * load, open - shortValue);
            var tracking = (open - load) * (1 - sourceMatch);
            var loadMatch = Divide(throughReflection - load, tracking + sourceMatch * (throughReflection - load));
            var transmission = (through - isolation) * (1 - sourceMatch * loadMatch);
            if (tracking.Magnitude < 1e-15 || transmission.Magnitude < 1e-15) throw new InvalidOperationException("Degenerate calibration standards.");
            _terms.Add(new Terms(values[0], load, sourceMatch, tracking, isolation, transmission));
        }
        if (_terms.Count < 2) throw new InvalidOperationException("Calibration needs at least two frequency points.");
    }

    private static Complex Divide(Complex numerator, Complex denominator)
    {
        if (denominator.Magnitude < 1e-15) throw new InvalidOperationException("Singular calibration correction.");
        var result = numerator / denominator;
        if (!double.IsFinite(result.Real) || !double.IsFinite(result.Imaginary)) throw new InvalidOperationException("Nonfinite calibration result.");
        return result;
    }

    public MeasuredSweep Apply(MeasuredSweep measurement)
    {
        if (measurement.Source.Contains("[Saver CAL applied]", StringComparison.Ordinal)) throw new InvalidOperationException("Calibration already applied.");
        if (measurement.ReferenceOhms != 50) throw new InvalidOperationException("This calibration implementation assumes ideal 50-ohm SOL and zero-delay through standards.");
        var output = new List<SweepPoint>();
        foreach (var point in measurement.Points)
        {
            if (point.FrequencyHz < MinimumHz || point.FrequencyHz > MaximumHz + 1000)
                throw new InvalidOperationException("Measurement lies outside calibration coverage. No extrapolation is allowed (up to 1 kHz endpoint rounding tolerance).");
            var frequency = Math.Min(point.FrequencyHz, MaximumHz);
            var right = _terms.FindIndex(term => term.Frequency >= frequency);
            var upper = _terms[right];
            var lower = _terms[Math.Max(0, right - 1)];
            var fraction = upper.Frequency == lower.Frequency ? 0 : (frequency - lower.Frequency) / (upper.Frequency - lower.Frequency);
            Complex Interpolate(Func<Terms, Complex> selector) => selector(lower) + fraction * (selector(upper) - selector(lower));
            var directivity = Interpolate(term => term.Directivity);
            var sourceMatch = Interpolate(term => term.SourceMatch);
            var delta = Interpolate(term => term.Directivity * term.SourceMatch - term.Tracking);
            var tracking = Interpolate(term => term.Tracking);
            var isolation = Interpolate(term => term.Isolation);
            var transmission = Interpolate(term => term.Transmission);
            var denominator = point.S11 * sourceMatch - delta;
            var s11 = Divide(point.S11 - directivity, denominator);
            var s21 = Divide(point.S21 - isolation, transmission) * Divide(tracking, denominator);
            output.Add(new SweepPoint(point.FrequencyHz, s11, s21));
        }
        return new MeasuredSweep($"[Saver CAL applied] {Source} | Ideal standards assumed | Input: {measurement.Source}", measurement.ReferenceOhms, output.AsReadOnly());
    }
}
