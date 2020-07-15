namespace BucketMonitor
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using System.Threading;
    using System.Threading.Tasks;

    public class SyncManager
    {
        public SyncManager(
            Settings settings,
            AmazonScanner scanner,
            DirectoryScanner directoryScanner,
            ILoggerFactory factory)
        {
            this.Settings = settings;
            this.Scanner = scanner;
            this.DirectoryScanner = directoryScanner;
            this.Logger = factory.CreateLogger<SyncManager>();
        }

        private Settings Settings { get; }

        private AmazonScanner Scanner { get; }

        private DirectoryScanner DirectoryScanner { get; }

        private ILogger<SyncManager> Logger { get; }

        public async Task SyncAsync()
        {
            var diff = await this.DiffAsync();

            if (diff.Count() > 0)
            {
                await this.SyncAsync(
                    diff: diff,
                    maxDownloads: this.Settings.MaxDownloads);
            }
            else
            {
                this.Logger.LogInformation("No Pending Files.");
            }
        }

        public async Task SyncDebugAsync()
        {
            var diff = await this.DiffAsync();

            if (diff.Count() > 0)
            {
                await this.SyncDebugAsync(
                    diff: diff);
            }
            else
            {
                this.Logger.LogInformation("No Pending Files.");
            }
        }

        public async Task SyncAsync(
            IEnumerable<RemoteImage> diff,
            int maxDownloads)
        {
            // this.Logger.LogDebug("Downloading {0} Pending Images", pending.Count());
            Console.WriteLine("Downloading {0} Pending Images", diff.Count());
            var throttler = new SemaphoreSlim(initialCount: maxDownloads);
            var tracker = new SyncTracker(diff.Count(), diff.Select(x => x.TotalBytes).Sum());

            var tasks = new List<Task<FileInfo>>();
            foreach (var image in diff)
            {
                await throttler.WaitAsync();
                tasks.Add(
                    Task.Run(async () =>
                    {
                        try
                        {
                            return await this.Scanner.SyncAsync(
                                image,
                                tracker);
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }));
            }
            await Task.WhenAll(tasks);

            Console.WriteLine();
            Console.WriteLine("Downloads Complete");
            // this.Logger.LogDebug("Downloads Complete");
        }

        public async Task SyncDebugAsync(
            IEnumerable<RemoteImage> diff)
        {
            var initial = DateTime.Now;
            foreach (var image in diff)
            {
                var tracker = new SyncTracker(1, image.TotalBytes);
                Console.WriteLine(Environment.NewLine);
                Console.WriteLine("Downloading {0}", image.Key);

                var result = await this.Scanner.SyncAsync(image, tracker);
                Console.WriteLine();
                Console.WriteLine("Download {0}: {1}",
                    result != null ? "Complete" : "Failed",
                    DateTime.Now.Subtract(initial));
            }
        }

        public async Task<IEnumerable<RemoteImage>> DiffAsync()
        {
            var snapshot = await this.DirectoryScanner.ScanAsync();
            return await this.Scanner.DiffAsync(snapshot, new ScanNotifier());
        }

    }
}
