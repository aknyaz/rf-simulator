using System.Globalization;
using System.Numerics;

namespace RfSimulator;

public sealed record MeasuredSweep(string Source, double ReferenceOhms, IReadOnlyList<SweepPoint> Points);

public static class TouchstoneImporter
{
    public static MeasuredSweep Read(string path) => Parse(File.ReadAllText(path), Path.GetFullPath(path));

    public static MeasuredSweep Parse(string text, string source = "Touchstone")
    {
        var scale = 1e9;
        var format = "MA";
        var reference = 50.0;
        var values = new List<double>();
        var optionSeen = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('!')[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[')) throw new InvalidOperationException("Only Touchstone 1.x two-port files are supported.");
            if (line.StartsWith('#'))
            {
                if (optionSeen || values.Count > 0) throw new InvalidOperationException("Unexpected Touchstone option line.");
                optionSeen = true;
                var options = line[1..].ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (options.Length != 5 || options[1] != "S" || options[3] != "R")
                    throw new InvalidOperationException("Expected: # HZ/KHZ/MHZ/GHZ S RI/MA/DB R impedance.");
                scale = options[0] switch { "HZ" => 1, "KHZ" => 1e3, "MHZ" => 1e6, "GHZ" => 1e9, _ => throw new InvalidOperationException("Unsupported frequency unit.") };
                format = options[2];
                if (format is not ("RI" or "MA" or "DB")) throw new InvalidOperationException("Unsupported complex format.");
                reference = Number(options[4]);
                if (reference <= 0) throw new InvalidOperationException("Reference impedance must be positive.");
                continue;
            }
            values.AddRange(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Number));
        }
        if (values.Count < 18 || values.Count % 9 != 0)
            throw new InvalidOperationException("Expected at least two complete two-port records (9 numbers each); noise data is not supported.");
        var points = new List<SweepPoint>();
        for (var index = 0; index < values.Count; index += 9)
        {
            var frequency = values[index] * scale;
            if (!double.IsFinite(frequency) || frequency <= 0 || (points.Count > 0 && frequency <= points[^1].FrequencyHz))
                throw new InvalidOperationException("Frequencies must be positive and strictly increasing.");
            var s11 = ConvertPair(values[index + 1], values[index + 2], format);
            var s21 = ConvertPair(values[index + 3], values[index + 4], format);
            ConvertPair(values[index + 5], values[index + 6], format);
            ConvertPair(values[index + 7], values[index + 8], format);
            points.Add(new SweepPoint(frequency, s11, s21));
        }
        return new MeasuredSweep(source, reference, points.AsReadOnly());
    }

    private static double Number(string token)
    {
        if (!double.TryParse(token.Replace('D', 'E').Replace('d', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new InvalidOperationException($"Invalid finite number: {token}");
        return value;
    }

    private static Complex ConvertPair(double first, double second, string format)
    {
        if (format == "MA" && first < 0) throw new InvalidOperationException("Magnitude must not be negative.");
        var value = format == "RI" ? new Complex(first, second)
            : Complex.FromPolarCoordinates(format == "DB" ? Math.Pow(10, first / 20) : first, second * Math.PI / 180);
        if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary)) throw new InvalidOperationException("Complex value overflow.");
        return value;
    }
}
