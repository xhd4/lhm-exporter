using LibreHardwareMonitor.Hardware;

namespace LhmExporter;

public static class CpuTempSelector
{
    public sealed record Candidate(
        double Value,
        HardwareType HardwareType,
        string HardwareName,
        string SensorName,
        bool IsValid);

    public sealed record Result(
        double? Value,
        Candidate? Source,
        string Reason,
        List<Candidate> Candidates);

    private static bool Allowed(HardwareType t) =>
        t is HardwareType.Cpu or HardwareType.Motherboard or HardwareType.SuperIO;

    public static Result Select(Computer computer)
    {
        var cands = new List<Candidate>(128);
        var hasSuperIO = false;
        var hasCpuTempSensor = false;
        var cpuTctlZero = false;

        foreach (var top in computer.Hardware)
        {
            foreach (var hw in Walk(top))
            {
                if (hw.HardwareType == HardwareType.SuperIO)
                    hasSuperIO = true;

                if (!Allowed(hw.HardwareType))
                    continue;

                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || s.Value is null)
                        continue;

                    var v = (double)s.Value.Value;
                    if (!MetricsFormatter.IsValidNumber(v))
                        continue;

                    hasCpuTempSensor = true;
                    var name = (s.Name ?? "").Trim();
                    var valid = v > 1.0 && v < 130.0;

                    if (hw.HardwareType == HardwareType.Cpu &&
                        name.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase) &&
                        v <= 1.0)
                    {
                        cpuTctlZero = true;
                    }

                    cands.Add(new Candidate(v, hw.HardwareType, hw.Name ?? "", name, valid));
                }
            }
        }

        var best = cands
            .Where(c => c.IsValid)
            .OrderByDescending(Score)
            .ThenByDescending(c => c.Value)
            .FirstOrDefault();

        if (best is not null)
            return new Result(best.Value, best, "ok", cands);

        if (!hasCpuTempSensor)
            return new Result(null, null, "no_cpu_temp_sensors", cands);
        if (!hasSuperIO)
            return new Result(null, null, "no_superio", cands);
        if (cpuTctlZero)
            return new Result(null, null, "cpu_tctl_tdie_zero", cands);

        return new Result(null, null, "no_valid_cpu_temp", cands);
    }

    private static int Score(Candidate c)
    {
        var sn = (c.SensorName ?? "").ToLowerInvariant();
        var hn = (c.HardwareName ?? "").ToLowerInvariant();
        var score = 0;

        if (sn is "cpu" or "cpu temperature" || sn.StartsWith("cpu "))
        {
            score += c.HardwareType is HardwareType.SuperIO or HardwareType.Motherboard ? 1000 : 600;
        }

        if (sn.Contains("tctl/tdie"))
            score += 700;
        if (sn.Contains("cpu package"))
            score += 650;
        if (sn.Contains("package"))
            score += 400;
        if (c.HardwareType == HardwareType.Cpu)
            score += 200;
        if (hn.Contains("ite") || hn.Contains("nuvoton") || hn.Contains("superio"))
            score += 150;
        if (sn.Contains("pch") || sn.Contains("vrm") || sn.Contains("system") || sn.Contains("pcie"))
            score -= 800;

        return score;
    }

    private static IEnumerable<IHardware> Walk(IHardware hw)
    {
        yield return hw;
        foreach (var sub in hw.SubHardware)
        {
            foreach (var x in Walk(sub))
                yield return x;
        }
    }
}
