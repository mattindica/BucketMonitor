using System;

namespace BucketMonitor
{
    public class ScanNotifier
    {
        public ScanNotifier()
        {
        }

        private ConsoleString ConsoleString { get; } = new ConsoleString();

        public void Start()
        {
            this.ConsoleString.Update($"Scanning Objects...");
        }

        public void ScannedBatch(long scanned, long tracked)
        {
            this.ConsoleString.Update(string.Format("Scanned: {0:n0}. Tracked: {1:n0}", scanned, tracked));
        }

        public void Finish(long scanned, long tracked)
        {
            this.ConsoleString.Update(string.Format("Scanned {0:n0} Objects. Tracking {1:n0}", scanned, tracked));
            this.ConsoleString.Update(string.Empty);
            Console.WriteLine();
        }
    }
}
