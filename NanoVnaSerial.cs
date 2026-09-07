using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace RfSimulator;

public static class NanoVnaSerial
{
    public sealed class Session : IDisposable
    {
        private readonly SerialPort _port;
        public string Description { get; }
        public bool IsOpen => _port.IsOpen;
        public Session(string name, CancellationToken cancellation)
        {
            _port = Open(name);
            try
            {
                Synchronize(_port, cancellation);
                var version = Command(_port, "version", cancellation);
                if (!HasScan(Command(_port, "help", cancellation))) throw new InvalidOperationException("Firmware does not advertise scan.");
                Description = $"{name} | firmware {string.Join(" ", version)} | F V2 profile";
            }
            catch { _port.Dispose(); throw; }
        }
        public MeasuredSweep Measure(FrequencyConfiguration frequency, CancellationToken cancellation, IProgress<string>? progress) =>
            AcquirePort(_port, frequency, cancellation, progress);
        public void Dispose() => _port.Dispose();
    }
    public static string Probe(string portName, CancellationToken cancellation)
    {
        using var port = Open(portName);
        Synchronize(port, cancellation);
        var version = Command(port, "version", cancellation);
        var help = Command(port, "help", cancellation);
        return $"{portName}, 115200 baud; firmware: {string.Join(" ", version)}\r\nText shell responded; F V2 profile is user-selected, not automatically verified. Scan command: {HasScan(help)}. Port released. Calibration unknown.";
    }

    public static void ValidateSweep(FrequencyConfiguration frequency)
    {
        frequency.Validate();
        if (frequency.Z0 != 50) throw new InvalidOperationException("Hardware acquisition currently requires the shared reference to be 50 ohms.");
        if (frequency.StartHz < 50000 || frequency.StopHz > 3e9)
            throw new InvalidOperationException("F V2 acquisition is restricted to 50 kHz–3 GHz.");
        if (frequency.Points < 101 || frequency.Points > 10001 || (frequency.Points - 1) % 100 != 0)
            throw new InvalidOperationException("Use 101, 201, 301, 401, ... points (maximum 10001). Acquisition uses overlapping 101-point segments.");
        if ((frequency.StopHz - frequency.StartHz) / (frequency.Points - 1) < 1)
            throw new InvalidOperationException("Hardware frequency spacing must be at least 1 Hz.");
    }

    public static MeasuredSweep Acquire(string portName, FrequencyConfiguration frequency, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        ValidateSweep(frequency);
        using var port = Open(portName);
        Synchronize(port, cancellation);
        if (!HasScan(Command(port, "help", cancellation)))
            throw new InvalidOperationException("This firmware does not advertise scan. No sweep settings were sent.");
        return AcquirePort(port, frequency, cancellation, progress);
    }

    private static MeasuredSweep AcquirePort(SerialPort port, FrequencyConfiguration frequency, CancellationToken cancellation, IProgress<string>? progress)
    {
        ValidateSweep(frequency);
        var points = new List<SweepPoint>();
        var segments = (frequency.Points - 1) / 100;
        for (var segment = 0; segment < segments; segment++)
        {
            cancellation.ThrowIfCancellationRequested();
            var start = (long)Math.Round(frequency.StartHz + (frequency.StopHz - frequency.StartHz) * segment / segments);
            var stop = (long)Math.Round(frequency.StartHz + (frequency.StopHz - frequency.StartHz) * (segment + 1) / segments);
            progress?.Report($"Measuring segment {segment + 1}/{segments}: {start / 1e6:g}–{stop / 1e6:g} MHz");
            var reply = Command(port, FormattableString.Invariant($"scan {start} {stop} 101"), cancellation, 60);
            if (reply.Any(line => Regex.IsMatch(line, "error|usage|unknown|invalid", RegexOptions.IgnoreCase)))
                throw new InvalidOperationException("Scan rejected: " + string.Join(" ", reply));
            var frequencies = Command(port, "frequencies", cancellation);
            var channel0 = Command(port, "data 0", cancellation);
            var channel1 = Command(port, "data 1", cancellation);
            var part = ParseSegment(frequencies, channel0, channel1, start, stop);
            points.AddRange(segment == 0 ? part : part.Skip(1));
        }
        return new MeasuredSweep($"NanoVNA F V2 profile | {port.PortName} | {DateTimeOffset.Now:O} | device calibration unverified", 50, points.AsReadOnly());
    }

    public static IReadOnlyList<SweepPoint> ParseSegment(IReadOnlyList<string> frequencies, IReadOnlyList<string> channel0, IReadOnlyList<string> channel1, long start, long stop)
    {
        if (frequencies.Count != 101 || channel0.Count != 101 || channel1.Count != 101)
            throw new InvalidOperationException("Incomplete scan: expected 101 frequencies and 101 values per channel. Previous measurement retained.");
        double Number(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
                throw new InvalidOperationException("Invalid numeric instrument response.");
            return parsed;
        }
        Complex Pair(string line)
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2) throw new InvalidOperationException("Expected real and imaginary channel values.");
            return new Complex(Number(fields[0]), Number(fields[1]));
        }
        var result = new List<SweepPoint>();
        for (var index = 0; index < 101; index++)
        {
            var value = Number(frequencies[index]);
            var expected = start + (stop - start) * (double)index / 100;
            if (Math.Abs(value - expected) > 101 || value < start || value > stop || (index > 0 && value <= result[^1].FrequencyHz))
                throw new InvalidOperationException("Returned frequency grid does not match requested scan.");
            result.Add(new SweepPoint(value, Pair(channel0[index]), Pair(channel1[index])));
        }
        return result;
    }

    private static bool HasScan(IEnumerable<string> help) => help.Any(line => Regex.IsMatch(line, @"\bscan\b", RegexOptions.IgnoreCase));

    private static SerialPort Open(string name)
    {
        var port = new SerialPort(name, 115200) { ReadTimeout = 200, WriteTimeout = 1000, DtrEnable = false, RtsEnable = false, Encoding = Encoding.ASCII };
        try { port.Open(); return port; }
        catch { port.Dispose(); throw; }
    }

    private static void Synchronize(SerialPort port, CancellationToken cancellation)
    {
        port.DiscardInBuffer();
        Command(port, "", cancellation);
    }

    private static string[] Command(SerialPort port, string command, CancellationToken cancellation, int timeoutSeconds = 5)
    {
        cancellation.ThrowIfCancellationRequested();
        port.Write(command + "\r");
        var buffer = new StringBuilder();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < timeoutSeconds)
        {
            cancellation.ThrowIfCancellationRequested();
            try { buffer.Append((char)port.ReadChar()); }
            catch (TimeoutException) { continue; }
            if (buffer.Length > 262144) throw new InvalidOperationException("Instrument reply exceeds safety limit.");
            if (buffer.Length >= 3 && buffer[^3] == 'c' && buffer[^2] == 'h' && buffer[^1] == '>')
                return buffer.ToString()[..^3].Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim()).Where(line => line.Length > 0 && line != command).ToArray();
        }
        throw new TimeoutException($"No complete reply to '{command}'. Check selected port and close NanoVNA Saver. Port will be released.");
    }
}
