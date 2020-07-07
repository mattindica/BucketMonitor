namespace BucketMonitor
{
    using System;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;

    public class AmazonMonitor
    {
        public AmazonMonitor(
            SyncManager syncManager,
            Settings settings,
            ILoggerFactory factory)
        {
            this.SyncManager = syncManager;
            this.Settings = settings;
            this.Logger = factory.CreateLogger<AmazonMonitor>();
        }

        private SyncManager SyncManager { get; }

        private Settings Settings { get; }

        private ILogger<AmazonMonitor> Logger { get; }

        public async Task MonitorAsync()
        {
            DateTime? lastCheck = null;

            this.Logger.LogInformation("Monitoring Bucket {0}", this.Settings.BucketName);
            for (; ; )
            {
                var now = DateTime.Now;
                if (!lastCheck.HasValue || now.Subtract(lastCheck.Value) > this.Settings.PollingInterval)
                {
                    try
                    {
                        this.Logger.LogDebug("Scanning Bucket {0}", this.Settings.BucketName);
                        lastCheck = now;

                        Console.WriteLine("\n=> Scanning Bucket {0} ({1})", this.Settings.BucketName, DateTime.Now);
                        await this.SyncManager.SyncAsync();
                    }
                    catch (Exception e)
                    {
                        this.Logger.LogError("Exception: {0}", e.ToString());
                    }
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
        }
    }
}
