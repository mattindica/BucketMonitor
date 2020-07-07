namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Configures an S3 bucket for monitoring.")]
    public class ConfigureCommand : BaseCommandLine<ConfigureCommand>
    {
        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            var manager = await this.BuildBucketManager(provider, validation: false);
            await manager.ConfigureBucketAsync(provider);
            return 0;
        }
    }
}
