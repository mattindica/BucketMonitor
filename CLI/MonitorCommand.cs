namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Runs the bucket monitor.")]
    public class MonitorCommand : BaseCommandLine<MonitorCommand>
    {
        [Option("-l|--legacy", description: "Use the legacy monitor.", optionType: CommandOptionType.NoValue)]
        public bool UseLegacy { get; set; }

        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            if (this.UseLegacy)
            {
                var manager = await this.BuildBucketManager(provider);
                await manager.MonitorAsync(provider);
            }
            else
            {
                var monitor = provider.GetService<AmazonMonitor>();
                await monitor.MonitorAsync();
            }

            return 0;
        }
    }
}
