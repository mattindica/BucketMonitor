namespace BucketMonitor
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;

    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    using Serilog;

    public class Program
    {
        public static string BaseDirectory => ".";

        public static string OutputDirectory => Path.Combine(BaseDirectory, "output");

        public static string LoggingDirectory => Path.Combine(OutputDirectory, "logs");

        public static string CacheFile => Path.Combine(OutputDirectory, "cache");

        public static string ConfigFile => Path.Combine(BaseDirectory, "config.yml");

        private static void SetupOutputDirectory()
        {
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }

            if (!Directory.Exists(LoggingDirectory))
            {
                Directory.CreateDirectory(LoggingDirectory);
            }
        }

        private static void ConfigureServices(ServiceCollection services)
        {
            SetupOutputDirectory();

            var timestamp = DateTime.Now.ToString("yyyyMMddTHHmmss");
            var serilogLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(Path.Combine(LoggingDirectory, $"{timestamp}.log"))
                .CreateLogger();

            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information)
                    .AddSerilog(logger: serilogLogger, dispose: true)
                    .AddConsole(cfg => cfg.LogToStandardErrorThreshold = LogLevel.Information);
            });
        }


        public static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);

            using (ServiceProvider serviceProvider = services.BuildServiceProvider())
            {
                if (args.Length < 1)
                {
                    Console.WriteLine("Usage: dotnet run -- [monitor|list|status|completed]");
                    return;
                }

                var logger = serviceProvider.GetService<ILogger<Program>>();

                if (Settings.TryLoad(ConfigFile, out var settings, logger))
                {
                    logger.LogInformation("Loaded Configuration {0}:\n{1}", new FileInfo(ConfigFile).FullName, settings.Summarize());
                    // Customize AWS info here

                    BucketManager manager = new BucketManager(
                        logger: logger,
                        settings: settings);

                    switch (args[0]) {
                        case "completed":
                            await manager.DisplayCompleted();
                            break;
                        case "list":
                            await manager.DisplayImages();
                            break;
                        case "status":
                            await manager.Summarize();
                            break;
                        case "monitor":
                            await manager.MonitorAsync();
                            break;
                        default:
                            Console.WriteLine("Invalid Command: {0}", args[0]);
                            break;
                    }
                }
            }


        }
    }
}
