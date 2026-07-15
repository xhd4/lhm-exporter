namespace LhmExporter;

public sealed class LhmCollectorConfig
{
    public int SampleIntervalMs { get; init; } = 2000;
    public bool EnableCpu { get; init; } = true;
    public bool EnableGpu { get; init; } = true;
    public bool EnableMotherboard { get; init; } = true;
    public bool EnableMemory { get; init; } = true;
    public bool EnableStorage { get; init; } = true;
    public bool EnableNetwork { get; init; } = false;
    public bool EnableController { get; init; } = false;
    public string SensorAllowlist { get; init; } = "";
    public string SensorDenylist { get; init; } = "";
    public bool DebugMetrics { get; init; } = false;
}
