namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Runs the bucket monitor.")]
    public class MonitorCommand : BaseCommandLine<MonitorCommand>
    {
        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            var manager = await this.BuildBucketManager(provider);
            await manager.MonitorAsync(provider);
            return 0;
        }
    }
}
