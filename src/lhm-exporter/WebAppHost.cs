using LibreHardwareMonitor.Hardware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace LhmExporter;

public static class WebAppHost
{
    public static async Task RunAsync(AppConfig config, string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService();

        ConfigureLogging(builder, config);

        var lhm = config.Lhm;
        Regex? allow = null;
        Regex? deny = null;
        if (!string.IsNullOrWhiteSpace(lhm.SensorAllowlist))
            allow = new Regex(lhm.SensorAllowlist, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        if (!string.IsNullOrWhiteSpace(lhm.SensorDenylist))
            deny = new Regex(lhm.SensorDenylist, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var computer = new Computer
        {
            IsCpuEnabled = lhm.EnableCpu,
            IsGpuEnabled = lhm.EnableGpu,
            IsMotherboardEnabled = lhm.EnableMotherboard,
            IsMemoryEnabled = lhm.EnableMemory,
            IsStorageEnabled = lhm.EnableStorage,
            IsNetworkEnabled = lhm.EnableNetwork,
            IsControllerEnabled = lhm.EnableController,
        };

        builder.Services.AddSingleton(computer);
        builder.Services.AddSingleton(new SensorSamplerOptions(
            lhm.SampleIntervalMs,
            lhm.DebugMetrics,
            allow,
            deny));
        builder.Services.AddSingleton<SensorSampler>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorSampler>());

        var app = builder.Build();
        var version = CliParser.GetVersion();

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                computer.Close();
            }
            catch
            {
                // Best effort on shutdown.
            }
        });

        app.Logger.LogInformation(
            "listen={Listen} telemetry.path={TelemetryPath} collectors.enabled={Collectors}",
            config.WebListenAddress,
            config.TelemetryPath,
            config.CollectorsEnabled);
        app.Logger.LogInformation(
            "lhm: cpu={Cpu} gpu={Gpu} mb={Mb} mem={Mem} storage={Sto} debugMetrics={Debug}",
            lhm.EnableCpu,
            lhm.EnableGpu,
            lhm.EnableMotherboard,
            lhm.EnableMemory,
            lhm.EnableStorage,
            lhm.DebugMetrics);

        app.Urls.Clear();
        app.Urls.Add(config.BuildKestrelUrl());

        app.MapGet("/health", (SensorSampler sampler) =>
            sampler.IsHealthy
                ? Results.Text("ok\n", "text/plain")
                : Results.StatusCode(503));

        app.MapGet("/version", () => Results.Text($"{version}\n", "text/plain"));

        app.MapGet(config.TelemetryPath, (SensorSampler sampler) =>
        {
            var samples = sampler.Last.Concat([
                new MetricsFormatter.MetricSample(
                    "lhm_exporter_build_info",
                    1,
                    "Build information",
                    new Dictionary<string, string> { ["version"] = version }),
            ]);
            var body = MetricsFormatter.FormatMetrics(samples);
            return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
        });

        await app.RunAsync();
    }

    private static void ConfigureLogging(WebApplicationBuilder builder, AppConfig config)
    {
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(config.LogLevel);
        builder.Logging.AddConsole();

        if (config.LogToFile)
            builder.Logging.AddProvider(new FileLoggerProvider(config.ResolvedLogFile));
    }
}

public sealed record SensorSamplerOptions(
    int IntervalMs,
    bool DebugMetrics,
    Regex? Allow,
    Regex? Deny);
