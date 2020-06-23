namespace BucketMonitor
{
    public enum ImageStatus : int
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Skipped = 3,
        Failed = 4
    }
}
