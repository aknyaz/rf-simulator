using System.Text;
using System.Text.Json;

namespace RfSimulator;

public sealed record TuningInstruction(string Id, string Message);

public static class TuningExchange
{
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
