using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LhmExporter;

public sealed class AppConfig
{
    public required string ExeDirectory { get; init; }
    public required string ConfigFilePath { get; init; }

    public string WebListenAddress { get; init; } = ":9182";
    public string TelemetryPath { get; init; } = "/metrics";
    public string LogFile { get; init; } = AppPaths.DefaultLogFile;
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;
    public double ScrapeTimeoutMargin { get; init; } = 0.5;
    public bool DebugEnabled { get; init; } = false;
    public string CollectorsEnabled { get; init; } = "lhm";

    public LhmCollectorConfig Lhm { get; init; } = new();

    public bool LogToFile =>
        !LogFile.Equals("stdout", StringComparison.OrdinalIgnoreCase) &&
        !LogFile.Equals("stderr", StringComparison.OrdinalIgnoreCase);

    public string ResolvedLogFile => AppPaths.ResolveLogFile(LogFile, ExeDirectory);

    public int ListenPort => ParseListenPort(WebListenAddress);

    public string BuildKestrelUrl()
    {
        var addr = WebListenAddress.Trim();
        if (addr.StartsWith(':'))
            return $"http://0.0.0.0{addr}";

        if (addr.StartsWith("[", StringComparison.Ordinal))
        {
            var end = addr.IndexOf(']');
            if (end < 0)
                throw new InvalidOperationException($"Invalid web.listen-address: {WebListenAddress}");

            var host = addr[..(end + 1)];
            var rest = addr[(end + 1)..];
            if (rest.StartsWith(':'))
                return $"http://{host}{rest}";

            throw new InvalidOperationException($"Invalid web.listen-address: {WebListenAddress}");
        }

        if (!addr.Contains(':'))
            return $"http://0.0.0.0:{addr}";

        return $"http://{addr}";
    }

    public static AppConfig CreateDefaults(string exeDir, string? configFilePath = null) =>
        new()
        {
            ExeDirectory = exeDir,
            ConfigFilePath = configFilePath ?? AppPaths.DefaultConfigNextToExe(exeDir),
        };

    public AppConfig WithFlatOverrides(IReadOnlyDictionary<string, string?> overrides)
    {
        var current = ToFlatDictionary();
        foreach (var (key, value) in overrides)
        {
            if (value is not null)
                current[NormalizeFlatKey(key)] = value;
        }

        return FromFlatDictionary(ExeDirectory, ConfigFilePath, current);
    }

