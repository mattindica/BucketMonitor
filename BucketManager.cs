namespace BucketMonitor
{

    /*
     * TODO:
     * 
     * - Ignore Directores (Or Include)
     * - Show Cached
     * - Erase Cache
     * 
     * 
     */

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime.Internal;
    using Amazon.S3;
    using Amazon.S3.Model;

    using ConsoleTables;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    public class BucketManager
    {
        public BucketManager(
            ILogger logger,
            Settings settings)
        {
            this.Client = new AmazonS3Client(
                credentials: settings.GetCredentials(),
                region: settings.RegionEndpoint);

            this.BucketName = settings.BucketName;
            this.DriveLetter = settings.DriveLetter;
            this.PollingInterval = settings.PollingInterval;
            this.DebugMode = settings.DebugMode;
            this.MaxDownloads = settings.MaxDownloads;
            this.Logger = logger;
        }

        public TimeSpan PollingInterval { get; }

        public string BucketName { get; }

        public char DriveLetter { get; }

        private AmazonS3Client Client { get; }

        private int MaxDownloads { get; set; }

        private bool DebugMode { get; }

        private ILogger Logger { get; }

        public async Task<IEnumerable<SourceImage>> ListPendingAsync(Bucket bucket, BucketMonitorContext dbContext)
        {
            return (await this.ListImagesAsync(dbContext, bucket))
                .Where(x => x.Status == ImageStatus.Pending)
                .OrderBy(obj => obj.LastModified);
        }

        private SourceImage CreateSourceImage(string key, DateTime lastModified, long totalBytes)
        {
            SourceImage image;

            if (this.TryConvertPath(key, out var file))
            {
                image = new SourceImage(
                    key: key,
                    file: file,
                    lastModified: lastModified,
                    totalBytes: totalBytes,
                    status: ImageStatus.Pending);
            }
            else
            {
                image = new SourceImage(
                    key: key,
                    file: null,
                    lastModified: lastModified,
                    totalBytes: totalBytes,
                    status: ImageStatus.Skipped);
            }

            return image;
        }

        public async Task MonitorAsync(ServiceProvider provider)
        {
            DateTime? lastCheck = null;

            this.Logger.LogInformation("Monitoring Bucket {0}", this.BucketName);
            Console.Out.Flush();
            for (; ; )
            {
                try
                {
                    var now = DateTime.Now;
                    if (!lastCheck.HasValue || DateTime.Now.Subtract(lastCheck.Value) > this.PollingInterval)
                    {
                        using (var scoped = provider.CreateScope())
                        {
                            var dbContext = scoped.ServiceProvider.GetService<BucketMonitorContext>();
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.Out.Flush();
                            this.Logger.LogInformation("Scanning Bucket {0}", this.BucketName);
                            Console.Out.Flush();
                            lastCheck = DateTime.Now;

                            var bucket = await this.GetBucketAsync(dbContext);
                            var pending = await this.ListPendingAsync(bucket, dbContext);
                            var count = pending.Count();
                            var tracker = new DownloadTracker(count, pending.Select(x => x.TotalBytes).Sum());
                            var dbTracker = new DatabaseTracker(dbContext: dbContext);

                            bucket = await this.GetBucketAsync(dbContext);
                            var entryMap = bucket.Images.ToDictionary(x => x.Key);
                            if (count > 0)
                            {
                                this.Logger.LogInformation("Downloading {0} Pending Images", pending.Count());
                                var throttler = new SemaphoreSlim(initialCount: this.MaxDownloads);

                                var tasks = new List<Task<FileInfo>>();
                                foreach (var image in pending)
                                {
                                    await throttler.WaitAsync();
                                    tasks.Add(
                                        Task.Run(async () =>
                                        {
                                            try
                                            {
                                                return await this.DownloadAsync(
                                                    image,
                                                    entryMap[image.Key],
                                                    tracker,
                                                    dbTracker);
                                            }
                                            finally
                                            {
                                                throttler.Release();
                                            }
                                        }));
                                }
                                await Task.WhenAll(tasks);
                                this.Logger.LogInformation("Downloads Complete");
                            }
                            else
                            {
                                this.Logger.LogInformation("No Pending Images");
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }
                catch (Exception e)
                {
                    this.Logger.LogError("Exception: {0}", e.ToString());
                }
            }
        }

        public async Task ResetAsync(ServiceProvider provider)
        {
            var dbContext = provider.GetService<BucketMonitorContext>();
            await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE IMAGE");
        }

        public async Task Summarize(ServiceProvider provider)
        {
            var dbContext = provider.GetService<BucketMonitorContext>();
            var bucket = await this.GetBucketAsync(dbContext);

            var images = await this.ListImagesAsync(dbContext, bucket);
            this.Summarize(images, provider);
        }

        public async Task SummarizeCached(ServiceProvider provider)
        {
            var dbContext = provider.GetService<BucketMonitorContext>();
            var bucket = await this.GetBucketAsync(dbContext);

            var images = bucket.Images.Select(x => this.GetFromCacheEntry(x));
            this.Summarize(images, provider);
        }


        private void Summarize(
            IEnumerable<SourceImage> images,
            ServiceProvider provider)
        {
            var imageMap = images
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified)
                .GroupBy(x => x.Status)
                .ToDictionary(x => x.Key, x => x.ToList());

            var table = new ConsoleTable("Status", "Total Count");
            table.Options.EnableCount = false;


            table.AddRow(this.ToName(ImageStatus.Completed), (imageMap.GetValueOrDefault(ImageStatus.Completed)?.Count ?? 0).ToString());
            table.AddRow(this.ToName(ImageStatus.Pending), (imageMap.GetValueOrDefault(ImageStatus.Pending)?.Count ?? 0).ToString());
            table.AddRow(this.ToName(ImageStatus.Failed), (imageMap.GetValueOrDefault(ImageStatus.Failed)?.Count ?? 0).ToString());
            table.AddRow(this.ToName(ImageStatus.Skipped), (imageMap.GetValueOrDefault(ImageStatus.Skipped)?.Count ?? 0).ToString());

            table.Write();
        }

        private async Task<Bucket> GetBucketAsync(BucketMonitorContext context, bool tracked = false)
        {
            Bucket bucket;

            if (tracked)
            {
                bucket = await context.Bucket
                    .SingleOrDefaultAsync(x => x.Name == this.BucketName);
            }
            else
            {
                bucket = await context.Bucket
                    .Include(x => x.Images)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Name == this.BucketName);
            }

            if (bucket != null)
            {
                return bucket;
            }
            else
            {
                throw new Exception(string.Format("Bucket Not Configured: {0}", this.BucketName));
            }
        }
        
        private string ToName(ImageStatus status) => Enum.GetName(typeof(ImageStatus), status);

        public async Task ConfigureBucketAsync(ServiceProvider provider)
        {
            using (var dbContext = provider.GetService<BucketMonitorContext>())
            {
                dbContext.Add(new Bucket()
                {
                    Name = this.BucketName
                });

                await dbContext.SaveChangesAsync();
            }
        }

        public async Task DisplayImages(
            ServiceProvider provider,
            ImageStatus? filter = null)
        {
            var dbContext = provider.GetService<BucketMonitorContext>();
            var bucket = await this.GetBucketAsync(dbContext);

            var images = await this.ListImagesAsync(
                dbContext: dbContext,
                bucket: bucket);

            this.DisplayImages(images
                .Where(x => filter.HasValue ? x.Status == filter : true)
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified));
        }


        public async Task DisplayCachedImages(
            ServiceProvider provider,
            ImageStatus? filter = null)
        {
            var dbContext = provider.GetService<BucketMonitorContext>();
            var bucket = await this.GetBucketAsync(dbContext);
            var images = bucket.Images.Select(x => this.GetFromCacheEntry(x));

            this.DisplayImages(images
                .Where(x => filter.HasValue ? x.Status == filter : true)
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified));
        }

        private void DisplayImages(IEnumerable<SourceImage> images)
        {
            this.Logger.LogInformation("Showing {0} objects...", images.Count());

            var table = new ConsoleTable("Key", "Last Modified", "Status", "Path");
            table.Options.EnableCount = false;
            foreach (var image in images)
            {
                table.AddRow(
                    image.Key,
                    image.LastModified,
                    Enum.GetName(typeof(ImageStatus), image.Status),
                    image.File?.FullName ?? "-"); 
            }
            table.Write();
            Console.WriteLine();
        }

        private SourceImage GetFromCacheEntry(
            ImageEntry entry)
        {
            return new SourceImage(
                entry.Key,
                this.TryConvertPath(entry.Key, out var file) ? file : null,
                entry.LastModified,
                entry.FileSize,
                entry.Status);
        }

        private SourceImage ProcessObject(
            BucketMonitorContext dbContext,
            Bucket tracked,
            S3Object obj,
            Dictionary<string, ImageEntry> entries)
        {
            if (entries.TryGetValue(obj.Key, out var entry))
            {
                return this.GetFromCacheEntry(entry);
            }
            else
            {
                var image = this.CreateSourceImage(
                    key: obj.Key,
                    lastModified: obj.LastModified,
                    totalBytes: obj.Size);

                dbContext.Image.Add(new ImageEntry()
                {
                    Bucket = tracked,
                    Key = obj.Key,
                    LastModified = obj.LastModified,
                    FileSize = obj.Size,
                    Status = image.Status
                });

                return image;
            }
        } 

        private async Task<IEnumerable<SourceImage>> ListImagesAsync(
            BucketMonitorContext dbContext,
            Bucket bucket)
        {
            var entries = bucket.Images.ToDictionary(x => x.Key);

            var results = new List<SourceImage>();
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = this.BucketName
            };

            var console = new ConsoleString();
            var tracked = await this.GetBucketAsync(dbContext, tracked: true);


            int count = 0;
            ListObjectsV2Response response;
            do
            {
                console.Update($"Querying {results.Count()} Objects...");
                response = await this.Client.ListObjectsV2Async(request);

                var images = new List<SourceImage>();

                foreach (var obj in response.S3Objects)
                {
                    var image = this.ProcessObject(
                        dbContext: dbContext,
                        tracked: tracked,
                        obj: obj,
                        entries: entries);

                    images.Add(image);
                }

                await dbContext.SaveChangesAsync();

                results.AddRange(images);
                request.ContinuationToken = response.NextContinuationToken;
                count++;

            }
            while (response.IsTruncated);

            console.Update("");
            Console.WriteLine();
            this.Logger.LogInformation($"Queried {results.Count()} Objects");

            Console.Out.Flush();

            return results;
        }

        public bool TryConvertPath(string key, out FileInfo file)
        {
            return TryConvertPath(key, this.DriveLetter, out file);
        }

        public static bool TryConvertPath(string key, char driveLetter, out FileInfo file)
        {
            if (key.EndsWith("/") || Path.IsPathRooted(key))
            {
                file = default(FileInfo);
                return false;
            }
            else
            {
                var path = Path.Combine($"{driveLetter}:\\", key);
                file = new FileInfo(path); 
                return true;
            }
        }

        public async Task<FileInfo> DownloadAsync(
            SourceImage image,
            ImageEntry entry,
            DownloadTracker tracker,
            DatabaseTracker dbTracker)
        {
            var file = image.File;
            var started = false;
            long downloaded = 0;

            try
            {
                var response = await this.Client.GetObjectAsync(new GetObjectRequest()
                {
                    BucketName = this.BucketName,
                    Key = image.Key
                });

                var tmp = Path.GetTempFileName();

                long transferred = 0;
                response.WriteObjectProgressEvent += (sender, p) =>
                {
                    if (!started)
                    {
                        tracker.Start();
                    }
                    started = true;

                    downloaded = p.TransferredBytes;
                    tracker.Downloaded(downloaded - transferred);
                    transferred = downloaded;
                };

                await response.WriteResponseStreamToFileAsync(tmp, false, CancellationToken.None);

                if (this.DebugMode)
                {
                    File.Delete(tmp);
                }
                else
                {
                    if (!this.SafeMove(tmp, file.FullName))
                    {
                        image.MarkFailed();
                        dbTracker.Update(entry, ImageStatus.Failed);
                        tracker.Fail();
                        return null;
                    }
                }

                this.Logger.LogDebug("Download Complete: {0} -> {1}", image.Key, file?.FullName ?? "-");

                image.MarkCompleted();
                dbTracker.Update(entry, ImageStatus.Completed);

                if (started)
                {
                    tracker.Complete();
                }

                return file;
            }
            catch (Exception ec)
            {
                this.Logger.LogDebug("Download Failed:  {0} -> {1}", image.Key, file?.FullName ?? "-");
                this.Logger.LogDebug("Exception: {0}", ec.ToString());

                image.MarkFailed();
                dbTracker.Update(entry, ImageStatus.Failed);

                if (started)
                {
                    tracker.Fail();
                    tracker.Downloaded(image.TotalBytes - downloaded);
                }

                return null;
            }
        }

        private bool SafeMove(string source, string destination)
        {
            try
            {
                var sourceFile = new FileInfo(source); 
                var destinationFile = new FileInfo(destination); 

                if (sourceFile.Length == destinationFile.Length)
                {
                    File.Move(source, destination, overwrite: true);
                }
                else
                {
                    this.Logger.LogDebug(
                        "Cancelling Move Operation. Source file length ({0}) does not match destination ({1}): {2}",
                        sourceFile.Length,
                        destinationFile.Length,
                        destination);

                    File.Delete(source);
                }

                return true;
            }
            catch (Exception ec)
            {
                this.Logger.LogDebug("Exception Moving File {0} -> {1}: {2}", source, destination, ec.ToString());
                if (File.Exists(source))
                {
                    File.Delete(source);
                }

                return false;
            }
        }
    }
}
