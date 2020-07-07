namespace BucketMonitor.CLI
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using ConsoleTables;
    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Information about tracked remote files.")]
    public class SnapshotRemoteCommand : BaseCommandLine<SnapshotLocalCommand>
    {
        [Option("-l|--list", description: "List the files.", optionType: CommandOptionType.NoValue)]
        public bool ShowFiles { get; set; }

        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            var initial = DateTime.Now;
            var scanner = provider.GetService<AmazonScanner>();
            var files = await scanner.QueryIncludedAsync(notifier: new ScanNotifier());

            if (this.ShowFiles)
            {
                var table = new ConsoleTable("Key", "Last Modified", "Path");
                table.Options.EnableCount = false;
                foreach (var file in files)
                {
                    table.AddRow(
                        file.Key,
                        file.LastModified,
                        file.File?.FullName ?? "-");
                }
                table.Write();
                Console.WriteLine();
            }

            Console.WriteLine("Scanned {0:n0} Files: {1}", files.Count(), DateTime.Now.Subtract(initial));
            return 0;
        }
    }
}
