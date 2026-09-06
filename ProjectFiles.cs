using System.Text.Json;

namespace RfSimulator;

public static class ProjectFiles
{
    public static (SimulationConfig Config, string Notice) Read(string json, FrequencyConfiguration fallback)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var config = root.ValueKind switch
        {
            JsonValueKind.Array => new SimulationConfig
            {
                StartMHz = fallback.StartMHz, StopMHz = fallback.StopMHz, Points = fallback.Points, Z0 = fallback.Z0,
                Structure = JsonSerializer.Deserialize<List<RfElement>>(json, options) ?? new()
            },
            JsonValueKind.Object => JsonSerializer.Deserialize<SimulationConfig>(json, options)
                ?? throw new InvalidOperationException("Empty configuration."),
            _ => throw new InvalidOperationException("Expected a saved project object or a legacy structure array.")
        };
        var frequency = new FrequencyConfiguration(config.StartMHz, config.StopMHz, config.Points, config.Z0);
        frequency.Validate();
        SimulationEngine.ValidateStructure(config.Structure);
        if (config.LockedIndices.Any(i => i < 0 || i >= config.Structure.Count))
            throw new InvalidOperationException("Saved lock index is outside the circuit.");
        var notice = root.ValueKind == JsonValueKind.Array ? "Legacy structure loaded; retained shared frequency settings." : "Shared frequency settings loaded.";
        // Older official projects only stored the main sweep. Explicitly disclose separate ranges
        // found in extended legacy files rather than silently merging them into the shared range.
        if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any(p =>
            p.Name.Contains("opt", StringComparison.OrdinalIgnoreCase) &&
            (p.Value.ValueKind == JsonValueKind.Object || p.Name.Contains("start", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("stop", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("points", StringComparison.OrdinalIgnoreCase))))
            notice += " Legacy optimizer settings/range were ignored; StartMHz, StopMHz and Points now govern both simulation and optimization.";
        if (config.ManualMarkerMHz is { } marker && (marker < config.StartMHz || marker > config.StopMHz || !double.IsFinite(marker)))
        {
            config.ManualMarkerMHz = null;
            notice += " Saved marker was outside the band; reset to automatic midpoint.";
        }
        return (config, notice);
    }
}
