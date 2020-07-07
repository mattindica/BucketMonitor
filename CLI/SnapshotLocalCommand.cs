namespace BucketMonitor.CLI
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Information about tracked local files.")]
    public class SnapshotLocalCommand : BaseCommandLine<SnapshotLocalCommand>
    {
        [Option("-l|--list", description: "List the files.", optionType: CommandOptionType.NoValue)]
        public bool ShowFiles { get; set; }

        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {
            var scanner = provider.GetService<DirectoryScanner>();
            var root = this.Parent.Settings.RootPath;

            Console.WriteLine("Scanning {0} ...", root); 

            var initial = DateTime.Now;
            var files = scanner.Scan().ToList();

            if (this.ShowFiles)
            {
                this.DisplayFileTable(files);
            }

            Console.WriteLine("Scanned {0:n0} Files: {1}", files.Count(), DateTime.Now.Subtract(initial));

            await Task.Delay(0); //TODO: Remove this
            return 0;
        }
    }
}
