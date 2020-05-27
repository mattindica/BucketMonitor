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
            if (this.Cache.TryGetValue(key, out image))
            {
                if (image.LastModified == lastModified)
                {
                    return true;
                }
                else
                {
                    Console.WriteLine("Key is outdated: {key}", key);
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public void Put(SourceImage image)
        {
            this.Cache[image.Key] = image;
        } 
    }
}
