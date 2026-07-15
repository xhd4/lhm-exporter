using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LhmExporter;

public sealed class SensorSampler(
    ILogger<SensorSampler> log,
    SensorSamplerOptions options,
    Computer computer) : BackgroundService
{
    private readonly int _intervalMs = Math.Max(250, options.IntervalMs);
    private readonly bool _debugMetrics = options.DebugMetrics;
    private readonly Regex? _allow = options.Allow;
    private readonly Regex? _deny = options.Deny;
    private readonly UpdateVisitor _visitor = new();

    private volatile List<MetricsFormatter.MetricSample> _last = [];
    private volatile bool _isHealthy = true;

    public IReadOnlyList<MetricsFormatter.MetricSample> Last => _last;
    public bool IsHealthy => _isHealthy;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Sampler started, interval={Interval}ms debugMetrics={DebugMetrics}", _intervalMs, _debugMetrics);

        try
        {
            computer.Open();
            log.LogInformation("LibreHardwareMonitor Computer opened");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to open LHM Computer");
            _isHealthy = false;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                try
                {
                    computer.Accept(_visitor);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Computer.Accept(visitor) failed");
                }

                var samples = new List<MetricsFormatter.MetricSample>(25000);

                samples.Add(new MetricsFormatter.MetricSample(
                    "libre_hw_sampler_up",
                    1,
                    "1 if sampler loop is running"));

                var nowTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                samples.Add(new MetricsFormatter.MetricSample(
                    "libre_hw_sampler_last_success_timestamp_seconds",
                    nowTs,
                    "UTC timestamp of last successful sample"));

                try
                {
                    var uptimeMs = Environment.TickCount64;
                    if (uptimeMs > 0)
                    {
                        var bootTime = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(uptimeMs);
                        samples.Add(new MetricsFormatter.MetricSample(
                            "node_boot_time_seconds",
                            bootTime.ToUnixTimeSeconds(),
                            "Unix time of last boot"));
                    }
                }
                catch
                {
                    // Ignore uptime failures so scrapes keep working.
                }

                var tempCount = 0;
                var loadCount = 0;
                var powerCount = 0;
                var voltCount = 0;
                var fanCount = 0;

                if (_debugMetrics)
                {
                    samples.AddRange(HardwareDebug.PresentHardware(computer));
                    samples.AddRange(CollectRawTemperatureDebug(computer));
                }

                foreach (var hwTop in computer.Hardware)
                {
                    foreach (var hw in Walk(hwTop))
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (s.Value is null)
                                continue;

                            var v = (double)s.Value.Value;
                            if (!MetricsFormatter.IsValidNumber(v))
                                continue;

                            switch (s.SensorType)
                            {
                                case SensorType.Temperature: tempCount++; break;
                                case SensorType.Load: loadCount++; break;
                                case SensorType.Power: powerCount++; break;
                                case SensorType.Voltage: voltCount++; break;
                                case SensorType.Fan: fanCount++; break;
                            }
                        }

                        foreach (var ms in FlattenHardware(hw))
                        {
                            if (_allow != null && !_allow.IsMatch(ms.Name))
                                continue;
                            if (_deny != null && _deny.IsMatch(ms.Name))
                                continue;
                            samples.Add(ms);
                        }
                    }
                }

                samples.Add(new MetricsFormatter.MetricSample("libre_hw_sensor_count_temperature", tempCount, "Number of temperature sensors with values"));
                samples.Add(new MetricsFormatter.MetricSample("libre_hw_sensor_count_load", loadCount, "Number of load sensors with values"));
                samples.Add(new MetricsFormatter.MetricSample("libre_hw_sensor_count_power", powerCount, "Number of power sensors with values"));
                samples.Add(new MetricsFormatter.MetricSample("libre_hw_sensor_count_voltage", voltCount, "Number of voltage sensors with values"));
                samples.Add(new MetricsFormatter.MetricSample("libre_hw_sensor_count_fan", fanCount, "Number of fan sensors with values"));

                var sel = CpuTempSelector.Select(computer);
                samples.Add(new MetricsFormatter.MetricSample(
                    "libre_hw_cpu_temperature_available",
                    sel.Value is not null ? 1 : 0,
                    "1 if exporter has a valid CPU temperature"));

                if (_debugMetrics)
                {
                    samples.Add(new MetricsFormatter.MetricSample(
                        "libre_hw_cpu_temperature_reason",
                        1,
                        "Reason why CPU temperature is missing (debug)",
                        new Dictionary<string, string> { ["reason"] = sel.Reason }));

                    var rank = 0;
                    foreach (var c in sel.Candidates
                                 .OrderByDescending(x => x.IsValid)
                                 .ThenByDescending(x => x.Value)
                                 .Take(50))
                    {
                        samples.Add(new MetricsFormatter.MetricSample(
                            "libre_hw_cpu_temperature_candidate_celsius",
                            c.Value,
                            "Candidate CPU temperature sensors (debug)",
                            new Dictionary<string, string>
                            {
                                ["rank"] = (++rank).ToString(),
                                ["valid"] = c.IsValid ? "1" : "0",
                                ["hardware_type"] = c.HardwareType.ToString(),
                                ["hardware"] = c.HardwareName,
                                ["sensor"] = c.SensorName,
                            }));
                    }
                }

                if (sel.Value is not null && sel.Source is not null)
                {
                    samples.Add(new MetricsFormatter.MetricSample(
                        "libre_hw_cpu_temperature_celsius",
                        sel.Value.Value,
                        "Best-effort CPU temperature in Celsius"));

                    samples.Add(new MetricsFormatter.MetricSample(
                        "libre_hw_cpu_temperature_celsius_source",
                        sel.Value.Value,
                        "CPU temperature with source labels (same value)",
                        new Dictionary<string, string>
                        {
                            ["hardware_type"] = sel.Source.HardwareType.ToString(),
                            ["hardware"] = sel.Source.HardwareName,
                            ["sensor"] = sel.Source.SensorName,
                        }));
                }
                else
                {
                    samples.Add(new MetricsFormatter.MetricSample(
                        "libre_hw_cpu_temperature_celsius_missing",
                        1,
                        "1 if no suitable CPU temperature sensor was found"));
                }

                _last = samples;
                _isHealthy = true;
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Sampling error");
                _isHealthy = false;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _last =
                [
                    new MetricsFormatter.MetricSample("libre_hw_sampler_up", 0, "1 if sampler loop is running"),
                    new MetricsFormatter.MetricSample("libre_hw_sampler_last_success_timestamp_seconds", now, "UTC timestamp of last successful sample"),
                ];
            }

            try
            {
                await Task.Delay(_intervalMs, stoppingToken);
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        try
        {
            computer.Close();
        }
        catch
        {
            // Ignore close failures.
        }

        log.LogInformation("Sampler stopped");
    }

    private static IEnumerable<MetricsFormatter.MetricSample> CollectRawTemperatureDebug(Computer computer)
    {
        foreach (var hwTop in computer.Hardware)
        {
            foreach (var hw in Walk(hwTop))
            {
                if (hw.HardwareType is not (HardwareType.Cpu or HardwareType.Motherboard))
                    continue;

                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || s.Value is null)
                        continue;

                    var v = (double)s.Value.Value;
                    yield return new MetricsFormatter.MetricSample(
                        "libre_hw_debug_temperature_raw",
                        MetricsFormatter.IsValidNumber(v) ? v : double.NaN,
                        "Raw temperature values from CPU/Motherboard (debug)",
                        new Dictionary<string, string>
                        {
                            ["hardware_type"] = hw.HardwareType.ToString(),
                            ["hardware"] = hw.Name ?? "",
                            ["sensor"] = s.Name ?? "",
                        });
                }
            }
        }
    }

    private static IEnumerable<MetricsFormatter.MetricSample> FlattenHardware(IHardware hw)
    {
        var hwName = MetricsFormatter.Sanitize(hw.Name ?? "");
        var hwType = MetricsFormatter.Sanitize(hw.HardwareType.ToString());

        foreach (var s in hw.Sensors)
        {
            if (s.Value is null)
                continue;

            var v = (double)s.Value.Value;
            if (!MetricsFormatter.IsValidNumber(v))
                continue;

            var sensorType = MetricsFormatter.Sanitize(s.SensorType.ToString());
            var sensorName = MetricsFormatter.Sanitize(s.Name ?? "");
            var metric = $"libre_hw_{hwType}_{hwName}_{sensorType}_{sensorName}";

            // Network traffic sensors are exported as counters for Prometheus.
            if (hw.HardwareType == HardwareType.Network && s.SensorType == SensorType.Data)
            {
                var originalName = s.Name ?? "";
                if (originalName.Contains("Downloaded", StringComparison.OrdinalIgnoreCase) ||
                    originalName.Contains("Uploaded", StringComparison.OrdinalIgnoreCase) ||
                    originalName.Contains("Download", StringComparison.OrdinalIgnoreCase) ||
                    originalName.Contains("Upload", StringComparison.OrdinalIgnoreCase))
                {
                    metric += "_total";
                }
            }

            yield return new MetricsFormatter.MetricSample(metric, v);
        }
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
