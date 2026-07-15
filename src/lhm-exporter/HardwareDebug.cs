using LibreHardwareMonitor.Hardware;

namespace LhmExporter;

public static class HardwareDebug
{
    public static IEnumerable<MetricsFormatter.MetricSample> PresentHardware(Computer computer)
    {
        foreach (var top in computer.Hardware)
        {
            foreach (var hw in Walk(top))
            {
                yield return new MetricsFormatter.MetricSample(
                    "libre_hw_hardware_present",
                    1,
                    "1 if a hardware node exists (debug)",
                    new Dictionary<string, string>
                    {
                        ["type"] = hw.HardwareType.ToString(),
                        ["name"] = hw.Name ?? ""
                    }
                );
            }
        }
    }

    private static IEnumerable<IHardware> Walk(IHardware hw)
    {
        yield return hw;
        foreach (var sub in hw.SubHardware)
            foreach (var x in Walk(sub))
                yield return x;
    }
}
