namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Removes all cached image entries.")]
    public class ResetCommand : BaseCommandLine<ResetCommand>
    {
        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            await this.BuildBucketManager(provider)
                .ResetAsync(provider);
            return 0;
        }
    }
}
