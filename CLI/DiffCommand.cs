namespace BucketMonitor.CLI
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Displays all missing local files.")]
    public class MissingCommand : BaseCommandLine<SnapshotLocalCommand>
    {
        [Option("-l|--list", description: "List the files.", optionType: CommandOptionType.NoValue)]
        public bool ShowFiles { get; set; }

        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            var initial = DateTime.Now;
            var manager = provider.GetService<SyncManager>();
            var diff = await manager.DiffAsync();

            if (this.ShowFiles)
            {
                var files = diff.Select(x => x.File)
                    .OrderBy(x => x.FullName);

                foreach (var file in files)
                {
                    Console.WriteLine(file.FullName);
                }
                Console.WriteLine();
            }

            Console.WriteLine("Pending Objects: {0:n0} [{1}]", diff.Count(), DateTime.Now.Subtract(initial));
            return 0;
        }
    }
}
