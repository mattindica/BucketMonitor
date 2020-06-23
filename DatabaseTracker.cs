namespace BucketMonitor
{
    using System;
    using System.Threading.Tasks;

    public class DatabaseTracker
    {
        private object myLock = new object();

        public DatabaseTracker(
            BucketMonitorContext dbContext)
        {
            this.DbContext = dbContext;
        }

        private BucketMonitorContext DbContext { get; }

        public void Update(ImageEntry entry, ImageStatus status)
        {
            lock (myLock)
            {
                entry.Status = status;
                this.DbContext.SaveChanges();
            }
        }
    }
}
