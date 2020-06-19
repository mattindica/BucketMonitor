namespace BucketMonitor
{
    using Amazon.S3.Model;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    public class S3ObjectCache
    {
        private IDictionary<string, SourceImage> Cache { get; } = new ConcurrentDictionary<string, SourceImage>();
        private ConcurrentQueue<SourceImage> Pending = new ConcurrentQueue<SourceImage>();

        public S3ObjectCache(ILogger logger, char driveLetter)
        {
            this.Logger = logger;

            if (File.Exists(Program.CacheFile))
            {
                var cached = this.ReadCache(driveLetter);
                foreach (var image in cached)
                {
                    this.Cache[image.Key] = image;
                }

                this.Logger.LogInformation("Initialized {0} Cached Entries", cached.Count());
            }
        }

        private ILogger Logger { get; } 

        public bool TryGet(string key, DateTime lastModified, out SourceImage image)
        {
            return this.Cache.TryGetValue(key, out image);
        }

        public void Put(SourceImage image, bool skipEnqueue = false)
        {
            this.Cache[image.Key] = image;
            if (!skipEnqueue)
            {
                this.Pending.Enqueue(image);
            }
        } 

        private IEnumerable<SourceImage> ReadCache(char driveLetter)
        {
            var lines = File.ReadAllLines(Program.CacheFile);

            foreach (var line in lines)
            {
                var tokens = line.Split('\t');

                if (tokens.Count() == 3 &&
                    BucketManager.TryConvertPath(tokens[0], driveLetter, out var file) &&
                    DateTime.TryParse(tokens[1], out var lastModified) &&
                    long.TryParse(tokens[2], out var totalBytes) &&
                    int.TryParse(tokens[3], out var code))
                {
                    //Console.WriteLine($"Loading {tokens[0]}...");
                    yield return new SourceImage(
                        key: tokens[0],
                        file: file,
                        lastModified: lastModified,
                        totalBytes: totalBytes,
                        status: (ImageStatus)code);
                }
            }
        }

        public async Task SaveAsync()
        {
            var output = new List<string>();

            while (this.Pending.TryDequeue(out var image))
            {
                output.Add($"{image.Key}\t{image.LastModified.ToString("O")}\t{image.TotalBytes}\t{(int)image.Status}");
            }

            await File.AppendAllLinesAsync(Program.CacheFile, output);
        }
    }
}
