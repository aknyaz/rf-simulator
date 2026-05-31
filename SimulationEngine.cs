using System.Numerics;
using System.Text.Json;

namespace RfSimulator;

public static class SimulationEngine
{
    public static List<RfElement> ParseStructure(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var elements = JsonSerializer.Deserialize<List<RfElement>>(json, options)
                       ?? throw new InvalidOperationException("Structure JSON is empty.");

        ValidateStructure(elements);

        return elements;
    }

    public static List<RfElement> ParseStructureAllowEmpty(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var elements = JsonSerializer.Deserialize<List<RfElement>>(json, options)
                       ?? throw new InvalidOperationException("Structure JSON is empty.");

        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.Type))
            {
                throw new InvalidOperationException("Each element must define type.");
            }
            if (element.Value <= 0)
            {
                throw new InvalidOperationException("Element values must be positive.");
            }
        }

        return elements;
    }

    public static void ValidateStructure(IReadOnlyCollection<RfElement> elements)
    {
        if (elements.Count == 0)
        {
            throw new InvalidOperationException("Structure must contain at least one element.");
        }

        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.Type))
            {
                throw new InvalidOperationException("Each element must define type.");
            }
            if (element.Value <= 0)
            {
                throw new InvalidOperationException("Element values must be positive.");
            }
        }
    }

    public static List<SweepPoint> Simulate(
        double startHz,
        double stopHz,
        int points,
        double z0,
        IReadOnlyList<RfElement> structure)
    {
        return SimulateSParameters(startHz, stopHz, points, z0, structure)
            .Select(p => new SweepPoint(p.FrequencyHz, p.S11, p.S21))
            .ToList();
    }

    public static List<SParameterPoint> SimulateSParameters(
        double startHz,
        double stopHz,
        int points,
        double z0,
        IReadOnlyList<RfElement> structure)
    {
        if (points < 2)
        {
            throw new InvalidOperationException("Sweep points must be at least 2.");
        }
        if (stopHz <= startHz)
        {
            throw new InvalidOperationException("Stop frequency must be greater than start frequency.");
        }

        var result = new List<SParameterPoint>(points);
        var step = (stopHz - startHz) / (points - 1);

        for (var i = 0; i < points; i++)
        {
            var f = startHz + i * step;
            var omega = 2.0 * Math.PI * f;
            var total = AbcdMatrix.Identity;

            foreach (var element in structure)
            {
                var section = element.Type.Trim().ToLowerInvariant() switch
                {
                    "seriesl" => AbcdMatrix.SeriesImpedance(Complex.ImaginaryOne * omega * element.Value),
                    "seriesc" => AbcdMatrix.SeriesImpedance(1.0 / (Complex.ImaginaryOne * omega * element.Value)),
                    "seriesr" => AbcdMatrix.SeriesImpedance(element.Value),
                    "shuntc" => AbcdMatrix.ShuntAdmittance(Complex.ImaginaryOne * omega * element.Value),
                    "shuntl" => AbcdMatrix.ShuntAdmittance(1.0 / (Complex.ImaginaryOne * omega * element.Value)),
                    "shuntr" => AbcdMatrix.ShuntAdmittance(1.0 / element.Value),
                    _ => throw new InvalidOperationException($"Unsupported element type: {element.Type}")
                };

                total = total * section;
            }

            var denominator = total.A + total.B / z0 + total.C * z0 + total.D;
            var s11 = (total.A + total.B / z0 - total.C * z0 - total.D) / denominator;
            var s21 = 2.0 / denominator;
            var determinant = total.A * total.D - total.B * total.C;
            var s12 = 2.0 * determinant / denominator;
            var s22 = (-total.A + total.B / z0 - total.C * z0 + total.D) / denominator;

            result.Add(new SParameterPoint(f, s11, s21, s12, s22));
        }

        return result;
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
