namespace BucketMonitor.CLI
{
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Lists the status of the source bucket.")]
    public class StatusCommand : BaseCommandLine<StatusCommand>
    {
        [Option("-c|--cached", description: "Only display the status of cached entries.", optionType: CommandOptionType.NoValue)]
        public bool CachedOnly { get; set; }

        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            var manager = await this.BuildBucketManager(provider);

            if (this.CachedOnly)
            {
                await manager.SummarizeCached(provider);
            }
            else
            {
                await manager.Summarize(provider);
            }
            return 0;
        }
    }
}
