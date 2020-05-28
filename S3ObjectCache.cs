namespace BucketMonitor
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    public class S3ObjectCache
    {
        private IDictionary<string, SourceImage> Cache { get; } = new ConcurrentDictionary<string, SourceImage>();

        public bool TryGet(string key, DateTime lastModified, out SourceImage image)
        {
            return this.Cache.TryGetValue(key, out image);
        }

        public void Put(SourceImage image)
        {
            this.Cache[image.Key] = image;
        } 
    }
}
