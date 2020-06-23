namespace BucketMonitor
{
    using System.Linq;

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
                var image = this.DbContext.Image.Single(x => x.Id == entry.Id);
                image.Status = status;
                this.DbContext.SaveChanges();
            }
        }
    }
}
