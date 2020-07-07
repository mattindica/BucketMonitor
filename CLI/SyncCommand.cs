namespace BucketMonitor.CLI
{
    using System;
    using System.Threading.Tasks;

    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Extensions.DependencyInjection;

    [Command(Description = "Synchronizes drive with bucket.")]
    public class SyncCommand : BaseCommandLine<SyncCommand>
    {
        [Option("-st|--single-threaded", description: "Download single threaded.", optionType: CommandOptionType.NoValue)]
        public bool SingleThreaded { get; set; }

        [Option("-k|--key", description: "The key of the object to sync", optionType: CommandOptionType.SingleOrNoValue)]
        public string Key { get; set; }

        protected override async Task<int> ExecuteAsync(
            CommandLineApplication app,
            ServiceProvider provider)
        {

            var initial = DateTime.Now;
            var manager = provider.GetService<SyncManager>();
            var scanner = provider.GetService<AmazonScanner>();

            if (this.Key != null)
            {
                Console.WriteLine("Downloading {0}", this.Key);

                var image = await scanner.LoadAsync(this.Key);

                var tracker = new SyncTracker(1, image.TotalBytes);

                var result = await scanner.SyncAsync(image, tracker);
                Console.WriteLine();
                Console.WriteLine("Download {0}: {1}",
                    result != null ? "Complete" : "Failed",
                    DateTime.Now.Subtract(initial));
            }
            else
            {
                if (this.SingleThreaded)
                {
                    await manager.SyncDebugAsync();
                }
                else
                {
                    await manager.SyncAsync();
                }
            }

            return 0;
        }
    }
}
