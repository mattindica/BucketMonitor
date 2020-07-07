namespace BucketMonitor
{
    using System;

    public class DownloadTracker
    {
        private ConsoleString message = new ConsoleString();
        private object myLock = new object();

        private long totalBytes;
        private long downloadedBytes;

        private int total;
        private int running;
        private int completed;
        private int failed;


        public DownloadTracker(
            int total,
            long totalBytes)
        {
            this.total = total;
            this.totalBytes = totalBytes;
        }


        public void Start()
        {
            lock (myLock)
            {
                this.running++;
                this.PrintStatus();
            }
        }

        public void Downloaded(long bytes)
        {
            lock (myLock)
            {
                var last = this.downloadedBytes;
                this.downloadedBytes += bytes;
                if (last / 1e8 != this.downloadedBytes / 1e8)
                {
                    this.PrintStatus();
                }
            }
        }

        private void PrintStatus()
        {
            var all = (completed + failed);
            var percent = all * 100 / total;

            var failureText = failed > 0 ? $", Failures={failed}" : string.Empty;
            this.message.Update(
                string.Format("Processing Objects {0}%: Remaining=({1}/{2}) Downloaded={3}/{4}, Active={5}{6}",
                percent,
                all,
                total,
                BytesToString(this.downloadedBytes),
                BytesToString(this.totalBytes),
                running,
                failureText));
        }

        public void Fail()
        {
            lock (myLock)
            {
                this.running--;
                this.failed++;
                this.PrintStatus();
            }
        }

        public void Complete()
        {
            lock (myLock)
            {
                this.running--;
                this.completed++;
                this.PrintStatus();
            }
        }

        public static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);

            return string.Format("{0:F1}{1}", Math.Sign(byteCount) * num, suf[place]);
        }
    }
}
