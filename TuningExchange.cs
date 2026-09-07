using System.Text;
using System.Text.Json;

namespace RfSimulator;

public sealed record TuningInstruction(string Id, string Message);

public static class TuningExchange
{
    public static async Task<string> WaitAsync(string directory, string id, TimeSpan timeout, CancellationToken cancellation = default)
    {
        ValidateId(id);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(1)) throw new InvalidOperationException("Wait must be positive and at most 3600 seconds.");
        if (!File.Exists(Path.Combine(directory, "session.json"))) throw new InvalidOperationException("Session folder not found.");
        var observer = Path.Combine(directory, "observer-" + Guid.NewGuid().ToString("N") + ".json");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (timer.Elapsed < timeout)
            {
                cancellation.ThrowIfCancellationRequested();
                File.WriteAllText(observer, JsonSerializer.Serialize(new { stepId = id, updatedUtc = DateTimeOffset.UtcNow }));
                var path = Path.Combine(directory, id, "result.json");
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    using var result = JsonDocument.Parse(text);
                    var state = result.RootElement.GetProperty("state").GetString();
                    if (state is "complete" or "failed" or "cancelled" or "skipped" or "stopped") return text;
                }
                await Task.Delay(500, cancellation);
            }
            return JsonSerializer.Serialize(new { state = "wait_timeout", stepId = id, detail = "Observer stopped waiting; command was not cancelled. Wait again with the same ID." });
        }
        finally { if (File.Exists(observer)) File.Delete(observer); }
    }

    public static bool HasObserver(string directory, string? id)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "observer-*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                var age = DateTimeOffset.UtcNow - root.GetProperty("updatedUtc").GetDateTimeOffset();
                if (age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(3) &&
                    (id is null || root.GetProperty("stepId").GetString() == id)) return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        }
        return false;
    }
    public static string Submit(string directory, string message)
    {
        if (!File.Exists(Path.Combine(directory, "session.json"))) throw new InvalidOperationException("Start a tuning session in the GUI first.");
        if (string.IsNullOrWhiteSpace(message) || message.Length > 2000) throw new InvalidOperationException("Instruction must contain 1–2000 characters.");
        var command = new TuningInstruction(Guid.NewGuid().ToString("N"), message);
        var temporary = Path.Combine(directory, command.Id + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(command));
            File.Move(temporary, Path.Combine(directory, "command.json"), false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return command.Id;
    }

    public static TuningInstruction? Read(string directory)
    {
        var path = Path.Combine(directory, "command.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16000) throw new InvalidOperationException("Instruction file too large.");
        var command = JsonSerializer.Deserialize<TuningInstruction>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Invalid instruction.");
        ValidateId(command.Id);
        if (string.IsNullOrWhiteSpace(command.Message) || command.Message.Length > 2000) throw new InvalidOperationException("Invalid message.");
        return command;
    }

    public static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Invalid step ID.");
    }

    public static string Claim(string directory, TuningInstruction command)
    {
        ValidateId(command.Id);
        var step = Path.Combine(directory, command.Id);
        Directory.CreateDirectory(step);
        using var claim = new FileStream(Path.Combine(step, "instruction.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(claim, command);
        return step;
    }

    public static void Result(string step, string state, string detail, object? metadata = null)
    {
        var temporary = Path.Combine(step, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { state, detail, timestamp = DateTimeOffset.Now, metadata }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, Path.Combine(step, "result.json"), true);
    }

    public static void SaveSweep(string path, MeasuredSweep sweep)
    {
        using var stream = new FileStream(path, FileMode.CreateNew);
        using var writer = new StreamWriter(stream, Encoding.ASCII);
        writer.WriteLine("! S11/S21 only; reverse parameters are placeholders. Device calibration unverified.");
        writer.WriteLine("! " + sweep.Source.Replace('\r', ' ').Replace('\n', ' '));
        writer.WriteLine(FormattableString.Invariant($"# HZ S RI R {sweep.ReferenceOhms:R}"));
        foreach (var point in sweep.Points)
            writer.WriteLine(FormattableString.Invariant($"{point.FrequencyHz:R} {point.S11.Real:R} {point.S11.Imaginary:R} {point.S21.Real:R} {point.S21.Imaginary:R} 0 0 0 0"));
    }
}
