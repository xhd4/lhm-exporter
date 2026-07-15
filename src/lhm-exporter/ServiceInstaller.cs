namespace LhmExporter;

public static class ServiceInstaller
{
    public static int Install(AppConfig config, bool dryRun)
    {
        EnsureAdministrator();

        var sourceExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine executable path");

        var installDir = AppPaths.InstallDirectory;
        var exeDest = AppPaths.InstalledExecutable;
        var configDest = AppPaths.InstalledConfigFile;
        var logDir = AppPaths.DefaultLogDirectory;
        var port = config.ListenPort;
        var fwRuleName = $"{AppPaths.FirewallRulePrefix}{port})";
        var binPath = $"\"{exeDest}\" --config.file=\"{configDest}\"";

        PrintPlan("Install plan", dryRun, [
            $"Copy: {sourceExe} -> {exeDest}",
            $"Config: {configDest} {(File.Exists(configDest) ? "(keep existing)" : "(create new)")}",
            $"Logs dir: {logDir}",
            $"Firewall: {fwRuleName}",
            $"Service: {AppPaths.ServiceName}",
            $"binPath: {binPath}",
            $"Metrics: http://localhost:{port}{config.TelemetryPath}",
        ]);

        if (dryRun)
            return 0;

        Directory.CreateDirectory(installDir);
        Directory.CreateDirectory(logDir);

        StopServiceIfExists();
        File.Copy(sourceExe, exeDest, overwrite: true);

        if (!File.Exists(configDest))
            File.WriteAllText(configDest, config.SerializeToYaml(), Encoding.UTF8);
        else
            config = ConfigLoader.Load(AppPaths.InstallDirectory, new CliParseResult { ConfigFilePath = configDest });

        DeleteFirewallRulesByPrefix();
        if (config.FirewallEnabled)
            AddFirewallRule(fwRuleName, exeDest, port, config.FirewallProfile);
        CreateOrReplaceService(binPath);
        ConfigureServiceRecovery();
        RunSc($"start {AppPaths.ServiceName}");

        Console.WriteLine($"[OK] Installed and started: {AppPaths.ServiceName}");
        Console.WriteLine($"[OK] Metrics: http://localhost:{port}{config.TelemetryPath}");
        return 0;
    }

    public static int Uninstall(bool dryRun)
    {
        EnsureAdministrator();

        PrintPlan("Uninstall plan", dryRun, [
            $"Stop/delete service: {AppPaths.ServiceName}",
            $"Delete firewall rules: {AppPaths.FirewallRulePrefix}*",
            $"Remove: {AppPaths.InstallDirectory}",
            $"Remove: {AppPaths.ProgramDataDirectory}",
        ]);

        if (dryRun)
            return 0;

        StopServiceIfExists();
        DeleteFirewallRulesByPrefix();
        KillProcessIfRunning();

        TryDeleteDirectory(AppPaths.InstallDirectory);
        TryDeleteDirectory(AppPaths.ProgramDataDirectory);

        Console.WriteLine("[OK] Uninstalled");
        return 0;
    }

    private static void PrintPlan(string title, bool dryRun, IEnumerable<string> lines)
    {
        Console.WriteLine($"=== {title}{(dryRun ? " (dry-run)" : "")} ===");
        foreach (var line in lines)
            Console.WriteLine($"  {line}");
    }

    private static void EnsureAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Install/uninstall is supported on Windows only.");

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Administrator privileges are required. Run elevated.");
    }

    private static void StopServiceIfExists()
    {
        RunSc($"stop {AppPaths.ServiceName}", ignoreErrors: true);
        Thread.Sleep(2000);
        RunSc($"delete {AppPaths.ServiceName}", ignoreErrors: true);
        Thread.Sleep(1000);
    }

    private static void CreateOrReplaceService(string binPath)
    {
        var createArgs = $"create {AppPaths.ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"{AppPaths.DisplayName}\"";
        RunSc(createArgs);
        RunSc($"description {AppPaths.ServiceName} \"{AppPaths.ServiceDescription}\"", ignoreErrors: true);
    }

    private static void ConfigureServiceRecovery()
    {
        RunSc($"failure {AppPaths.ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000", ignoreErrors: true);
        RunSc($"failureflag {AppPaths.ServiceName} 1", ignoreErrors: true);
    }

    private static void DeleteFirewallRulesByPrefix()
    {
        var prefix = AppPaths.FirewallRulePrefix.Replace("'", "''");
        var script = $"$p='{prefix}'; Get-NetFirewallRule -DisplayName ($p+'*') -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue | Out-Null";
        RunPowerShell(script, ignoreErrors: true);
    }

    public static void EnsureFirewall(AppConfig config, string programPath)
    {
        if (!config.FirewallEnabled)
            return;

        try
        {
            EnsureAdministrator();
        }
        catch
        {
            // Best-effort when running without elevation (console).
            return;
        }

        var port = config.ListenPort;
        var fwRuleName = $"{AppPaths.FirewallRulePrefix}{port})";
        DeleteFirewallRulesByPrefix();
        AddFirewallRule(fwRuleName, programPath, port, config.FirewallProfile);
    }

    private static void AddFirewallRule(string ruleName, string exePath, int port, string profile)
    {
        var name = ruleName.Replace("'", "''");
        var exe = exePath.Replace("'", "''");
        var psProfile = MapFirewallProfile(profile);
        var script =
            $"New-NetFirewallRule -DisplayName '{name}' -Direction Inbound -Action Allow -Enabled True -Program '{exe}' -Protocol TCP -LocalPort {port} -Profile {psProfile} | Out-Null";
        RunPowerShell(script);
    }

    private static string MapFirewallProfile(string profile) =>
        profile.Trim().ToLowerInvariant() switch
        {
            "domain" => "Domain",
            "private" => "Private",
            "public" => "Public",
            _ => "Any",
        };

    private static void KillProcessIfRunning()
    {
        foreach (var process in Process.GetProcessesByName(AppPaths.AppFolderName))
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to delete {path}: {ex.Message}");
        }
    }

    private static void RunSc(string arguments, bool ignoreErrors = false)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Failed to start sc.exe ({arguments})");

        process.WaitForExit();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (process.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException($"sc.exe {arguments} failed ({process.ExitCode}): {stderr}{stdout}");
    }

    private static void RunPowerShell(string script, bool ignoreErrors = false)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start powershell.exe");

        process.WaitForExit();
        var stderr = process.StandardError.ReadToEnd();

        if (process.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException($"PowerShell failed ({process.ExitCode}): {stderr}");
    }
}
