namespace LhmExporter;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var exeDir = AppContext.BaseDirectory;

        try
        {
            var cli = CliParser.Parse(args);

            if (cli.ShowHelp)
            {
                CliParser.PrintHelp(Console.Out);
                return 0;
            }

            if (cli.ShowVersion)
            {
                Console.WriteLine(CliParser.GetVersion());
                return 0;
            }

            if (cli.Uninstall)
                return ServiceInstaller.Uninstall(cli.DryRun);

            var config = ConfigLoader.Load(exeDir, cli);

            if (cli.PrintConfig)
            {
                Console.WriteLine(config.ToDisplayString());
                return 0;
            }

            if (cli.Install)
                return ServiceInstaller.Install(config, cli.DryRun);

            config.Validate();
            Console.WriteLine($"[boot] exeDir={exeDir}");
            Console.WriteLine($"[boot] config={config.ConfigFilePath}");

            await WebAppHost.RunAsync(config, args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[fatal] {ex.Message}");
            return 1;
        }
    }
}
