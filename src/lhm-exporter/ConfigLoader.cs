namespace LhmExporter;

public static class ConfigLoader
{
    public static AppConfig Load(string exeDir, CliParseResult cli)
    {
        var configPath = ResolveConfigPath(exeDir, cli.ConfigFilePath);
        var config = AppConfig.CreateDefaults(exeDir, configPath);

        if (File.Exists(configPath))
        {
            var flat = YamlConfigFlattener.FlattenFile(configPath)
                .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
            config = config.WithFlatOverrides(flat);
        }

        if (cli.ConfigOverrides.Count > 0)
            config = config.WithFlatOverrides(cli.ConfigOverrides);

        return config;
    }

    private static string ResolveConfigPath(string exeDir, string? cliConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(cliConfigPath))
            return Path.GetFullPath(cliConfigPath);

        var localConfig = AppPaths.DefaultConfigNextToExe(exeDir);
        if (File.Exists(localConfig))
            return localConfig;

        if (File.Exists(AppPaths.InstalledConfigFile))
            return AppPaths.InstalledConfigFile;

        return localConfig;
    }
}
