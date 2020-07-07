namespace BucketMonitor
{
    using System.Collections.Generic;
    using System.IO;

    public interface ILocalSnapshot
    {
        bool Exists(RemoteImage image);

        IEnumerable<FileInfo> ToList();
    }
}
