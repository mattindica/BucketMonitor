namespace BucketMonitor
{
    using System;
    using System.IO;

    public class SourceImage
    {
        public SourceImage(
            string key,
            FileInfo file,
            DateTime lastModified,
            bool processed)
        {
            this.Key = key;
            this.File = file;
            this.LastModified = lastModified;
            this.Processed = processed;
        }

        public string Key { get; }

        public FileInfo File { get; }

        public DateTime LastModified { get; }

        public bool Processed { get; }
    }
}
