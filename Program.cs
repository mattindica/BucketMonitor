namespace BucketMonitor
{
    using System;
    using System.IO;
    using System.Reflection;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    using BucketMonitor.CLI;

    using Serilog;

    [Command("BucketMonitor.exe")]
    [VersionOptionFromMember("--version", MemberName = nameof(GetVersion))]
    [Subcommand(
       typeof(MonitorCommand),
       typeof(ConfigureCommand),
       typeof(MigrateCommand),
       typeof(SnapshotLocalCommand),
       typeof(SnapshotRemoteCommand),
       typeof(MissingCommand),
       typeof(SyncCommand)
        )]
    public class Program 
    {
        public Program()
        {
            var services = new ServiceCollection();

            if (Settings.TryLoad(ConfigFile, out var settings))
            {
                this.ConfigureServices(services, settings);
                this.Settings = settings;
                this.Services = services;
            }
            else
            {
                throw new Exception(
                    string.Format("Failed to load config: {0}", ConfigFile));
            }
        }

        private ServiceCollection Services { get; }

        public Settings Settings { get; }

        public ServiceProvider Provider { get; }

        public static string BaseDirectory => ".";

        public static string OutputDirectory => Path.Combine(BaseDirectory, "output");

        public static string LoggingDirectory => Path.Combine(OutputDirectory, "logs");

        public static string CacheFile => Path.Combine(OutputDirectory, "cache");

        public static string ConfigFile => Path.Combine(BaseDirectory, "config.yml");

        public static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        public ServiceProvider CreateProvider() => this.Services.BuildServiceProvider();

        protected int OnExecute(CommandLineApplication app)
        {
            app.ShowHelp();
            return 1;
        }

        private static string GetVersion()
            => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        private void ConfigureServices(ServiceCollection services, Settings settings)
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
                    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
                    .SetMinimumLevel(LogLevel.Information)
                    .AddSerilog(logger: serilogLogger, dispose: true)
                    .AddConsole(cfg => cfg.LogToStandardErrorThreshold = LogLevel.Information);
            });

            services.AddSingleton(settings);
            services.AddSingleton<AmazonScanner>();
            services.AddSingleton<SyncManager>();
            services.AddSingleton<AmazonMonitor>();
            services.AddSingleton<DirectoryScanner>();
        }

        private void SetupOutputDirectory()
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
    }
}