    public void Validate()
    {
        var port = ListenPort;
        if (port is < 1 or > 65535)
            throw new InvalidOperationException($"Invalid listen port: {port}");

        if (!TelemetryPath.StartsWith('/'))
            throw new InvalidOperationException($"telemetry.path must start with '/': {TelemetryPath}");

        if (Lhm.SampleIntervalMs < 250)
            throw new InvalidOperationException($"collector.lhm.sample-interval-ms must be >= 250 (got {Lhm.SampleIntervalMs})");

        if (!string.IsNullOrWhiteSpace(Lhm.SensorAllowlist))
            _ = new Regex(Lhm.SensorAllowlist, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        if (!string.IsNullOrWhiteSpace(Lhm.SensorDenylist))
            _ = new Regex(Lhm.SensorDenylist, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        _ = BuildKestrelUrl();
    }

    public string SerializeToYaml()
    {
        var model = new YamlConfigRoot
        {
            Collectors = new YamlCollectors { Enabled = CollectorsEnabled },
            Collector = new YamlCollectorSection
            {
                Lhm = new YamlLhmCollector
                {
                    SampleIntervalMs = Lhm.SampleIntervalMs,
                    EnableCpu = Lhm.EnableCpu,
                    EnableGpu = Lhm.EnableGpu,
                    EnableMotherboard = Lhm.EnableMotherboard,
                    EnableMemory = Lhm.EnableMemory,
                    EnableStorage = Lhm.EnableStorage,
                    EnableNetwork = Lhm.EnableNetwork,
                    EnableController = Lhm.EnableController,
                    SensorAllowlist = Lhm.SensorAllowlist,
                    SensorDenylist = Lhm.SensorDenylist,
                    DebugMetrics = Lhm.DebugMetrics,
                },
            },
            Log = new YamlLog
            {
                Level = LogLevelToYaml(LogLevel),
                File = LogToFile ? ResolvedLogFile : LogFile,
            },
            Telemetry = new YamlTelemetry { Path = TelemetryPath },
            Web = new YamlWeb { ListenAddress = WebListenAddress },
            Scrape = new YamlScrape { TimeoutMargin = ScrapeTimeoutMargin },
            Debug = new YamlDebug { Enabled = DebugEnabled },
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        return serializer.Serialize(model);
    }

    public string ToDisplayString() => SerializeToYaml();

    private Dictionary<string, string> ToFlatDictionary()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["web.listen-address"] = WebListenAddress,
            ["telemetry.path"] = TelemetryPath,
            ["log.file"] = LogFile,
            ["log.level"] = LogLevelToYaml(LogLevel),
            ["scrape.timeout-margin"] = ScrapeTimeoutMargin.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["debug.enabled"] = DebugEnabled.ToString().ToLowerInvariant(),
            ["collectors.enabled"] = CollectorsEnabled,
            ["collector.lhm.sample-interval-ms"] = Lhm.SampleIntervalMs.ToString(),
            ["collector.lhm.enable-cpu"] = Lhm.EnableCpu.ToString().ToLowerInvariant(),
            ["collector.lhm.enable-gpu"] = Lhm.EnableGpu.ToString().ToLowerInvariant(),
            ["collector.lhm.enable-motherboard"] = Lhm.EnableMotherboard.ToString().ToLowerInvariant(),
            ["collector.lhm.enable-memory"] = Lhm.EnableMemory.ToString().ToLowerInvariant(),
            ["collector.lhm.enable-storage"] = Lhm.EnableStorage.ToString().ToLowerInvariant(),
            ["collector.lhm.enable-network"] = Lhm.EnableNetwork.ToString().ToLowerInvariant(),
            ["collector.lhm.enable-controller"] = Lhm.EnableController.ToString().ToLowerInvariant(),
            ["collector.lhm.sensor-allowlist"] = Lhm.SensorAllowlist,
            ["collector.lhm.sensor-denylist"] = Lhm.SensorDenylist,
            ["collector.lhm.debug-metrics"] = Lhm.DebugMetrics.ToString().ToLowerInvariant(),
        };
        return d;
    }

    private static AppConfig FromFlatDictionary(string exeDir, string configPath, IReadOnlyDictionary<string, string> values)
    {
        var defaults = CreateDefaults(exeDir, configPath);
        var lhmDefaults = defaults.Lhm;

        return new AppConfig
        {
            ExeDirectory = exeDir,
            ConfigFilePath = configPath,
            WebListenAddress = GetString(values, "web.listen-address", defaults.WebListenAddress),
            TelemetryPath = GetString(values, "telemetry.path", defaults.TelemetryPath),
            LogFile = GetString(values, "log.file", defaults.LogFile),
            LogLevel = ParseLogLevel(GetString(values, "log.level", LogLevelToYaml(defaults.LogLevel))),
            ScrapeTimeoutMargin = GetDouble(values, "scrape.timeout-margin", defaults.ScrapeTimeoutMargin),
            DebugEnabled = GetBool(values, "debug.enabled", defaults.DebugEnabled),
            CollectorsEnabled = GetString(values, "collectors.enabled", defaults.CollectorsEnabled),
            Lhm = new LhmCollectorConfig
            {
                SampleIntervalMs = GetInt(values, "collector.lhm.sample-interval-ms", lhmDefaults.SampleIntervalMs),
                EnableCpu = GetBool(values, "collector.lhm.enable-cpu", lhmDefaults.EnableCpu),
                EnableGpu = GetBool(values, "collector.lhm.enable-gpu", lhmDefaults.EnableGpu),
                EnableMotherboard = GetBool(values, "collector.lhm.enable-motherboard", lhmDefaults.EnableMotherboard),
                EnableMemory = GetBool(values, "collector.lhm.enable-memory", lhmDefaults.EnableMemory),
                EnableStorage = GetBool(values, "collector.lhm.enable-storage", lhmDefaults.EnableStorage),
                EnableNetwork = GetBool(values, "collector.lhm.enable-network", lhmDefaults.EnableNetwork),
                EnableController = GetBool(values, "collector.lhm.enable-controller", lhmDefaults.EnableController),
                SensorAllowlist = GetString(values, "collector.lhm.sensor-allowlist", lhmDefaults.SensorAllowlist),
                SensorDenylist = GetString(values, "collector.lhm.sensor-denylist", lhmDefaults.SensorDenylist),
                DebugMetrics = GetBool(values, "collector.lhm.debug-metrics", lhmDefaults.DebugMetrics),
            },
        };
    }

    private static string NormalizeFlatKey(string key) =>
        key.Replace('_', '-');

    private static int ParseListenPort(string listenAddress)
    {
        var addr = listenAddress.Trim();
        if (addr.StartsWith(':'))
            return int.Parse(addr[1..]);

        if (addr.StartsWith("[", StringComparison.Ordinal))
        {
            var end = addr.IndexOf(']');
            if (end < 0 || end + 1 >= addr.Length || addr[end + 1] != ':')
                throw new InvalidOperationException($"Invalid web.listen-address: {listenAddress}");

            return int.Parse(addr[(end + 2)..]);
        }

        var idx = addr.LastIndexOf(':');
        if (idx < 0 || idx == addr.Length - 1)
            throw new InvalidOperationException($"Invalid web.listen-address: {listenAddress}");

        return int.Parse(addr[(idx + 1)..]);
    }

    private static string GetString(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        key = NormalizeFlatKey(key);
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();
        return fallback;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(NormalizeFlatKey(key), out var value) && int.TryParse(value.Trim(), out var parsed)
            ? parsed
            : fallback;

    private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(NormalizeFlatKey(key), out var value) &&
        double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
    {
        if (!values.TryGetValue(NormalizeFlatKey(key), out var value))
            return fallback;

        value = value.Trim();
        if (value is "1" or "true" or "yes" or "on")
            return true;
        if (value is "0" or "false" or "no" or "off")
            return false;
        return fallback;
    }

    private static LogLevel ParseLogLevel(string raw)
    {
        raw = raw.Trim().ToLowerInvariant();
        return raw switch
        {
            "trace" or "trc" => LogLevel.Trace,
            "debug" or "dbg" => LogLevel.Debug,
            "info" or "inf" => LogLevel.Information,
            "warn" or "wrn" => LogLevel.Warning,
            "error" or "err" => LogLevel.Error,
            "fatal" or "crit" => LogLevel.Critical,
            _ => Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var level) ? level : LogLevel.Warning,
        };
    }

    private static string LogLevelToYaml(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "fatal",
        _ => "warn",
    };

    private sealed class YamlConfigRoot
    {
        public YamlCollectors? Collectors { get; set; }
        public YamlCollectorSection? Collector { get; set; }
        public YamlLog? Log { get; set; }
        public YamlTelemetry? Telemetry { get; set; }
        public YamlWeb? Web { get; set; }
        public YamlScrape? Scrape { get; set; }
        public YamlDebug? Debug { get; set; }
    }

    private sealed class YamlCollectors
    {
        public string? Enabled { get; set; }
    }

    private sealed class YamlCollectorSection
    {
        public YamlLhmCollector? Lhm { get; set; }
    }

    private sealed class YamlLhmCollector
    {
        public int SampleIntervalMs { get; set; }
        public bool EnableCpu { get; set; }
        public bool EnableGpu { get; set; }
        public bool EnableMotherboard { get; set; }
        public bool EnableMemory { get; set; }
        public bool EnableStorage { get; set; }
        public bool EnableNetwork { get; set; }
        public bool EnableController { get; set; }
        public string? SensorAllowlist { get; set; }
        public string? SensorDenylist { get; set; }
        public bool DebugMetrics { get; set; }
    }

    private sealed class YamlLog
    {
        public string? Level { get; set; }
        public string? File { get; set; }
    }

    private sealed class YamlTelemetry
    {
        public string? Path { get; set; }
    }

    private sealed class YamlWeb
    {
        [YamlMember(Alias = "listen-address")]
        public string? ListenAddress { get; set; }
    }

    private sealed class YamlScrape
    {
        [YamlMember(Alias = "timeout-margin")]
        public double TimeoutMargin { get; set; }
    }

    private sealed class YamlDebug
    {
        public bool Enabled { get; set; }
    }
}
