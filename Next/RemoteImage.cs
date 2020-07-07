namespace BucketMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Amazon.S3.Model;

    public class RemoteImage
    {
        private RemoteImage(
            string key,
            FileInfo file,
            DateTime lastModified,
            long totalBytes)
        {
            this.Key = key;
            this.File = file;
            this.LastModified = lastModified;
            this.TotalBytes = totalBytes;
        }

        public string Key { get; }

        public FileInfo File { get; }

        public DateTime LastModified { get; }

        public long TotalBytes { get; }

        public static bool TryLoad(S3Object obj, Settings settings, out RemoteImage image)
        {
            if (BucketManager.TryConvertPath(
                obj.Key,
                settings.RootPath,
                settings.IncludedPaths ?? new List<string>(),
                out var file))
            {
                image = new RemoteImage(obj.Key, file, obj.LastModified, obj.Size);
                return true;
            }
            else
            {
                image = null;
                return false;
            }
        }
    }
}
