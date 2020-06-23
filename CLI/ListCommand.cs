namespace BucketMonitor.CLI
{
    using System.Linq;
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Lists all objects in source bucket.")]
    public class ListCommand : BaseCommandLine<ListCommand>
    {
        [Option("-c|--cached", description: "Only display cached entries.", optionType: CommandOptionType.NoValue)]
        public bool CachedOnly { get; set; }


        [Option("-s|--status", description: "Limits results to given status.", optionType: CommandOptionType.MultipleValue)]
        public ImageStatus[] Statuses { get; set; }

        protected override async Task<int> ExecuteAsync(CommandLineApplication app, ServiceProvider provider)
        {
            var manager = this.BuildBucketManager(provider);
            var statuses = this.Statuses.ToHashSet();

            if (this.CachedOnly)
            {
                await manager.DisplayCachedImages(provider, filter: statuses);
            }
            else
            {
                await manager.DisplayImages(provider, filter: statuses);
            }

            return 0;
        }
    }
}
