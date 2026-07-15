namespace LhmExporter;

public sealed class CliParseResult
{
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool PrintConfig { get; set; }
    public bool Install { get; set; }
    public bool Uninstall { get; set; }
    public bool DryRun { get; set; }
    public string? ConfigFilePath { get; set; }
    public Dictionary<string, string?> ConfigOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class CliParser
{
    private static readonly HashSet<string> BoolFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "debug.enabled",
        "collector.lhm.enable-cpu",
        "collector.lhm.enable-gpu",
        "collector.lhm.enable-motherboard",
        "collector.lhm.enable-memory",
        "collector.lhm.enable-storage",
        "collector.lhm.enable-network",
        "collector.lhm.enable-controller",
        "collector.lhm.debug-metrics",
    };

    public static CliParseResult Parse(string[] args)
    {
        var result = new CliParseResult();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                    result.ShowHelp = true;
                    return result;
                case "--version":
                    result.ShowVersion = true;
                    return result;
                case "--print-config":
                    result.PrintConfig = true;
                    break;
                case "--install":
                    result.Install = true;
                    break;
                case "--uninstall":
                    result.Uninstall = true;
                    break;
                case "--dry-run":
                    result.DryRun = true;
                    break;
                default:
                    ParseOption(result, arg, args, ref i);
                    break;
            }
        }

        return result;
    }

    public static void PrintHelp(TextWriter output)
    {
        output.WriteLine("""
            lhm-exporter - Prometheus exporter for LibreHardwareMonitor (Windows)
            Configuration follows prometheus-community/windows_exporter conventions.

            Usage:
              lhm-exporter.exe [options]
              lhm-exporter.exe --install [options] [--dry-run]
              lhm-exporter.exe --uninstall [--dry-run]

            Operational:
              --install                 Install to Program Files, register service and firewall
              --uninstall               Remove service, firewall, Program Files and ProgramData
              --dry-run                   Preview install/uninstall without changes
              --config.file PATH          YAML config (default: ./config.yaml or installed path)
              --help, -h                  Show this help
              --version                   Show version
              --print-config              Print resolved config.yaml and exit

            Global (windows_exporter-compatible):
              --web.listen-address ADDR   e.g. :9182, 0.0.0.0:9182
              --telemetry.path PATH       Full metrics path, default /metrics
              --log.file TARGET           stdout, stderr, or file path
              --log.level LEVEL           debug, info, warn, error
              --scrape.timeout-margin SEC
              --debug.enabled BOOL
              --collectors.enabled LIST   lhm or [defaults]

            Collector lhm:
              --collector.lhm.sample-interval-ms MS
              --collector.lhm.enable-cpu BOOL
              --collector.lhm.enable-gpu BOOL
              --collector.lhm.enable-motherboard BOOL
              --collector.lhm.enable-memory BOOL
              --collector.lhm.enable-storage BOOL
              --collector.lhm.enable-network BOOL
              --collector.lhm.enable-controller BOOL
              --collector.lhm.sensor-allowlist REGEX
              --collector.lhm.sensor-denylist REGEX
              --collector.lhm.debug-metrics BOOL

            Flags accept --name=value or --name value. Boolean flags without value default to true.
            """);
    }

    public static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static void ParseOption(CliParseResult result, string arg, string[] args, ref int index)
    {
        if (!arg.StartsWith("--", StringComparison.Ordinal) && !arg.StartsWith("-", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown argument: {arg}. Use --help for usage.");

        string name;
        string? inlineValue = null;

        var eq = arg.IndexOf('=');
        if (eq > 0)
        {
            name = arg[..eq].TrimStart('-');
            inlineValue = arg[(eq + 1)..];
        }
        else
        {
            name = arg.TrimStart('-');
            if (!BoolFlags.Contains(name))
            {
                if (index + 1 >= args.Length)
                    throw new InvalidOperationException($"Missing value for --{name}");

                inlineValue = args[++index];
            }
        }

        if (name.Equals("config.file", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(inlineValue))
                throw new InvalidOperationException("--config.file requires a path");
            result.ConfigFilePath = inlineValue;
            return;
        }

        var flatKey = name.Replace('_', '-');

        if (BoolFlags.Contains(flatKey))
        {
            var boolValue = string.IsNullOrWhiteSpace(inlineValue) || ParseBool(inlineValue, true);
            result.ConfigOverrides[flatKey] = boolValue.ToString().ToLowerInvariant();
            return;
        }

        if (inlineValue is null)
            throw new InvalidOperationException($"Missing value for --{name}");

        result.ConfigOverrides[flatKey] = inlineValue;
    }

    private static bool ParseBool(string raw, bool fallback)
    {
        raw = raw.Trim();
        if (raw is "1" or "true" or "yes" or "on")
            return true;
        if (raw is "0" or "false" or "no" or "off")
            return false;
        return fallback;
    }
}
