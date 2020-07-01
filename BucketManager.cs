namespace BucketMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    using Amazon.S3;
    using Amazon.S3.Model;

    using ConsoleTables;
    using System.Runtime.CompilerServices;

    public class BucketManager
    {
        public static int MAX_PATH_LENGTH { get; } = 185;

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
            this.IncludedPaths = settings.IncludedPaths ?? new List<string>();
            this.Logger = logger;
        }

        public TimeSpan PollingInterval { get; }

        public string BucketName { get; }

        public char DriveLetter { get; }

        private AmazonS3Client Client { get; }

        private int MaxDownloads { get; set; }

        private bool DebugMode { get; }

        private ILogger Logger { get; }

        private IEnumerable<string> IncludedPaths { get; }

        public async Task<IEnumerable<SourceImage>> ListPendingAsync(ServiceProvider provider)
        {
            return (await this.ListImagesAsync(provider))
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
                            this.Logger.LogDebug("Scanning Bucket {0}", this.BucketName);
                            lastCheck = DateTime.Now;

                            Console.WriteLine("\n=> Scanning Bucket {0} ({1})", this.BucketName, DateTime.Now);

                            var pending = await this.ListPendingAsync(provider);
                            var count = pending.Count();
                            var tracker = new DownloadTracker(count, pending.Select(x => x.TotalBytes).Sum());
                            var dbTracker = new DatabaseTracker(dbContext: dbContext);

                            var entryMap = await this.GetImageMapAsync(dbContext);
                            if (count > 0)
                            {
                                this.Logger.LogDebug("Downloading {0} Pending Images", pending.Count());
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

                                Console.WriteLine();
                                Console.WriteLine("Downloads Complete");
                                this.Logger.LogDebug("Downloads Complete");

                            }
                            else
                            {
                                Console.WriteLine("No Pending Images");
                                this.Logger.LogDebug("No Pending Images");
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
            var toDelete = dbContext.Image.Where(x => x.Bucket.Name == this.BucketName);
            dbContext.Image.RemoveRange(toDelete);
            await dbContext.SaveChangesAsync();
        }

        public async Task Summarize(ServiceProvider provider)
        {
            var images = await this.ListImagesAsync(provider);
            this.Summarize(images, provider);
        }

        public async Task SummarizeCached(ServiceProvider provider)
        {
            var dbContext = provider.GetService<BucketMonitorContext>();
            var images = await this.GetCachedImagesAsync(dbContext);
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

            Console.WriteLine();
            Console.WriteLine();
            table.Write();
        }

        private async Task<IEnumerable<SourceImage>> GetCachedImagesAsync(BucketMonitorContext context)
        {
            return (await context.Image
                .AsNoTracking()
                .ToListAsync())
                .Select(x => this.GetFromCacheEntry(x));
        }
        
        public async Task ValidateBucket(BucketMonitorContext context)
        {
           var bucket = await context.Bucket
                .AsNoTracking()
                .Where(x => x.Name == this.BucketName)
                .SingleOrDefaultAsync();

            if (bucket == null)
            {
                this.Logger.LogError("Bucket is not configured: {0}", this.BucketName);
                throw new Exception();
            }
        }

        private async Task<IDictionary<string, ImageEntry>> GetImageMapAsync(BucketMonitorContext context)
        {
            return await context.Image.AsNoTracking().ToDictionaryAsync(key => key.Key);
        }

        private async Task<Bucket> GetTrackedBucket(BucketMonitorContext context)
        {
            return await context.Bucket
                .SingleOrDefaultAsync(x => x.Name == this.BucketName);
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
            ISet<ImageStatus> filter = null)
        {
            var images = await this.ListImagesAsync(
                provider: provider);

            this.DisplayImages(images
                .Where(x => filter?.Contains(x.Status) != false)
                .OrderBy(x => x.Status)
                .ThenBy(x => x.LastModified));
        }


        public async Task DisplayCachedImages(
            ServiceProvider provider,
            ISet<ImageStatus> filter = null)
        {
            var dbContext = provider.GetService<BucketMonitorContext>();
            var images = await this.GetCachedImagesAsync(dbContext);

            this.DisplayImages(images
                .Where(x => filter?.Contains(x.Status) != false)
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
            FileInfo file = null;
            var status = this.TryConvertPath(entry.Key, out file) ? entry.Status : ImageStatus.Skipped;

            return new SourceImage(
                entry.Key,
                file,
                entry.LastModified,
                entry.FileSize,
                status);
        }

        private SourceImage ProcessObject(
            Bucket tracked,
            S3Object obj,
            IDictionary<string, ImageEntry> entries,
            IList<ImageEntry> toAdd)
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

                toAdd.Add(new ImageEntry()
                {
                    Bucket = tracked,
                    Key = obj.Key,
                    LastModified = obj.LastModified,
                    FileSize = obj.Size,
                    Status = image.Status == ImageStatus.Skipped ?
                        ImageStatus.Pending : image.Status
                });

                return image;
            }
        } 

        private async Task<IEnumerable<SourceImage>> ListImagesAsync(
            ServiceProvider provider)
        {
            var entries = await this.GetImageMapAsync(provider.GetService<BucketMonitorContext>());

            var results = new List<SourceImage>();
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = this.BucketName
            };

            var console = new ConsoleString();
            console.Update($"Loading Objects...");

            int cacheAdditions = 0;
            int count = 0;
            ListObjectsV2Response response;
            do
            {
                long total = 0;
                using (var scoped = provider.CreateScope())
                {
                    var dbContext = scoped.ServiceProvider.GetService<BucketMonitorContext>();
                    dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

                    var tracked = await this.GetTrackedBucket(dbContext);

                    response = await this.Client.ListObjectsV2Async(request);

                    var images = new List<SourceImage>();
                    var toAdd = new List<ImageEntry>();

                    total += response.S3Objects.Count();

                    var filtered = response.S3Objects
                        .Where(x => x.Key.Length <= MAX_PATH_LENGTH)
                        .Where(x => this.IsIncludedPath(x.Key));

                    foreach (var obj in filtered)
                    {
                        var image = this.ProcessObject(
                            tracked: tracked,
                            obj: obj,
                            entries: entries,
                            toAdd: toAdd);

                        images.Add(image);
                    }

                    if (toAdd.Count() > 0)
                    {
                        dbContext.Image.AddRange(toAdd);
                        await dbContext.SaveChangesAsync();
                        cacheAdditions += toAdd.Count();
                    }

                    results.AddRange(images);
                    request.ContinuationToken = response.NextContinuationToken;
                    count++;

                    if (cacheAdditions > 0)
                    {
                        console.Update($"Loading Objects: {total}, CacheAdditions={cacheAdditions}");
                    }
                    else
                    {
                        console.Update($"Loading Objects: {total}");
                    }

                }

            }
            while (response.IsTruncated);
            console.Update($"Loaded {results.Count()} Objects");
            this.Logger.LogDebug($"Queried {results.Count()} Objects");

            return results;
        }

        public bool TryConvertPath(string key, out FileInfo file)
        {
            return TryConvertPath(
                key,
                this.DriveLetter,
                this.IncludedPaths,
                out file);
        }

        public bool IsIncludedPath(string key) => IsIncludedPath(key, this.IncludedPaths);

        public static bool IsIncludedPath(string key, IEnumerable<string> included) => included.Count() == 0 || included.Any(x => key.StartsWith($"{x}/"));

        public static bool TryConvertPath(string key, char driveLetter, IEnumerable<string> included, out FileInfo file)
        {
            if (!IsIncludedPath(key, included))
            {
                file = default(FileInfo);
                return false;
            }
            else if (key.EndsWith("/") || Path.IsPathRooted(key))
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
                if (!file.Exists)
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
                }
                else
                {
                    this.Logger.LogDebug("Skipping Existing File: {0} -> {1}", image.Key, file?.FullName ?? "-");
                    started = true;
                    tracker.Start();
                    tracker.Downloaded(image.TotalBytes);
                }

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

                if (!destinationFile.Directory.Exists)
                {
                    Directory.CreateDirectory(destinationFile.Directory.FullName);
                    this.Logger.LogDebug($"Created Directory: {destinationFile.Directory.FullName}");
                }

                File.Move(source, destination);
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
