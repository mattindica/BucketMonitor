namespace BucketMonitor
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
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

        private static void ConfigureServices(ServiceCollection services, Settings settings)
        {
            SetupOutputDirectory();

            var timestamp = DateTime.Now.ToString("yyyyMMddTHHmmss");
            var serilogLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(Path.Combine(LoggingDirectory, $"{timestamp}.log"))
                .CreateLogger();

            services.AddDbContext<BucketMonitorContext>(builder =>
            {
                builder.UseMySql(settings.DatabaseConnectionString);
            });

            services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning)
                    .SetMinimumLevel(LogLevel.Information)
                    .AddSerilog(logger: serilogLogger, dispose: true)
                    .AddConsole(cfg => cfg.LogToStandardErrorThreshold = LogLevel.Information);
            });
        }


        public static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: dotnet run -- [monitor|list|status|completed]");
                return;
            }


            if (Settings.TryLoad(ConfigFile, out var settings))
            {
                var services = new ServiceCollection();
                ConfigureServices(services, settings);


                using (ServiceProvider provider = services.BuildServiceProvider())
                {
                    var logger = provider.GetService<ILogger<Program>>();
                    logger.LogInformation("Loaded Configuration {0}:\n{1}", new FileInfo(ConfigFile).FullName, settings.Summarize());

                    BucketManager manager = new BucketManager(
                        logger: logger,
                        settings: settings);

                    // Customize AWS info here

                    switch (args[0])
                    {
                        case "list":
                            await manager.DisplayImages(provider);
                            break;
                        case "status":
                            await manager.Summarize(provider);
                            break;
                        case "monitor":
                            await manager.MonitorAsync(provider);
                            break;
                        case "configure":
                            await manager.ConfigureBucketAsync(provider);
                            break;
                        case "db":
                            await manager.DisplayImages(provider);
                            break;
                        default:
                            Console.WriteLine("Invalid Command: {0}", args[0]);
                            break;
                    }
                }
            }
            else
            {
                Console.WriteLine("Failed to load config: {0}", ConfigFile);
            }
        }
    }
}
