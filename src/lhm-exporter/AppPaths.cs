namespace LhmExporter;

public static class AppPaths
{
    public const string AppFolderName = "lhm-exporter";
    public const string ConfigFileName = "config.yaml";
    public const string ServiceName = "lhm-exporter";
    public const string DisplayName = "LHM Exporter";
    public const string ServiceDescription = "Prometheus exporter using LibreHardwareMonitorLib";
    public const string FirewallRulePrefix = "lhm-exporter (TCP ";

    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppFolderName);

    public static string InstalledExecutable =>
        Path.Combine(InstallDirectory, "lhm-exporter.exe");

    public static string InstalledConfigFile =>
        Path.Combine(InstallDirectory, ConfigFileName);

    public static string ProgramDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    public static string DefaultLogDirectory =>
        Path.Combine(ProgramDataDirectory, "logs");

    public static string DefaultLogFile =>
        Path.Combine(DefaultLogDirectory, "exporter.log");

    public static string DefaultConfigNextToExe(string exeDir) =>
        Path.Combine(exeDir, ConfigFileName);

    public static string ResolveLogFile(string logFile, string exeDir)
    {
        if (string.IsNullOrWhiteSpace(logFile))
            return DefaultLogFile;

        return Path.IsPathRooted(logFile)
            ? logFile
            : Path.Combine(exeDir, logFile.Replace('/', Path.DirectorySeparatorChar));
    }
}
