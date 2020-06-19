﻿namespace BucketMonitor
{
    using System;
    using System.IO;

    public class SourceImage
    {
        public SourceImage(
            string key,
            FileInfo file,
            DateTime lastModified,
            long totalBytes,
            ImageStatus status)
        {
            this.Key = key;
            this.File = file;
            this.LastModified = lastModified;
            this.TotalBytes = totalBytes;
            this.Status = status;
        }

        public string Key { get; }

        public FileInfo File { get; }

        public DateTime LastModified { get; }

        public long TotalBytes { get; }

        public ImageStatus Status { get; private set; }

        public void MarkCompleted()
        {
            this.Status = ImageStatus.Completed;
        }

        public void MarkFailed()
        {
            this.Status = ImageStatus.Failed;
        }
    }
}
