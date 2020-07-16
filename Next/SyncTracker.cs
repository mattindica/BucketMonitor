namespace BucketMonitor
{
    using System.Collections.Generic;
    using System.Linq;

    public class SyncTracker
    {
        private ConsoleString message = new ConsoleString();
        private object myLock = new object();

        private long totalBytes;
        private long transferredBytes;

        private int numFiles;
        private int completed;
        private int failed;

        public SyncTracker(
            int numFiles,
            long totalBytes)
        {
            this.numFiles = numFiles;
            this.totalBytes = totalBytes;
        }

        private IDictionary<string, DownloadStatus> ActiveDownloads { get; } = new Dictionary<string, DownloadStatus>();

        public void Transferred(
            RemoteImage image,
            long transferredBytes)
        {
            lock (myLock)
            {
                long last;
                if (this.ActiveDownloads.TryGetValue(image.Key, out var status))
                {
                    last = status.TransferredBytes;
                    status.TransferredBytes = transferredBytes;
                }
                else
                {
                    last = 0;
                    this.ActiveDownloads[image.Key] = new DownloadStatus(image, transferredBytes);
                }

                long diff = transferredBytes - last;
                this.transferredBytes += diff;

                if (last / 1e7 != transferredBytes / 1e7)
                {
                    this.PrintStatus();
                }
            }
        }

        public void Skip(RemoteImage image)
        {
            lock (myLock)
            {
                this.numFiles--;
                this.totalBytes -= image.TotalBytes;
                //TODO: Add skip metric?
                this.PrintStatus();
            }
        }

        public void Fail(RemoteImage image)
        {
            lock (myLock)
            {
                if (this.ActiveDownloads.TryGetValue(
                    image.Key,
                    out var status)) {

                    this.transferredBytes -= status.TransferredBytes;
                    this.ActiveDownloads.Remove(image.Key);
                }
                this.totalBytes -= image.TotalBytes;
                this.failed++;
                this.PrintStatus();
            }
        }

        public void Complete(RemoteImage image)
        {
            lock (myLock)
            {
                if (this.ActiveDownloads.TryGetValue(
                    image.Key,
                    out var status)) {
                    this.ActiveDownloads.Remove(image.Key);
                }
                this.completed++;
                this.PrintStatus();
            }
        }

        private void PrintStatus()
        {
            var all = (completed + failed);

            long percent;

            if (this.totalBytes > 0)
            {
                percent = this.transferredBytes * 100L / this.totalBytes;
            }
            else
            {
                percent = 100;
            }


            var failureText = failed > 0 ? $", Failures={failed}" : string.Empty;
            this.message.Update(
                string.Format("Processing Objects {0}%: Remaining=({1}/{2}) Downloaded={3}/{4}, Active={5}{6}",
                percent,
                all,
                numFiles,
                DownloadTracker.BytesToString(this.transferredBytes),
                DownloadTracker.BytesToString(this.totalBytes),
                this.ActiveDownloads.Count(),
                failureText));
        }

        internal class DownloadStatus
        {
            internal DownloadStatus(
                RemoteImage image,
                long transferredBytes = 0)
            {
                this.Image = image;
                this.TransferredBytes = transferredBytes;
            }

            private RemoteImage Image { get; }

            public long TransferredBytes { get; set; }
        }
    }
}
